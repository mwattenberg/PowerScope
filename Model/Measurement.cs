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
        private int _fftSize = 1024;
        private int _interpolation = 1;
        private string _windowFunction = "Blackman-Harris";
        private int _averaging = 1;

        // Pre-calculated window function coefficients for performance optimization
        private double[] _windowCoefficients;
        private bool _windowCoefficientsValid = false;

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
            if (dataStream == null) throw new ArgumentNullException(nameof(dataStream));
            _dataStream = dataStream;
            _channelIndex = channelIndex;
            if (channelSettings == null) throw new ArgumentNullException(nameof(channelSettings));
            _channelSettings = channelSettings;
            
            // For FFT measurements, ensure buffer is large enough for maximum FFT size
            // For other measurements, use a reasonable default
            int bufferSize;
            if (measurementType == MeasurementType.FFT)
                bufferSize = 8192;
            else
                bufferSize = 5000;

            _dataBuffer = new double[bufferSize];
            
            _calculationFunction = GetCalculationFunction(measurementType);
            
            // Pre-calculate window coefficients for FFT measurements
            if (measurementType == MeasurementType.FFT)
            {
                // For FFT measurements we need an instance-bound calculation function
                _calculationFunction = data => FFT_Update(data);
                FFT_PreCalculateWindowCoefficients();
            }
        }

        /// <summary>
        /// Legacy constructor for backward compatibility
        /// </summary>
        /// <param name="measurementType">Type of measurement to perform</param>
        /// <param name="dataStream">Data stream to read from</param>
        /// <param name="channelIndex">Zero-based channel index</param>
        public Measurement(MeasurementType measurementType, IDataStream dataStream, int channelIndex)
            : this(measurementType, dataStream, channelIndex, new ChannelSettings { Label = $"CH{channelIndex + 1}" })
        {
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
                if (!EqualityComparer<double>.Default.Equals(_result, value))
                {
                    _result = value;
                    OnPropertyChanged(nameof(Result));
                }
            }
        }

        /// <summary>
        /// Spectrum frequencies for FFT measurements (Hz)
        /// </summary>
        public double[] SpectrumFrequencies
        {
            get
            {
                return _spectrumFrequencies;
            }
        }

        /// <summary>
        /// Spectrum magnitudes for FFT measurements (dB)
        /// </summary>
        public double[] SpectrumMagnitudes
        {
            get
            {
                return _spectrumMagnitudes;
            }
        }

        /// <summary>
        /// Whether this measurement has spectrum data available
        /// </summary>
        public bool HasSpectrumData
        {
            get
            {
                if (_measurementType != MeasurementType.FFT) return false;
                if (_spectrumFrequencies == null) return false;
                if (_spectrumMagnitudes == null) return false;
                return true;
            }
        }

        /// <summary>
        /// FFT size (number of samples for FFT calculation)
        /// Must be a power of 2, range 128-8192
        /// </summary>
        public int FFT_Size
        {
            get
            {
                return _fftSize;
            }
            set
            {
                int clampedValue = Math.Max(128, Math.Min(8192, value));
                int powerOfTwo = NextPowerOfTwo(clampedValue);
                
                if (_fftSize != powerOfTwo)
                {
                    _fftSize = powerOfTwo;
                    OnPropertyChanged(nameof(FFT_Size));
                    
                    // Invalidate window coefficients since FFT size changed
                    _windowCoefficientsValid = false;
                    FFT_PreCalculateWindowCoefficients();
                    
                    // Invalidate cached spectrum signal since data length will change
                    _lastSpectrumLength = -1;
                    
                    // Recalculate spectrum if FFT window is open
                    if (_fftWindow != null && _fftWindow.IsVisible)
                    {
                        if (Application.Current != null)
                        {
                            if (Application.Current.Dispatcher != null)
                                Application.Current.Dispatcher.BeginInvoke((Action)FFT_UpdateSpectrumPlot);
                        }
                    }
                }
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
                return _interpolation;
            }
            set
            {
                int clampedValue;
                if (value <= 1) clampedValue = 1;
                else if (value <= 2) clampedValue = 2;
                else if (value <= 4) clampedValue = 4;
                else clampedValue = 8;
                
                if (_interpolation != clampedValue)
                {
                    _interpolation = clampedValue;
                    OnPropertyChanged(nameof(FFT_Interpolation));
                    
                    // Invalidate cached spectrum signal since data length will change
                    _lastSpectrumLength = -1;
                    
                    // Recalculate spectrum if FFT window is open
                    if (_fftWindow != null && _fftWindow.IsVisible)
                    {
                        if (Application.Current != null)
                        {
                            if (Application.Current.Dispatcher != null)
                                Application.Current.Dispatcher.BeginInvoke((Action)FFT_UpdateSpectrumPlot);
                        }
                    }
                }
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
                return _windowFunction;
            }
            set
            {
                string validatedValue;
                if (value == "Hann") validatedValue = "Hann";
                else if (value == "Flat Top") validatedValue = "Flat Top";
                else if (value == "None") validatedValue = "None";
                else validatedValue = "Blackman-Harris";
                
                if (_windowFunction != validatedValue)
                {
                    _windowFunction = validatedValue;
                    OnPropertyChanged(nameof(FFT_WindowFunction));
                    
                    // Invalidate window coefficients since window function changed
                    _windowCoefficientsValid = false;
                    FFT_PreCalculateWindowCoefficients();
                    
                    // Recalculate spectrum if FFT window is open
                    if (_fftWindow != null && _fftWindow.IsVisible)
                    {
                        if (Application.Current != null)
                        {
                            if (Application.Current.Dispatcher != null)
                                Application.Current.Dispatcher.BeginInvoke((Action)FFT_UpdateSpectrumPlot);
                        }
                    }
                }
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
                return _averaging;
            }
            set
            {
                int clampedValue;
                if (value <= 1) clampedValue = 1;
                else if (value <= 2) clampedValue = 2;
                else if (value <= 4) clampedValue = 4;
                else if (value <= 8) clampedValue = 8;
                else clampedValue = 16;
                
                if (_averaging != clampedValue)
                {
                    _averaging = clampedValue;
                    OnPropertyChanged(nameof(FFT_Averaging));
                    
                    // Recalculate spectrum if FFT window is open
                    if (_fftWindow != null && _fftWindow.IsVisible)
                    {
                        if (Application.Current != null)
                        {
                            if (Application.Current.Dispatcher != null)
                                Application.Current.Dispatcher.BeginInvoke((Action)FFT_UpdateSpectrumPlot);
                        }
                    }
                }
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
            if (_measurementType != MeasurementType.FFT) return;

            if (_fftWindow == null || !_fftWindow.IsVisible)
            {
                _fftWindow = new View.UserForms.FFT();
                _fftWindow.DataContext = this; // Set the Measurement as DataContext
                _fftWindow.Title = "FFT Spectrum - " + _channelSettings.Label;
                _fftWindow.Closed += (s, e) => {
                    _fftWindow = null;
                    _spectrumSignal = null; // Clear cached signal when window closes
                    _lastSpectrumLength = -1;
                };
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

            try
            {
                // Clear the plot completely for fresh start
                _fftWindow.WpfPlotFFT.Plot.Clear();
                
                // Set constant elements that never change
                _fftWindow.WpfPlotFFT.Plot.Axes.Bottom.Label.Text = "Frequency (Hz)";
                _fftWindow.WpfPlotFFT.Plot.Axes.Left.Label.Text = "Magnitude (dB)";
                _fftWindow.WpfPlotFFT.Plot.Title("FFT Spectrum - " + _channelSettings.Label + " (Fs = " + _dataStream.SampleRate.ToString("F1") + " Hz)");
                
                // Reset signal tracking
                _spectrumSignal = null;
                _lastSpectrumLength = -1;
                
                Debug.WriteLine("FFT: Spectrum plot initialized with constant elements");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("FFT: Error initializing spectrum plot: " + ex.Message);
            }
        }

        /// <summary>
        /// Update the spectrum plot with current data (efficient updates following PlotManager pattern)
        /// Only recreates signal when data length changes (FFT size or interpolation change)
        /// </summary>
        private void FFT_UpdateSpectrumPlot()
        {
            if (_fftWindow == null) return;
            if (!HasSpectrumData) return;

            try
            {
                Stopwatch stopwatch = Stopwatch.StartNew();
                
                int currentSpectrumLength = 0;
                if (_spectrumFrequencies != null) currentSpectrumLength = _spectrumFrequencies.Length;
                
                // Check if we need to rebuild the signal (data length changed)
                bool needsRebuild = false;
                if (_spectrumSignal == null) needsRebuild = true;
                if (_lastSpectrumLength != currentSpectrumLength) needsRebuild = true;
                
                if (needsRebuild)
                {
                    Debug.WriteLine("FFT: Rebuilding spectrum signal - Length changed from " + _lastSpectrumLength + " to " + currentSpectrumLength);
                    
                    // Remove old signal if it exists
                    if (_spectrumSignal != null)
                        _fftWindow.WpfPlotFFT.Plot.Remove(_spectrumSignal);

                    // Create new signal with current data
                    ScottPlot.Plottables.SignalXY newSignal = _fftWindow.WpfPlotFFT.Plot.Add.SignalXY(_spectrumFrequencies, _spectrumMagnitudes);
                    _spectrumSignal = newSignal;

                    //_spectrumSignal = _fftWindow.WpfPlotFFT.Plot.Add.Signal(_spectrumMagnitudes);

                    // Apply constant signal properties (set once)
                    ScottPlot.Color scColor = new ScottPlot.Color(_channelSettings.Color.R, _channelSettings.Color.G, _channelSettings.Color.B);
                    _spectrumSignal.Color = scColor;
                    _spectrumSignal.LineWidth = 2.0f;
                    _spectrumSignal.LineStyle.AntiAlias = true;
                    _spectrumSignal.MarkerShape = ScottPlot.MarkerShape.None;
                    
                    _lastSpectrumLength = currentSpectrumLength;
                }
                else
                {
                    // Efficient update: just update the data arrays in the existing signal
                    // The SignalXY plot will automatically use the updated array data
                    // No need to recreate the signal or set properties again
                }
                
                stopwatch.Stop();
                Debug.WriteLine("FFT: Spectrum plot updated - " + stopwatch.Elapsed.TotalMicroseconds.ToString("F1") + " us (Rebuild: " + needsRebuild.ToString() + ")");
                
                // Refresh the plot to show updated data
                _fftWindow.WpfPlotFFT.Refresh();
                
            }
            catch (Exception ex)
            {
                Debug.WriteLine("FFT: Error updating spectrum plot: " + ex.Message);
            }
        }

        /// <summary>
        /// Update measurement - called by SystemManager
        /// </summary>
        public void UpdateMeasurement()
        {
            if (_disposed) return;

            try
            {
                // For FFT measurements, copy exactly the amount of data needed based on FFT size
                // For other measurements, use the full buffer for maximum accuracy
                int samplesToCopy;
                if (_measurementType == MeasurementType.FFT) samplesToCopy = _fftSize; else samplesToCopy = _dataBuffer.Length;
                
                // Ensure we don't exceed buffer capacity
                if (samplesToCopy > _dataBuffer.Length) samplesToCopy = _dataBuffer.Length;
                
                // Copy fresh data from the data stream - respecting FFT size for FFT measurements
                int samplesCopied = _dataStream.CopyLatestTo(_channelIndex, _dataBuffer, samplesToCopy);
                
                if (samplesCopied > 0)
                {
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
                        double newResult = _calculationFunction(validData);
                        Result = newResult;
                    }
                }
            }
            catch
            {
                // Ignore errors - measurement will just use stale data
                // This can happen if the data stream is being disposed or has errors
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
                if (double.IsNaN(value) || double.IsInfinity(value)) continue;
                if (value < min) min = value;
            }
            if (min == double.MaxValue) return 0.0; else return min;
        }

        private static double CalculateMaximum(ReadOnlySpan<double> data)
        {
            double max = double.MinValue;
            foreach (double value in data)
            {
                if (double.IsNaN(value) || double.IsInfinity(value)) continue;
                if (value > max) max = value;
            }
            if (max == double.MinValue) return 0.0; else return max;
        }

        private static double CalculateMean(ReadOnlySpan<double> data)
        {
            double sum = 0.0;
            int count = 0;
            foreach (double value in data)
            {
                if (double.IsNaN(value) || double.IsInfinity(value)) continue;
                sum += value;
                count++;
            }
            if (count > 0) return sum / count; else return 0.0;
        }

        private static double CalculateRms(ReadOnlySpan<double> data)
        {
            double sumOfSquares = 0.0;
            int count = 0;
            foreach (double value in data)
            {
                if (double.IsNaN(value) || double.IsInfinity(value)) continue;
                sumOfSquares += value * value;
                count++;
            }
            if (count > 0) return Math.Sqrt(sumOfSquares / count); else return 0.0;
        }

        private static double CalculateStandardDeviation(ReadOnlySpan<double> data)
        {
            double mean = CalculateMean(data);
            double sumOfSquaredDifferences = 0.0;
            int count = 0;
            foreach (double value in data)
            {
                if (double.IsNaN(value) || double.IsInfinity(value)) continue;
                double diff = value - mean;
                sumOfSquaredDifferences += diff * diff;
                count++;
            }
            if (count > 0) return Math.Sqrt(sumOfSquaredDifferences / count); else return 0.0;
        }

        private static double CalculateVariance(ReadOnlySpan<double> data)
        {
            double mean = CalculateMean(data);
            double sumOfSquaredDifferences = 0.0;
            int count = 0;
            foreach (double value in data)
            {
                if (double.IsNaN(value) || double.IsInfinity(value)) continue;
                double diff = value - mean;
                sumOfSquaredDifferences += diff * diff;
                count++;
            }
            if (count > 0) return sumOfSquaredDifferences / count; else return 0.0;
        }

        private static double CalculatePeakToPeak(ReadOnlySpan<double> data)
        {
            double min = double.MaxValue;
            double max = double.MinValue;
            foreach (double value in data)
            {
                if (double.IsNaN(value) || double.IsInfinity(value)) continue;
                if (value < min) min = value;
                if (value > max) max = value;
            }
            if (min == double.MaxValue || max == double.MinValue) return 0.0; else return max - min;
        }

        private double FFT_Update(ReadOnlySpan<double> data)
        {
            Stopwatch totalStopwatch = Stopwatch.StartNew();

            try
            {
                if (data.Length < 4)
                {
                    totalStopwatch.Stop();
                    Debug.WriteLine("FFT: Insufficient data (" + data.Length + " samples) - " + totalStopwatch.Elapsed.TotalMicroseconds.ToString("F1") + " us");
                    return 0.0;
                }

                // Use the configured FFT size with zero-padding interpolation
                int effectiveFftSize = _fftSize * _interpolation;

                Debug.WriteLine("FFT: Starting calculation - Size: " + _fftSize + ", Interpolation: " + _interpolation + "x, Effective: " + effectiveFftSize + ", Window: " + _windowFunction);

                // Copy data to FFT buffer with windowing and zero-padding
                Stopwatch dataProcessingStopwatch = Stopwatch.StartNew();
                Aelian.FFT.SignalData fftData = Aelian.FFT.SignalData.CreateFromRealSize(effectiveFftSize);
                System.Span<double> realPart = fftData.AsReal();

                // Copy input data (up to FFT size, not interpolated size)
                int copyLength = Math.Min(data.Length, _fftSize);

                // Apply window function if specified
                FFT_ApplyWindowFunction(data, realPart, copyLength);

                // Zero-pad the rest for interpolation
                for (int i = copyLength; i < effectiveFftSize; i++)
                {
                    realPart[i] = 0.0;
                }

                dataProcessingStopwatch.Stop();
                Debug.WriteLine("FFT: Data preparation completed - " + dataProcessingStopwatch.Elapsed.TotalMicroseconds.ToString("F1") + " us");

                // Perform FFT
                Stopwatch fftStopwatch = Stopwatch.StartNew();
                Aelian.FFT.FastFourierTransform.RealFFT(realPart, true);
                fftStopwatch.Stop();
                Debug.WriteLine("FFT: Core FFT calculation completed - " + fftStopwatch.Elapsed.TotalMicroseconds.ToString("F1") + " us");

                // Calculate magnitude spectrum and find peak frequency
                Stopwatch spectrumStopwatch = Stopwatch.StartNew();
                System.Span<System.Numerics.Complex> complexData = fftData.AsComplex();
                double maxMagnitude = 0.0;
                int peakBin = 0;

                // Prepare arrays for spectrum data (only positive frequencies)
                int spectrumLength = effectiveFftSize / 2;

                bool needSpectrum = false;
                if (_fftWindow != null)
                {
                    if (_fftWindow.IsVisible)
                        needSpectrum = true;
                }

                // If spectrum is needed, ensure arrays exist and are the right size, but DON'T replace them
                if (needSpectrum)
                {
                    if (_spectrumFrequencies == null || _spectrumFrequencies.Length != spectrumLength)
                    {
                        _spectrumFrequencies = new double[spectrumLength];
                        Debug.WriteLine("FFT: Created new frequency array with length " + spectrumLength);
                    }
                    if (_spectrumMagnitudes == null || _spectrumMagnitudes.Length != spectrumLength)
                    {
                        _spectrumMagnitudes = new double[spectrumLength];
                        Debug.WriteLine("FFT: Created new magnitude array with length " + spectrumLength);
                    }
                }

                // Get actual sample rate from the data stream
                double sampleRate = _dataStream.SampleRate;
                if (sampleRate <= 0)
                    sampleRate = 10000.0; // 10 kHz default

                double frequencyResolution = sampleRate / effectiveFftSize;

                // Calculate magnitudes and optionally build spectrum arrays
                for (int i = 0; i < spectrumLength; i++)
                {
                    double magnitude = Math.Sqrt(complexData[i].Real * complexData[i].Real + 
                                               complexData[i].Imaginary * complexData[i].Imaginary);

                    // Track peak for the result value (using linear magnitude for peak detection)
                    if (i > 0)
                    {
                        if (magnitude > maxMagnitude)
                        {
                            maxMagnitude = magnitude;
                            peakBin = i;
                        }
                    }

                    if (needSpectrum)
                    {
                        _spectrumFrequencies[i] = i * frequencyResolution;

                        // Apply averaging smoothing (exponential moving average simulation)
                        if (_averaging > 1 && i < _spectrumMagnitudes.Length)
                        {
                            double alpha = 2.0 / (_averaging + 1.0); // EMA smoothing factor
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

                spectrumStopwatch.Stop();
                Debug.WriteLine("FFT: Spectrum calculation completed - " + spectrumStopwatch.Elapsed.TotalMicroseconds.ToString("F1") + " us");

                totalStopwatch.Stop();
                double peakFrequency = peakBin * frequencyResolution;
                Debug.WriteLine("FFT: Total execution time - " + totalStopwatch.Elapsed.TotalMicroseconds.ToString("F1") + " us, Peak at " + peakFrequency.ToString("F1") + " Hz");

                // Return the dominant frequency in Hz
                return peakFrequency;
            }
            catch (Exception ex)
            {
                totalStopwatch.Stop();
                Debug.WriteLine("FFT: Error during calculation - " + totalStopwatch.Elapsed.TotalMicroseconds.ToString("F1") + " us, Error: " + ex.Message);
                // Return 0 on any calculation errors
                return 0.0;
            }
        }

        /// <summary>
        /// Apply the selected window function to the input data using pre-calculated coefficients
        /// </summary>
        private void FFT_ApplyWindowFunction(ReadOnlySpan<double> input, Span<double> output, int length)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            
            // Ensure window coefficients are valid
            if (!_windowCoefficientsValid || _windowCoefficients == null || _windowCoefficients.Length != _fftSize)
            {
                FFT_PreCalculateWindowCoefficients();
            }
            
            // Apply pre-calculated window coefficients (element-wise multiplication)
            int applyLength = Math.Min(length, _windowCoefficients.Length);
            for (int i = 0; i < applyLength; i++)
            {
                output[i] = input[i] * _windowCoefficients[i];
            }
            
            stopwatch.Stop();
            Debug.WriteLine("FFT: Window function (" + _windowFunction + ") applied to " + length + " samples using pre-calculated coefficients - " + stopwatch.Elapsed.TotalMicroseconds.ToString("F1") + " us");
        }

        /// <summary>
        /// Pre-calculate window function coefficients for optimal performance
        /// Called when FFT size or window function changes
        /// </summary>
        private void FFT_PreCalculateWindowCoefficients()
        {
            if (_measurementType != MeasurementType.FFT) return;

            Stopwatch stopwatch = Stopwatch.StartNew();
            
            // Allocate or reallocate window coefficients array
            if (_windowCoefficients == null || _windowCoefficients.Length != _fftSize)
            {
                _windowCoefficients = new double[_fftSize];
            }
            
            // Calculate coefficients based on window function type
            switch (_windowFunction)
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
            
            _windowCoefficientsValid = true;
            stopwatch.Stop();
            Debug.WriteLine("FFT: Pre-calculated " + _windowFunction + " window coefficients for " + _fftSize + " samples - " + stopwatch.Elapsed.TotalMicroseconds.ToString("F1") + " us");
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
            
            for (int i = 0; i < _fftSize; i++)
            {
                double n = i / (double)(_fftSize - 1);
                _windowCoefficients[i] = a0 - a1 * Math.Cos(2.0 * Math.PI * n) + 
                                        a2 * Math.Cos(4.0 * Math.PI * n) - 
                                        a3 * Math.Cos(6.0 * Math.PI * n);
            }
        }

        /// <summary>
        /// Pre-calculate Hann window coefficients
        /// </summary>
        private void FFT_PreCalculateHannWindow()
        {
            for (int i = 0; i < _fftSize; i++)
            {
                _windowCoefficients[i] = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (_fftSize - 1)));
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
            
            for (int i = 0; i < _fftSize; i++)
            {
                double n = i / (double)(_fftSize - 1);
                _windowCoefficients[i] = a0 - a1 * Math.Cos(2.0 * Math.PI * n) + 
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
            for (int i = 0; i < _fftSize; i++)
            {
                _windowCoefficients[i] = 1.0;
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

        /// <summary>
        /// Computes the next highest power of two for a given integer n
        /// </summary>
        private static int NextPowerOfTwo(int value)
        {
            if (value <= 0) return 1;
            
            // Find the next power of 2
            int power = 1;
            while (power < value)
            {
                power <<= 1;
            }
            return power;
        }
        #endregion
    }
}
