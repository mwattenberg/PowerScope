using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Timers;

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
    /// Simplified measurement class that calculates only one specific measurement type
    /// Uses function pointer pattern and automatic timer-based updates every 100ms
    /// </summary>
    public class Measurement : INotifyPropertyChanged, IDisposable
    {
        private readonly MeasurementType _measurementType;
        private readonly CalculationFunction _calculationFunction;
        private readonly double[] _data;
        private readonly System.Timers.Timer _updateTimer;
        
        // Single measurement result
        private double _result = 0.0;

        /// <summary>
        /// Constructor with measurement type and data reference
        /// </summary>
        /// <param name="measurementType">Type of measurement to perform</param>
        /// <param name="data">Reference to data array to operate on</param>
        public Measurement(MeasurementType measurementType, double[] data)
        {
            _measurementType = measurementType;
            _data = data ?? throw new ArgumentNullException(nameof(data));
            _calculationFunction = GetCalculationFunction(measurementType);

            // Setup timer for automatic updates every 100ms
            _updateTimer = new System.Timers.Timer(100) // 100ms interval
            {
                AutoReset = true,
                Enabled = true
            };
            _updateTimer.Elapsed += OnTimerElapsed;
            _updateTimer.Start();
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
        /// Timer elapsed event handler - automatically calculates and updates result
        /// </summary>
        private void OnTimerElapsed(object sender, ElapsedEventArgs e)
        {
            if (_data != null && _data.Length > 0)
            {
                try
                {
                    // Calculate using the function pointer and update result
                    var newResult = _calculationFunction(_data.AsSpan());
                    Result = newResult;
                }
                catch
                {
                    // Ignore calculation errors (e.g., if data is being modified during calculation)
                }
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
            _updateTimer?.Stop();
            _updateTimer?.Dispose();
        }

        #endregion

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
