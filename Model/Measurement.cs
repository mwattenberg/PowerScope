using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using Aelian.FFT;

namespace PowerScope.Model
{
    /// <summary>
    /// Measurement types that can be calculated
    /// </summary>
    public enum MeasurementType
    {
        Minimum,
        Maximum,
        Mean,
        Rms,
        StandardDeviation,
        Variance,
        PeakToPeak,
        FFT
    }

    /// <summary>
    /// Delegate for measurement calculation function
    /// </summary>
    public delegate double CalculationFunction(ReadOnlySpan<double> data);

    /// <summary>
    /// Self-contained measurement class that manages its own data copying and calculations
    /// Takes a DataStream and channel index, handles all data management internally
    /// Updates are now managed externally by SystemManager
    /// Extended to work directly with WPF data binding and templates
    /// </summary>
    public class Measurement : INotifyPropertyChanged, IDisposable
    {
        private readonly MeasurementType _measurementType;
        private readonly CalculationFunction _calculationFunction;
        private readonly IDataStream _dataStream;
        private readonly int _channelIndex;
        private readonly double[] _dataBuffer;
        private readonly ChannelSettings _channelSettings;
        
        // Single measurement result
        private double _result = 0.0;
        private bool _disposed = false;

        // FFT-specific data for spectrum display
        private double[] _spectrumFrequencies;
        private double[] _spectrumMagnitudes;
        private View.UserForms.FFT _fftWindow;

        // Cached FFT plot signal for efficient updates (PlotManager pattern)
        private ScottPlot.Plottables.SignalXY _spectrumSignal;
        private int _lastSpectrumLength = -1; // Track when we need to rebuild the signal

        // FFT configuration properties
        private int _FFT_Size = 4096;
        private int _FFT_interpolation = 1; // Zero-padding factor
        private string _FFT_windowFunction = "Blackman-Harris";
        private int _FFT_averaging = 1;

        // Pre-calculated window function coefficients for performance optimization
        private double[] _FFT_windowCoefficients;
        private bool _FFT_windowCoefficientsValid = false;

        // Events
        public event EventHandler RemoveRequested;
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Constructor with measurement type, data stream, channel, and channel settings
        /// </summary>
        /// <param name="measurementType">Type of measurement to perform</param>
        /// <param name="dataStream">Data stream to read from</param>
        /// <param name="channelIndex">Zero-based channel index</param>
        /// <param name="channelSettings">Channel settings for display (color, label, etc.)</param>
        public Measurement(MeasurementType measurementType, IDataStream dataStream, int channelIndex, ChannelSettings channelSettings)
        {
            _measurementType = measurementType;
            _dataStream = dataStream;
            _channelIndex = channelIndex;
            _channelSettings = channelSettings;
            
            // For FFT measurements, ensure buffer is large enough for maximum FFT size
            // For other measurements, use a reasonable default
            int bufferSize;
            if (measurementType == MeasurementType.FFT)
                bufferSize = 16384; // Use maximum possible FFT size instead of current _fftSize
            else
                bufferSize = 5000;

            _dataBuffer = new double[bufferSize];
            
            _calculationFunction = GetCalculationFunction(measurementType);
            
            // Pre-calculate window coefficients for FFT measurements
            if (measurementType == MeasurementType.FFT)
            {
                // For FFT measurements we need an instance-bound calculation function
                // Traditional C-style: assign function pointer directly
                _calculationFunction = CalculateFFT;
                FFT_PreCalculateWindowCoefficients();
            }
        }

        /// <summary>
        /// Type of measurement being performed (read-only)
        /// </summary>
        public MeasurementType Type
        {
            get
            {
                return _measurementType;
            }
        }

        /// <summary>
        /// Display name for the measurement type
        /// </summary>
        public string TypeDisplayName
        {
            get
            {
                return _measurementType.ToString();
            }
        }

        /// <summary>
        /// The channel settings associated with this measurement (for color, label, etc.)
        /// </summary>
        public ChannelSettings ChannelSettings
        {
            get
            {
                return _channelSettings;
            }
        }

        /// <summary>
        /// Whether this measurement has been disposed
        /// </summary>
        public bool IsDisposed
        {
            get
            {
                return _disposed;
            }
        }

        /// <summary>
        /// The calculated result value
        /// </summary>
        public double Result
        {
            get
            {
                return _result;
            }
            private set
            {
                    _result = value;
                    OnPropertyChanged(nameof(Result));

            }
        }



        /// <summary>
        /// FFT size (number of samples for FFT calculation)
        /// Must be a power of 2, range 128-16384
        /// </summary>
        public int FFT_Size
        {
            get
            {
                return _FFT_Size;
            }
            set
            {

                _FFT_Size = value;
                OnPropertyChanged(nameof(FFT_Size));
                    
                // Invalidate window coefficients since FFT size changed
                _FFT_windowCoefficientsValid = false;
                FFT_PreCalculateWindowCoefficients();
                    
                // Invalidate cached spectrum signal since data length will change
                _lastSpectrumLength = -1;
                    

                int newSpectrumLength = _FFT_Size * _FFT_interpolation / 2;
                _spectrumFrequencies = new double[newSpectrumLength];
                _spectrumMagnitudes = new double[newSpectrumLength];
                        

            }
        }

        /// <summary>
        /// Interpolation factor (zero-padding factor)
        /// Valid values: 1, 2, 4, 8
        /// </summary>
        public int FFT_Interpolation
        {
            get
            {
                return _FFT_interpolation;
            }
            set
            {
                _FFT_interpolation = value;
                OnPropertyChanged(nameof(FFT_Interpolation));
                    
                // Invalidate cached spectrum signal since data length will change
                _lastSpectrumLength = -1;
                    
                int newSpectrumLength = _FFT_Size * _FFT_interpolation / 2;
                _spectrumFrequencies = new double[newSpectrumLength];
                _spectrumMagnitudes = new double[newSpectrumLength];
            }
        }

        /// <summary>
        /// Window function for FFT
        /// Valid values: "Blackman-Harris", "Hann", "Flat Top", "None"
        /// </summary>
        public string FFT_WindowFunction
        {
            get
            {
                return _FFT_windowFunction;
            }
            set
            {
                string validatedValue = value;

                _FFT_windowFunction = validatedValue;
                OnPropertyChanged(nameof(FFT_WindowFunction));
                    
                // Invalidate window coefficients since window function changed
                _FFT_windowCoefficientsValid = false;
                FFT_PreCalculateWindowCoefficients();
                    
            }
        }

        /// <summary>
        /// Averaging factor for FFT spectrum smoothing
        /// Valid values: 1, 2, 4, 8, 16
        /// </summary>
        public int FFT_Averaging
        {
            get
            {
                return _FFT_averaging;
            }
            set
            {
                _FFT_averaging = value;
                OnPropertyChanged(nameof(FFT_Averaging));
            }
        }

        /// <summary>
        /// Request removal of this measurement (for template-based UI)
        /// </summary>
        public void RequestRemove()
        {
            if (RemoveRequested != null)
                RemoveRequested(this, EventArgs.Empty);
        }

        /// <summary>
        /// Show the FFT spectrum window for this measurement (FFT measurements only)
        /// </summary>
        public void FFT_ShowSpectrumWindow()
        {
            if (_fftWindow == null || !_fftWindow.IsVisible)
            {
                _fftWindow = new View.UserForms.FFT();
                _fftWindow.DataContext = this; // Set the Measurement as DataContext

                _fftWindow.Title = "FFT Spectrum - " + _channelSettings.Label;
                _fftWindow.Closed += (s, e) => 
                {
                    _fftWindow = null;
                    _spectrumSignal = null; // Clear cached signal when window closes
                    _lastSpectrumLength = -1;
                };

                _spectrumFrequencies = new double[FFT_Size * FFT_Interpolation / 2];
                _spectrumMagnitudes = new double[FFT_Size * FFT_Interpolation / 2];
                _fftWindow.Show();

                // Initialize the spectrum plot with constant elements (PlotManager pattern)
                FFT_InitializeSpectrumPlot();
            }
            else
            {
                _fftWindow.Activate();
            }

            // Update the spectrum plot with current data
            FFT_UpdateSpectrumPlot();
        }

        /// <summary>
        /// Initialize the spectrum plot with constant elements (called once when window opens)
        /// Following PlotManager pattern for efficiency
        /// </summary>
        private void FFT_InitializeSpectrumPlot()
        {
            if (_fftWindow == null) return;

            // Clear the plot completely for fresh start
            _fftWindow.WpfPlotFFT.Plot.Clear();
                
            // Set constant elements that never change
            _fftWindow.WpfPlotFFT.Plot.Axes.Bottom.Label.Text = "Frequency (Hz)";
            _fftWindow.WpfPlotFFT.Plot.Axes.Left.Label.Text = "Magnitude (dB)";
            _fftWindow.WpfPlotFFT.Plot.Title("FFT Spectrum - " + _channelSettings.Label + " (Fs = " + _dataStream.SampleRate.ToString("F1") + " Hz)");

            // Initialize X-axis to show frequency range from 0 to Nyquist frequency (SampleRate/2)
            double nyquistFrequency = _dataStream.SampleRate / 2.0;
            _fftWindow.WpfPlotFFT.Plot.Axes.SetLimitsX(0, nyquistFrequency);
            _fftWindow.WpfPlotFFT.Plot.Axes.SetLimitsY(-60, 100);

            // Reset signal tracking
            _spectrumSignal = null;
            _lastSpectrumLength = -1;
        }

        /// <summary>
        /// Update the spectrum plot with current data (efficient updates following PlotManager pattern)
        /// Only recreates signal when data length changes (FFT size or interpolation change)
        /// </summary>
        private void FFT_UpdateSpectrumPlot()
        {

            int currentSpectrumLength = 0;
            if (_spectrumFrequencies != null) 
                currentSpectrumLength = _spectrumFrequencies.Length;
                
            // Check if we need to rebuild the signal (data length changed)
            bool needsRebuild = false;
            if (_spectrumSignal == null) 
                needsRebuild = true;
            if (_lastSpectrumLength != currentSpectrumLength) 
                needsRebuild = true;
                
            if (needsRebuild)
            {                    
                // Remove old signal if it exists
                if (_spectrumSignal != null)
                    _fftWindow.WpfPlotFFT.Plot.Remove(_spectrumSignal);

                // Create new signal with current data
                ScottPlot.Plottables.SignalXY newSignal = _fftWindow.WpfPlotFFT.Plot.Add.SignalXY(_spectrumFrequencies, _spectrumMagnitudes);
                _spectrumSignal = newSignal;


                // Apply constant signal properties (set once)
                ScottPlot.Color scColor = new ScottPlot.Color(_channelSettings.Color.R, _channelSettings.Color.G, _channelSettings.Color.B);
                _spectrumSignal.Color = scColor;
                _spectrumSignal.LineWidth = 2.0f;
                _spectrumSignal.LineStyle.AntiAlias = true;
                _spectrumSignal.MarkerShape = ScottPlot.MarkerShape.None;
                    
                _lastSpectrumLength = currentSpectrumLength;
            }
      
            // Refresh the plot to show updated data
            _fftWindow.WpfPlotFFT.Refresh();
        }

        /// <summary>
        /// Update measurement - called by SystemManager
        /// </summary>
        public void UpdateMeasurement()
        {
            if (_disposed) return;

            // For FFT measurements, copy exactly the amount of data needed based on FFT size
            // For other measurements, use 5000 samples as requested (hardcoded for now)
            int samplesToCopy;
            if (_measurementType == MeasurementType.FFT) 
                samplesToCopy = _FFT_Size; 
            else 
                samplesToCopy = 5000; // Use hardcoded 5000 samples for non-FFT measurements
                
            //// Ensure we don't exceed buffer capacity
            //if (samplesToCopy > _dataBuffer.Length) 
            //    samplesToCopy = _dataBuffer.Length;
                
            int samplesCopied = _dataStream.CopyLatestTo(_channelIndex, _dataBuffer, samplesToCopy);
            
            // Skip calculation if no data was copied
            if (samplesCopied <= 0)
                return;

            ReadOnlySpan<double> validData = _dataBuffer.AsSpan(0, samplesCopied);
                    
            if (_measurementType == MeasurementType.FFT)
            {
                double newResult = FFT_Update(validData);
                Result = newResult;
                        
                // Update FFT plot if window is open
                if (_fftWindow != null && _fftWindow.IsVisible)
                {
                    if (Application.Current != null)
                    {
                        if (Application.Current.Dispatcher != null)
                            Application.Current.Dispatcher.BeginInvoke((Action)FFT_UpdateSpectrumPlot);
                    }
                }
            }
            else
            {
                // Calculate using the assigned function pointer for non-FFT measurements
                double newResult = _calculationFunction(validData);
                Result = newResult;
            }
        }

        #region Private Methods - Function Pointer Pattern

        /// <summary>
        /// Gets the appropriate calculation function based on measurement type (like function pointer in C)
        /// </summary>
        private static CalculationFunction GetCalculationFunction(MeasurementType type)
        {
            CalculationFunction func;
            switch (type)
            {
                case MeasurementType.Minimum:
                    func = CalculateMinimum;
                    break;
                case MeasurementType.Maximum:
                    func = CalculateMaximum;
                    break;
                case MeasurementType.Mean:
                    func = CalculateMean;
                    break;
                case MeasurementType.Rms:
                    func = CalculateRms;
                    break;
                case MeasurementType.StandardDeviation:
                    func = CalculateStandardDeviation;
                    break;
                case MeasurementType.Variance:
                    func = CalculateVariance;
                    break;
                case MeasurementType.PeakToPeak:
                    func = CalculatePeakToPeak;
                    break;
                default:
                    func = CalculateRms;
                    break;
            }

            return func;
        }

        // Individual calculation functions (like function pointers in C)
        private static double CalculateMinimum(ReadOnlySpan<double> data)
        {
            double min = double.MaxValue;
            foreach (double value in data)
            {
                if (double.IsNaN(value) || double.IsInfinity(value)) 
                    continue;
                if (value < min) 
                    min = value;
            }
            if (min == double.MaxValue) 
                return 0.0; 
            else return min;
        }

        private static double CalculateMaximum(ReadOnlySpan<double> data)
        {
            double max = double.MinValue;
            foreach (double value in data)
            {
                if (double.IsNaN(value) || double.IsInfinity(value)) 
                    continue;
                if (value > max) 
                    max = value;
            }
            if (max == double.MinValue)
                return 0.0; 
            else return max;
        }

        private static double CalculateMean(ReadOnlySpan<double> data)
        {
            double sum = 0.0;
            int count = 0;
            foreach (double value in data)
            {
                if (double.IsNaN(value) || double.IsInfinity(value)) 
                    continue;

                sum += value;
                count++;
            }
            if (count > 0) 
                return sum / count; 
            else 
                return 0.0;
        }

        private static double CalculateRms(ReadOnlySpan<double> data)
        {
            double sumOfSquares = 0.0;
            int count = 0;
            foreach (double value in data)
            {
                if (double.IsNaN(value) || double.IsInfinity(value)) 
                    continue;

                sumOfSquares += value * value;
                count++;
            }
            if (count > 0) 
                return Math.Sqrt(sumOfSquares / count); 
            else return 0.0;
        }

        private static double CalculateStandardDeviation(ReadOnlySpan<double> data)
        {
            double mean = CalculateMean(data);
            double sumOfSquaredDifferences = 0.0;
            int count = 0;

            foreach (double value in data)
            {
                if (double.IsNaN(value) || double.IsInfinity(value)) 
                    continue;

                double diff = value - mean;
                sumOfSquaredDifferences += diff * diff;
                count++;
            }
            if (count > 0) 
                return Math.Sqrt(sumOfSquaredDifferences / count);
            else 
                return 0.0;
        }

        private static double CalculateVariance(ReadOnlySpan<double> data)
        {
            double mean = CalculateMean(data);
            double sumOfSquaredDifferences = 0.0;
            int count = 0;
            foreach (double value in data)
            {
                if (double.IsNaN(value) || double.IsInfinity(value)) 
                    continue;

                double diff = value - mean;
                sumOfSquaredDifferences += diff * diff;
                count++;
            }
            if (count > 0) 
                return sumOfSquaredDifferences / count; 
            else 
                return 0.0;
        }

        private static double CalculatePeakToPeak(ReadOnlySpan<double> data)
        {
            double min = double.MaxValue;
            double max = double.MinValue;
            foreach (double value in data)
            {
                if (double.IsNaN(value) || double.IsInfinity(value)) 
                    continue;

                if (value < min) min = value;
                if (value > max) max = value;
            }
            if (min == double.MaxValue || max == double.MinValue) 
                return 0.0; 
            else 
                return max - min;
        }

        /// <summary>
        /// FFT calculation function (matching traditional C-style function pointer pattern)
        /// Note: This is an instance method that gets bound to the specific Measurement instance
        /// </summary>
        private double CalculateFFT(ReadOnlySpan<double> data)
        {
            return FFT_Update(data);
        }

        private double FFT_Update(ReadOnlySpan<double> data)
        {
            int effectiveFftSize = _FFT_Size * _FFT_interpolation;

            Aelian.FFT.SignalData fftData = Aelian.FFT.SignalData.CreateFromRealSize(effectiveFftSize);
            System.Span<double> realPart = fftData.AsReal();

            int copyLength = Math.Min(data.Length, _FFT_Size);

            FFT_ApplyWindowFunction(data, realPart, copyLength);

            // Zero-pad the rest for interpolation
            for (int i = copyLength; i < effectiveFftSize; i++)
            {
                realPart[i] = 0.0;
            }

            Aelian.FFT.FastFourierTransform.RealFFT(realPart, true);

            // Calculate magnitude spectrum and find peak frequency

            System.Span<System.Numerics.Complex> complexData = fftData.AsComplex();
            double maxMagnitude = 0.0;
            int peakBin = 0;

            // Prepare arrays for spectrum data (only positive frequencies)
            int spectrumLength = effectiveFftSize / 2;

            bool needSpectrum = false;
            if (_fftWindow != null && _fftWindow.IsVisible)
                    needSpectrum = true;

            // Get actual sample rate from the data stream
            double sampleRate = _dataStream.SampleRate;
            double frequencyResolution = sampleRate / effectiveFftSize;

            // Calculate proper scaling factors for magnitude correction
            double fftScaling = 2.0 / _FFT_Size; // Factor of 2 for single-sided spectrum, divide by N for FFT scaling
            
            // Calculate window-specific amplitude correction factor
            double windowAmplitudeCorrection = GetWindowAmplitudeCorrection(_FFT_windowFunction);

            // Calculate magnitudes and optionally build spectrum arrays
            for (int i = 0; i < spectrumLength; i++)
            {
                double magnitude = Math.Sqrt(complexData[i].Real * complexData[i].Real + 
                                            complexData[i].Imaginary * complexData[i].Imaginary);

                // Apply proper scaling: FFT scaling and window amplitude correction
                magnitude = magnitude * fftScaling * windowAmplitudeCorrection;
                
                // Special case for DC component (i=0) - no factor of 2 needed for single-sided spectrum
                if (i == 0)
                {
                    magnitude = magnitude / 2.0;
                }

                if (magnitude > maxMagnitude)
                {
                    maxMagnitude = magnitude;
                    peakBin = i;
                }

                if (needSpectrum)
                {
                    _spectrumFrequencies[i] = i * frequencyResolution;

                    // Apply averaging smoothing (exponential moving average simulation)
                    if (_FFT_averaging > 1 && i < _spectrumMagnitudes.Length)
                    {
                        double alpha = 2.0 / (_FFT_averaging + 1.0); // EMA smoothing factor
                        double currentMagdB = 20.0 * Math.Log10(Math.Max(magnitude, 1e-10));
                        _spectrumMagnitudes[i] = alpha * currentMagdB + (1.0 - alpha) * _spectrumMagnitudes[i];
                    }
                    else
                    {
                        // Convert to dB (with small offset to avoid log(0))
                        _spectrumMagnitudes[i] = 20.0 * Math.Log10(Math.Max(magnitude, 1e-10));
                    }
                }
            }
                
            double peakFrequency = peakBin * frequencyResolution;
            return peakFrequency;
        }

        /// <summary>
        /// Get the amplitude correction factor for the specified window function
        /// These factors compensate for the amplitude loss caused by windowing
        /// </summary>
        /// <param name="windowFunction">Name of the window function</param>
        /// <returns>Amplitude correction factor</returns>
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
        /// Apply the selected window function to the input data using pre-calculated coefficients
        /// </summary>
        private void FFT_ApplyWindowFunction(ReadOnlySpan<double> input, Span<double> output, int length)
        {
            // Ensure window coefficients are valid
            if (!_FFT_windowCoefficientsValid || _FFT_windowCoefficients == null || _FFT_windowCoefficients.Length != _FFT_Size)
            {
                FFT_PreCalculateWindowCoefficients();
            }
            
            // Apply pre-calculated window coefficients (element-wise multiplication)
            int applyLength = Math.Min(length, _FFT_windowCoefficients.Length);
            for (int i = 0; i < applyLength; i++)
            {
                output[i] = input[i] * _FFT_windowCoefficients[i];
            }
            
        }

        /// <summary>
        /// Pre-calculate window function coefficients for optimal performance
        /// Called when FFT size or window function changes
        /// </summary>
        private void FFT_PreCalculateWindowCoefficients()
        {            
            // Allocate or reallocate window coefficients array
            if (_FFT_windowCoefficients == null || _FFT_windowCoefficients.Length != _FFT_Size)
            {
                _FFT_windowCoefficients = new double[_FFT_Size];
            }
            
            // Calculate coefficients based on window function type
            switch (_FFT_windowFunction)
            {
                case "Hann":
                    FFT_PreCalculateHannWindow();
                    break;
                case "Flat Top":
                    FFT_PreCalculateFlatTopWindow();
                    break;
                case "None":
                    FFT_PreCalculateRectangularWindow();
                    break;
                case "Blackman-Harris":
                default:
                    FFT_PreCalculateBlackmanHarrisWindow();
                    break;
            }
            
            _FFT_windowCoefficientsValid = true;
        }

        /// <summary>
        /// Pre-calculate Blackman-Harris window coefficients
        /// </summary>
        private void FFT_PreCalculateBlackmanHarrisWindow()
        {
            const double a0 = 0.35875;
            const double a1 = 0.48829;
            const double a2 = 0.14128;
            const double a3 = 0.01168;
            
            for (int i = 0; i < _FFT_Size; i++)
            {
                double n = i / (double)(_FFT_Size - 1);
                _FFT_windowCoefficients[i] = a0 - a1 * Math.Cos(2.0 * Math.PI * n) + 
                                        a2 * Math.Cos(4.0 * Math.PI * n) - 
                                        a3 * Math.Cos(6.0 * Math.PI * n);
            }
        }

        /// <summary>
        /// Pre-calculate Hann window coefficients
        /// </summary>
        private void FFT_PreCalculateHannWindow()
        {
            for (int i = 0; i < _FFT_Size; i++)
            {
                _FFT_windowCoefficients[i] = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (_FFT_Size - 1)));
            }
        }

        /// <summary>
        /// Pre-calculate Flat Top window coefficients
        /// </summary>
        private void FFT_PreCalculateFlatTopWindow()
        {
            const double a0 = 0.21557895;
            const double a1 = 0.41663158;
            const double a2 = 0.277263158;
            const double a3 = 0.083578947;
            const double a4 = 0.006947368;
            
            for (int i = 0; i < _FFT_Size; i++)
            {
                double n = i / (double)(_FFT_Size - 1);
                _FFT_windowCoefficients[i] = a0 - a1 * Math.Cos(2.0 * Math.PI * n) + 
                                        a2 * Math.Cos(4.0 * Math.PI * n) - 
                                        a3 * Math.Cos(6.0 * Math.PI * n) + 
                                        a4 * Math.Cos(8.0 * Math.PI * n);
            }
        }

        /// <summary>
        /// Pre-calculate rectangular window coefficients (all ones - no windowing)
        /// </summary>
        private void FFT_PreCalculateRectangularWindow()
        {
            for (int i = 0; i < _FFT_Size; i++)
            {
                _FFT_windowCoefficients[i] = 1.0;
            }
        }

        /// <summary>
        /// Raises the PropertyChanged event
        /// </summary>
        protected virtual void OnPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Disposes of the measurement (stopping any active processing)
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                
                // Clean up cached spectrum signal
                _spectrumSignal = null;
                _lastSpectrumLength = -1;
                
                // Dispose of FFT window if it exists
                if (_fftWindow != null)
                {
                    _fftWindow.Close();
                    _fftWindow = null;
                }
            }
        }

        #endregion
    }
}
