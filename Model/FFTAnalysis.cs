using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Numerics;
using System.Windows;

namespace PowerScope.Model
{
    /// <summary>
    /// Performs FFT spectrum analysis for a single FFT measurement: computes the magnitude
    /// spectrum and peak frequency, tracks the top peaks for display, and exposes bindable
    /// settings (size, interpolation, window function, averaging) for the FFT spectrum window.
    /// Has no knowledge of the spectrum window or its plot - those are the View's job.
    /// </summary>
    public class FFTAnalysis : INotifyPropertyChanged
    {
        private readonly IDataStream _dataStream;
        private readonly ChannelSettings _channelSettings;

        private int _size = 4096;
        private int _interpolation = 1; // Zero-padding factor
        private string _windowFunction = "Blackman-Harris";
        private int _averaging = 1;

        // Pre-calculated window function coefficients for performance optimization
        private double[] _windowCoefficients;
        private bool _windowCoefficientsValid;

        private double[] _spectrumFrequencies;
        private double[] _spectrumMagnitudes;

        // Top peaks tracked across updates, for the frequency analysis grid
        private readonly List<(double frequency, double magnitude)> _peaks = new List<(double, double)>();
        private const int MaxPeaksToTrack = 10;

        private View.UserForms.SortColumn _sortColumn = View.UserForms.SortColumn.Amplitude;
        private View.UserForms.SortDirection _sortDirection = View.UserForms.SortDirection.Descending;

        public FFTAnalysis(IDataStream dataStream, ChannelSettings channelSettings)
        {
            _dataStream = dataStream;
            _channelSettings = channelSettings;

            RebuildSpectrumArrays();
            PreCalculateWindowCoefficients();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        /// <summary>
        /// Raised after Update() recomputes the spectrum (only while ComputeSpectrum is true).
        /// The spectrum window subscribes to this to know when to refresh its plot.
        /// </summary>
        public event EventHandler SpectrumUpdated;

        /// <summary>
        /// Whether the spectrum arrays should be (re)computed on each Update. Peaks are always
        /// computed regardless of this flag. The spectrum window sets this true while open and
        /// false once closed, since the arrays are only ever consumed for plotting.
        /// </summary>
        public bool ComputeSpectrum { get; set; }

        public double[] SpectrumFrequencies => _spectrumFrequencies;
        public double[] SpectrumMagnitudes => _spectrumMagnitudes;

        public double SampleRate => _dataStream.SampleRate;
        public string ChannelLabel => _channelSettings.Label;
        public System.Windows.Media.Color ChannelColor => _channelSettings.Color;

        /// <summary>
        /// Top frequency peaks for display, sorted according to the current sort preference.
        /// </summary>
        public ObservableCollection<FFTPeakData> Peaks { get; } = new ObservableCollection<FFTPeakData>();

        /// <summary>
        /// FFT size (number of samples for FFT calculation). Must be a power of 2, range 128-16384.
        /// </summary>
        public int Size
        {
            get => _size;
            set
            {
                _size = value;
                OnPropertyChanged(nameof(Size));

                _windowCoefficientsValid = false;
                PreCalculateWindowCoefficients();
                RebuildSpectrumArrays();
            }
        }

        /// <summary>
        /// Interpolation factor (zero-padding factor). Valid values: 1, 2, 4, 8.
        /// </summary>
        public int Interpolation
        {
            get => _interpolation;
            set
            {
                _interpolation = value;
                OnPropertyChanged(nameof(Interpolation));
                RebuildSpectrumArrays();
            }
        }

        /// <summary>
        /// Window function for FFT. Valid values: "Blackman-Harris", "Hann", "Flat Top", "None".
        /// </summary>
        public string WindowFunction
        {
            get => _windowFunction;
            set
            {
                _windowFunction = value;
                OnPropertyChanged(nameof(WindowFunction));

                _windowCoefficientsValid = false;
                PreCalculateWindowCoefficients();
            }
        }

        /// <summary>
        /// Averaging factor for FFT spectrum smoothing. Valid values: 1, 2, 4, 8, 16.
        /// </summary>
        public int Averaging
        {
            get => _averaging;
            set
            {
                _averaging = value;
                OnPropertyChanged(nameof(Averaging));
            }
        }

        private void RebuildSpectrumArrays()
        {
            int newSpectrumLength = _size * _interpolation / 2;
            _spectrumFrequencies = new double[newSpectrumLength];
            _spectrumMagnitudes = new double[newSpectrumLength];
        }

        /// <summary>
        /// Sort the peaks collection by the specified column and direction.
        /// Called from the FFT window when the user clicks a column header.
        /// </summary>
        public void SortPeaks(View.UserForms.SortColumn column, View.UserForms.SortDirection direction)
        {
            _sortColumn = column;
            _sortDirection = direction;
            ApplyPeakSorting();
        }

        /// <summary>
        /// Gets the current peak sorting preference. Used by the FFT window to initialize its
        /// sort indicators.
        /// </summary>
        public (View.UserForms.SortColumn column, View.UserForms.SortDirection direction) GetSortState()
        {
            return (_sortColumn, _sortDirection);
        }

        private void ApplyPeakSorting()
        {
            if (_peaks.Count == 0)
                return;

            switch (_sortColumn)
            {
                case View.UserForms.SortColumn.Frequency:
                    _peaks.Sort((a, b) => _sortDirection == View.UserForms.SortDirection.Ascending
                        ? a.frequency.CompareTo(b.frequency)
                        : b.frequency.CompareTo(a.frequency));
                    break;

                case View.UserForms.SortColumn.Amplitude:
                case View.UserForms.SortColumn.AmplitudeDb: // Both sort by magnitude since dB is just a transform
                    _peaks.Sort((a, b) => _sortDirection == View.UserForms.SortDirection.Ascending
                        ? a.magnitude.CompareTo(b.magnitude)
                        : b.magnitude.CompareTo(a.magnitude));
                    break;
            }

            // Update() runs on a background thread (MeasurementBar updates measurements via
            // Task.Run/Parallel.ForEach), but Peaks is an ObservableCollection bound to the FFT
            // window's UI - mutating it off the UI thread throws, so marshal the mutation over.
            List<(double frequency, double magnitude)> topPeaks = _peaks.Take(MaxPeaksToTrack).ToList();
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                Peaks.Clear();
                foreach ((double frequency, double magnitude) peak in topPeaks)
                {
                    Peaks.Add(new FFTPeakData(peak.frequency, peak.magnitude));
                }
            }));
        }

        public void ClearPeaks()
        {
            _peaks.Clear();
            Peaks.Clear();
        }

        /// <summary>
        /// Runs the FFT on the supplied data and returns the peak frequency. Always updates the
        /// Peaks collection; updates the spectrum arrays only while ComputeSpectrum is true.
        /// </summary>
        public double Update(ReadOnlySpan<double> data)
        {
            int effectiveSize = _size * _interpolation;

            Aelian.FFT.SignalData fftData = Aelian.FFT.SignalData.CreateFromRealSize(effectiveSize);
            Span<double> realPart = fftData.AsReal();

            int copyLength = Math.Min(data.Length, _size);
            ApplyWindowFunction(data, realPart, copyLength);

            // Zero-pad the rest for interpolation
            for (int i = copyLength; i < effectiveSize; i++)
            {
                realPart[i] = 0.0;
            }

            Aelian.FFT.FastFourierTransform.RealFFT(realPart, true);

            Span<Complex> complexData = fftData.AsComplex();
            double maxMagnitude = 0.0;
            int peakBin = 0;

            int spectrumLength = effectiveSize / 2;

            double frequencyResolution = SampleRate / effectiveSize;

            // Calculate proper scaling factors for magnitude correction
            double fftScaling = 2.0 / _size; // Factor of 2 for single-sided spectrum, divide by N for FFT scaling
            double windowAmplitudeCorrection = GetWindowAmplitudeCorrection(_windowFunction);

            _peaks.Clear();

            double[] magnitudes = new double[spectrumLength];

            for (int i = 0; i < spectrumLength; i++)
            {
                double magnitude = Math.Sqrt(complexData[i].Real * complexData[i].Real +
                                            complexData[i].Imaginary * complexData[i].Imaginary);

                magnitude = magnitude * fftScaling * windowAmplitudeCorrection;

                // Special case for DC component (i=0) - no factor of 2 needed for single-sided spectrum
                if (i == 0)
                {
                    magnitude = magnitude / 2.0;
                }

                magnitudes[i] = magnitude;
                double frequency = i * frequencyResolution;

                if (magnitude > maxMagnitude)
                {
                    maxMagnitude = magnitude;
                    peakBin = i;
                }

                if (ComputeSpectrum)
                {
                    _spectrumFrequencies[i] = frequency;

                    // Apply averaging smoothing (exponential moving average simulation)
                    if (_averaging > 1 && i < _spectrumMagnitudes.Length)
                    {
                        double alpha = 2.0 / (_averaging + 1.0); // EMA smoothing factor
                        double currentMagdB = 20.0 * Math.Log10(Math.Max(magnitude, 1e-10));
                        _spectrumMagnitudes[i] = alpha * currentMagdB + (1.0 - alpha) * _spectrumMagnitudes[i];
                    }
                    else
                    {
                        _spectrumMagnitudes[i] = 20.0 * Math.Log10(Math.Max(magnitude, 1e-10));
                    }
                }
            }

            // Find peaks with windowing (+-5 bins)
            const int peakWindow = 5;
            const double peakThreshold = 0.001; // Threshold to avoid noise

            for (int i = 1; i < spectrumLength - 1; i++) // Skip DC (i=0) and last bin
            {
                double currentMagnitude = magnitudes[i];
                if (currentMagnitude <= peakThreshold)
                    continue;

                bool isLocalMaximum = true;
                int windowStart = Math.Max(1, i - peakWindow); // Don't include DC component
                int windowEnd = Math.Min(spectrumLength - 1, i + peakWindow);

                for (int j = windowStart; j <= windowEnd; j++)
                {
                    if (j != i && magnitudes[j] > currentMagnitude)
                    {
                        isLocalMaximum = false;
                        break;
                    }
                }

                if (isLocalMaximum)
                {
                    double frequency = i * frequencyResolution;
                    _peaks.Add((frequency, currentMagnitude));
                }
            }

            if (_peaks.Count > MaxPeaksToTrack)
            {
                _peaks.Sort((a, b) => b.magnitude.CompareTo(a.magnitude));
                _peaks.RemoveRange(MaxPeaksToTrack, _peaks.Count - MaxPeaksToTrack);
            }

            ApplyPeakSorting();

            if (ComputeSpectrum)
                SpectrumUpdated?.Invoke(this, EventArgs.Empty);

            double peakFrequency = peakBin * frequencyResolution;
            return peakFrequency;
        }

        /// <summary>
        /// Get the amplitude correction factor for the specified window function.
        /// These factors compensate for the amplitude loss caused by windowing.
        /// </summary>
        private double GetWindowAmplitudeCorrection(string windowFunction)
        {
            switch (windowFunction)
            {
                case "Hann":
                    return 2.0; // Hann window has coherent gain of 0.5, so correction is 2.0

                case "Blackman-Harris":
                    return 2.79; // Blackman-Harris window has coherent gain of ~0.358, so correction is ~2.79

                case "Flat Top":
                    return 4.64; // Flat Top window has coherent gain of ~0.215, so correction is ~4.64

                case "None":
                default:
                    return 1.0; // Rectangular window (no windowing) needs no correction
            }
        }

        /// <summary>
        /// Apply the selected window function to the input data using pre-calculated coefficients.
        /// </summary>
        private void ApplyWindowFunction(ReadOnlySpan<double> input, Span<double> output, int length)
        {
            if (!_windowCoefficientsValid || _windowCoefficients == null || _windowCoefficients.Length != _size)
            {
                PreCalculateWindowCoefficients();
            }

            int applyLength = Math.Min(length, _windowCoefficients.Length);
            for (int i = 0; i < applyLength; i++)
            {
                output[i] = input[i] * _windowCoefficients[i];
            }
        }

        /// <summary>
        /// Pre-calculate window function coefficients for optimal performance.
        /// Called when FFT size or window function changes.
        /// </summary>
        private void PreCalculateWindowCoefficients()
        {
            if (_windowCoefficients == null || _windowCoefficients.Length != _size)
            {
                _windowCoefficients = new double[_size];
            }

            switch (_windowFunction)
            {
                case "Hann":
                    PreCalculateHannWindow();
                    break;
                case "Flat Top":
                    PreCalculateFlatTopWindow();
                    break;
                case "None":
                    PreCalculateRectangularWindow();
                    break;
                case "Blackman-Harris":
                default:
                    PreCalculateBlackmanHarrisWindow();
                    break;
            }

            _windowCoefficientsValid = true;
        }

        private void PreCalculateBlackmanHarrisWindow()
        {
            const double a0 = 0.35875;
            const double a1 = 0.48829;
            const double a2 = 0.14128;
            const double a3 = 0.01168;

            for (int i = 0; i < _size; i++)
            {
                double n = i / (double)(_size - 1);
                _windowCoefficients[i] = a0 - a1 * Math.Cos(2.0 * Math.PI * n) +
                                        a2 * Math.Cos(4.0 * Math.PI * n) -
                                        a3 * Math.Cos(6.0 * Math.PI * n);
            }
        }

        private void PreCalculateHannWindow()
        {
            for (int i = 0; i < _size; i++)
            {
                _windowCoefficients[i] = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (_size - 1)));
            }
        }

        private void PreCalculateFlatTopWindow()
        {
            const double a0 = 0.21557895;
            const double a1 = 0.41663158;
            const double a2 = 0.277263158;
            const double a3 = 0.083578947;
            const double a4 = 0.006947368;

            for (int i = 0; i < _size; i++)
            {
                double n = i / (double)(_size - 1);
                _windowCoefficients[i] = a0 - a1 * Math.Cos(2.0 * Math.PI * n) +
                                        a2 * Math.Cos(4.0 * Math.PI * n) -
                                        a3 * Math.Cos(6.0 * Math.PI * n) +
                                        a4 * Math.Cos(8.0 * Math.PI * n);
            }
        }

        private void PreCalculateRectangularWindow()
        {
            for (int i = 0; i < _size; i++)
            {
                _windowCoefficients[i] = 1.0;
            }
        }
    }

    /// <summary>
    /// Represents a single FFT peak with frequency and amplitude data for data binding
    /// </summary>
    public class FFTPeakData
    {
        public double Frequency { get; }
        public double Magnitude { get; }

        public FFTPeakData(double frequency, double magnitude)
        {
            Frequency = frequency;
            Magnitude = magnitude;
        }

        /// <summary>
        /// Formatted frequency string with appropriate units (Hz, kHz, MHz)
        /// </summary>
        public string FrequencyText
        {
            get
            {
                if (Frequency >= 1000000)
                {
                    return $"{Frequency / 1000000.0:F2} MHz";
                }
                else if (Frequency >= 1000)
                {
                    return $"{Frequency / 1000.0:F1} kHz";
                }
                else
                {
                    return $"{Frequency:F1} Hz";
                }
            }
        }

        /// <summary>
        /// Formatted amplitude string in linear scale
        /// </summary>
        public string AmplitudeText
        {
            get
            {
                if (Magnitude >= 0.001)
                {
                    return $"{Magnitude:F4}";
                }
                else if (Magnitude > 0)
                {
                    return $"{Magnitude:E2}";
                }
                else
                {
                    return "0";
                }
            }
        }

        /// <summary>
        /// Formatted amplitude string in dB scale
        /// </summary>
        public string AmplitudeDbText
        {
            get
            {
                double amplitudeDb = Magnitude > 0 ? 20.0 * Math.Log10(Magnitude) : -100.0;
                return $"{amplitudeDb:F1}";
            }
        }
    }
}
