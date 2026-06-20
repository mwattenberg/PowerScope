using System.ComponentModel;

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
    /// Enhanced with detail tracking for expandable UI
    /// </summary>
    public class Measurement : INotifyPropertyChanged, IDisposable
    {
        private readonly MeasurementType _measurementType;
        private readonly CalculationFunction _calculationFunction;
        private readonly IDataStream _dataStream;
        private readonly int _channelIndex;
        private double[] _dataBuffer;
        private readonly ChannelSettings _channelSettings;
        
        // Buffer configuration
        private int _measurementWindowLength;
        
        // Single measurement result
        private double _result = 0.0;
        private bool _disposed = false;

        // Statistics properties
        private double _min = double.MaxValue;
        private double _max = double.MinValue;
        private double _mean = 0.0;
        private long _samplesCount = 0;

        private View.UserForms.FFT _fftWindow;

        /// <summary>
        /// FFT-specific computation and bindable settings. Non-null only when Type == FFT.
        /// </summary>
        public FFTAnalysis FFT { get; }

        // Events
        public event EventHandler RemoveRequested;
        public event PropertyChangedEventHandler PropertyChanged;

        public int MeasurementWindowLength
        {
            get { return _measurementWindowLength; }
            set
            {
                if (_measurementWindowLength != value)
                {
                    _measurementWindowLength = Math.Min(100000, Math.Max(1, value));
                    if (_dataBuffer == null || _measurementWindowLength > _dataBuffer.Length)
                        _dataBuffer = new double[_measurementWindowLength];
                    OnPropertyChanged(nameof(MeasurementWindowLength));
                }
            }
        }

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
            
            // Set default buffer size based on measurement type
            if (measurementType == MeasurementType.FFT)
                _measurementWindowLength = 16384; // Use maximum possible FFT size instead of current _fftSize
            else
                _measurementWindowLength = 5000;

            _dataBuffer = new double[_measurementWindowLength];

            _calculationFunction = GetCalculationFunction(measurementType);

            if (measurementType == MeasurementType.FFT)
                FFT = new FFTAnalysis(dataStream, channelSettings);
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

        #region Statistics

        /// <summary>
        /// Controls whether statistics (Min, Max, Mean, Count) are calculated for each measurement.
        /// </summary>
        public bool CalculateStatistics { get; set; } = false;


        /// <summary>
        /// Updates the running statistics with a new measurement value.
        /// </summary>
        private void UpdateStatistics(double value)
        {
            if (!double.IsNaN(value) && !double.IsInfinity(value))
            {
                // Update Min/Max
                if (value < Min) Min = value;
                if (value > Max) Max = value;

                // Update running mean and count
                SamplesCount++;
                Mean += (value - Mean) / SamplesCount;
            }
        }


        /// <summary>
        /// The minimum value recorded since statistics were last reset.
        /// </summary>
        public double Min
        {
            get => _min;
            private set
            {
                _min = value;
                OnPropertyChanged(nameof(Min));
            }
        }

        /// <summary>
        /// The maximum value recorded since statistics were last reset.
        /// </summary>
        public double Max
        {
            get => _max;
            private set
            {
                _max = value;
                OnPropertyChanged(nameof(Max));
            }
        }

        /// <summary>
        /// The running mean (average) of values since statistics were last reset.
        /// </summary>
        public double Mean
        {
            get => _mean;
            private set
            {
                _mean = value;
                OnPropertyChanged(nameof(Mean));
            }
        }

        /// <summary>
        /// Resets the calculated statistics (Min, Max, Mean, Count) to their initial values.
        /// </summary>
        public void ClearStatistics()
        {
            Min = double.MaxValue;
            Max = double.MinValue;
            Mean = 0.0;
            SamplesCount = 0;
            FFT?.ClearPeaks();
        }


        #endregion

        /// <summary>
        /// The total number of samples included in the statistics calculation.
        /// </summary>
        public long SamplesCount
        {
            get => _samplesCount;
            private set
            {
                _samplesCount = value;
                OnPropertyChanged(nameof(SamplesCount));
            }
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
                _fftWindow.Closed += (s, e) => { _fftWindow = null; };
                _fftWindow.Show();
            }
            else
            {
                _fftWindow.Activate();
                _fftWindow.RefreshSpectrumPlot();
            }
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
                samplesToCopy = FFT.Size;
            else
                samplesToCopy = MeasurementWindowLength;

            int samplesCopied = _dataStream.CopyLatestTo(_channelIndex, _dataBuffer, samplesToCopy);

            // Skip calculation if no data was copied
            if (samplesCopied <= 0)
                return;

            ReadOnlySpan<double> validData = _dataBuffer.AsSpan(0, samplesCopied);

            if (_measurementType == MeasurementType.FFT)
                Result = FFT.Update(validData);
            else
                Result = _calculationFunction(validData);

            // Update measurement history for detail tracking
            if (CalculateStatistics)
            {
                UpdateStatistics(Result);
            }
        }

        #region Clean-up
        #region "Normal" measurements

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
                sumOfSquares += value * value;
                count++;
            }
            if (count > 0) 
                return Math.Sqrt(sumOfSquares / count); 
            else return 0.0;
        }

        // Welford's online algorithm: accumulates mean and variance in one pass with good
        // numerical stability (avoids catastrophic cancellation from (x - mean)^2 on large data).
        private static double WelfordVariance(ReadOnlySpan<double> data)
        {
            int count = 0;
            double mean = 0.0;
            double m2 = 0.0;
            foreach (double value in data)
            {
                count++;
                double delta = value - mean;
                mean += delta / count;
                m2 += delta * (value - mean); // second delta uses updated mean — this is intentional
            }
            return count > 0 ? m2 / count : 0.0;
        }

        private static double CalculateStandardDeviation(ReadOnlySpan<double> data)
        {
            return Math.Sqrt(WelfordVariance(data));
        }

        private static double CalculateVariance(ReadOnlySpan<double> data)
        {
            return WelfordVariance(data);
        }

        private static double CalculatePeakToPeak(ReadOnlySpan<double> data)
        {
            double min = double.MaxValue;
            double max = double.MinValue;
            foreach (double value in data)
            {
                if (value < min) min = value;
                if (value > max) max = value;
            }
            if (min == double.MaxValue || max == double.MinValue) 
                return 0.0; 
            else 
                return max - min;
        }


        /// <summary>
        /// Apply the selected window function to the input data using pre-calculated coefficients
        /// </summary>

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

                // Close the spectrum window if it exists
                if (_fftWindow != null)
                {
                    _fftWindow.Close();
                    _fftWindow = null;
                }
            }
        }

        #endregion

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



        public void RequestRemove()
        {
            if (RemoveRequested != null)
                RemoveRequested(this, EventArgs.Empty);
        }

        #endregion
    }
}
