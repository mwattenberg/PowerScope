using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;

namespace SerialPlotDN_WPF.Model
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
        PeakToPeak
    }

    /// <summary>
    /// Delegate for measurement calculation function
    /// </summary>
    public delegate double CalculationFunction(ReadOnlySpan<double> data);

    /// <summary>
    /// Self-contained measurement class that manages its own data copying and calculations
    /// Takes a DataStream and channel index, handles all data management internally
    /// </summary>
    public class Measurement : INotifyPropertyChanged, IDisposable
    {
        private readonly MeasurementType _measurementType;
        private readonly CalculationFunction _calculationFunction;
        private readonly IDataStream _dataStream;
        private readonly int _channelIndex;
        private readonly double[] _dataBuffer;
        private readonly Timer _updateTimer;
        
        // Single measurement result
        private double _result = 0.0;
        private bool _disposed = false;

        /// <summary>
        /// Constructor with measurement type, data stream, and channel
        /// </summary>
        /// <param name="measurementType">Type of measurement to perform</param>
        /// <param name="dataStream">Data stream to read from</param>
        /// <param name="channelIndex">Zero-based channel index</param>
        public Measurement(MeasurementType measurementType, IDataStream dataStream, int channelIndex)
        {
            _measurementType = measurementType;
            _dataStream = dataStream ?? throw new ArgumentNullException(nameof(dataStream));
            _channelIndex = channelIndex;
            _dataBuffer = new double[5000]; // Fixed buffer size
            _calculationFunction = GetCalculationFunction(measurementType);

            // Setup Threading.Timer for automatic updates every 90ms
            _updateTimer = new Timer(OnTimerCallback, null, TimeSpan.FromMilliseconds(90), TimeSpan.FromMilliseconds(200));
        }

        /// <summary>
        /// Type of measurement being performed (read-only)
        /// </summary>
        public MeasurementType Type => _measurementType;

        /// <summary>
        /// The calculated result value
        /// </summary>
        public double Result
        {
            get { return _result; }
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
        /// Timer callback - copies fresh data and calculates result
        /// </summary>
        private void OnTimerCallback(object state)
        {
            if (_disposed) return;

            try
            {
                // Copy fresh data from the data stream
                int samplesCopied = _dataStream.CopyLatestTo(_channelIndex, _dataBuffer, _dataBuffer.Length);
                
                if (samplesCopied > 0)
                {
                    // Calculate using only the valid data and update result
                    var validData = _dataBuffer.AsSpan(0, samplesCopied);
                    var newResult = _calculationFunction(validData);
                    Result = newResult;
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
            return type switch
            {
                MeasurementType.Minimum => CalculateMinimum,
                MeasurementType.Maximum => CalculateMaximum,
                MeasurementType.Mean => CalculateMean,
                MeasurementType.Rms => CalculateRms,
                MeasurementType.StandardDeviation => CalculateStandardDeviation,
                MeasurementType.Variance => CalculateVariance,
                MeasurementType.PeakToPeak => CalculatePeakToPeak,
                _ => CalculateRms // Default fallback
            };
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
            return min == double.MaxValue ? 0.0 : min;
        }

        private static double CalculateMaximum(ReadOnlySpan<double> data)
        {
            double max = double.MinValue;
            foreach (double value in data)
            {
                if (double.IsNaN(value) || double.IsInfinity(value)) continue;
                if (value > max) max = value;
            }
            return max == double.MinValue ? 0.0 : max;
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
            return count > 0 ? sum / count : 0.0;
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
            return count > 0 ? Math.Sqrt(sumOfSquares / count) : 0.0;
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
            return count > 0 ? Math.Sqrt(sumOfSquaredDifferences / count) : 0.0;
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
            return count > 0 ? sumOfSquaredDifferences / count : 0.0;
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
            return (min == double.MaxValue || max == double.MinValue) ? 0.0 : max - min;
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _updateTimer?.Dispose();
            }
        }

        #endregion

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
