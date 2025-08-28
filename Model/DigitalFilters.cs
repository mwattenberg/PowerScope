using System;
using System.Collections.Generic;
using System.Linq;

namespace SerialPlotDN_WPF.Model
{
    public interface IDigitalFilter
    {
        double Filter(double input);
        void Reset();
    }

    /// <summary>
    /// Exponential Low Pass Filter implementation
    /// </summary>
    public class ExponentialLowPassFilter : IDigitalFilter
    {
        public double Alpha { get; set; }
        private double _lastOutput;
        private bool _initialized;

        /// <summary>
        /// Create a new exponential low pass filter
        /// </summary>
        /// <param name="alpha">Smoothing factor (0 < alpha <= 1). Lower alpha = more smoothing.</param>
        public ExponentialLowPassFilter(double alpha)
        {
            if (alpha <= 0 || alpha > 1)
                throw new ArgumentOutOfRangeException(nameof(alpha), "Alpha must be in (0, 1].");
            Alpha = alpha;
            _initialized = false;
        }

        /// <summary>
        /// Filter a new input value
        /// </summary>
        /// <param name="input">Input value</param>
        /// <returns>Filtered output</returns>
        public double Filter(double input)
        {
            if (!_initialized)
            {
                _lastOutput = input;
                _initialized = true;
            }
            else
            {
                _lastOutput = Alpha * input + (1 - Alpha) * _lastOutput;
            }
            return _lastOutput;
        }

        /// <summary>
        /// Reset the filter to its initial state
        /// </summary>
        public void Reset()
        {
            _initialized = false;
            _lastOutput = 0.0;
        }
    }

    /// <summary>
    /// Exponential High Pass Filter implementation
    /// </summary>
    public class ExponentialHighPassFilter : IDigitalFilter
    {
        private readonly ExponentialLowPassFilter _lowPass;
        private bool _initialized;
        private double _lastInput;
        private double _lastOutput;

        /// <summary>
        /// Exposes the alpha value of the internal low pass filter
        /// </summary>
        public double Alpha
        {
            get => _lowPass.Alpha;
            set => _lowPass.Alpha = value;
        }

        /// <summary>
        /// Create a new exponential high pass filter
        /// </summary>
        /// <param name="alpha">Smoothing factor (0 < alpha <= 1). Lower alpha = more smoothing.</param>
        public ExponentialHighPassFilter(double alpha)
        {
            _lowPass = new ExponentialLowPassFilter(alpha);
            _initialized = false;
        }

        /// <summary>
        /// Filter a new input value
        /// </summary>
        /// <param name="input">Input value</param>
        /// <returns>Filtered output</returns>
        public double Filter(double input)
        {
            double lowPassValue = _lowPass.Filter(input);
            double highPassValue = input - lowPassValue;
            _lastOutput = highPassValue;
            _lastInput = input;
            _initialized = true;
            return highPassValue;
        }

        /// <summary>
        /// Reset the filter to its initial state
        /// </summary>
        public void Reset()
        {
            _lowPass.Reset();
            _initialized = false;
            _lastInput = 0.0;
            _lastOutput = 0.0;
        }
    }

    public class MovingAverageFilter : IDigitalFilter
    {
        private readonly Queue<double> _window;
        private readonly int _windowSize;
        private double _sum;

        public MovingAverageFilter(int windowSize)
        {
            if (windowSize < 1)
                throw new ArgumentOutOfRangeException(nameof(windowSize), "Window size must be >= 1.");
            _windowSize = windowSize;
            _window = new Queue<double>(windowSize);
            _sum = 0.0;
        }

        public double Filter(double input)
        {
            _window.Enqueue(input);
            _sum += input;
            if (_window.Count > _windowSize)
                _sum -= _window.Dequeue();
            return _sum / _window.Count;
        }

        public void Reset()
        {
            _window.Clear();
            _sum = 0.0;
        }
    }

    public class MedianFilter : IDigitalFilter
    {
        private readonly Queue<double> _window;
        private readonly int _windowSize;

        public MedianFilter(int windowSize)
        {
            if (windowSize < 1)
                throw new ArgumentOutOfRangeException(nameof(windowSize), "Window size must be >= 1.");
            _windowSize = windowSize;
            _window = new Queue<double>(windowSize);
        }

        public double Filter(double input)
        {
            _window.Enqueue(input);
            if (_window.Count > _windowSize)
                _window.Dequeue();
            var sorted = _window.OrderBy(x => x).ToArray();
            int n = sorted.Length;
            if (n % 2 == 1)
                return sorted[n / 2];
            else
                return (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
        }

        public void Reset()
        {
            _window.Clear();
        }
    }

    public class NotchFilter : IDigitalFilter
    {
        private readonly double _omega;
        private readonly double _r;
        private double _x1, _x2, _y1, _y2;

        /// <summary>
        /// Create a basic recursive notch filter
        /// </summary>
        /// <param name="notchFreq">Notch frequency (Hz)</param>
        /// <param name="sampleRate">Sample rate (Hz)</param>
        /// <param name="bandwidth">Bandwidth (Hz), typical values: 1-5 Hz</param>
        public NotchFilter(double notchFreq, double sampleRate, double bandwidth = 2.0)
        {
            if (notchFreq <= 0 || sampleRate <= 0)
                throw new ArgumentOutOfRangeException("Frequencies must be positive.");
            _omega = 2 * Math.PI * notchFreq / sampleRate;
            _r = 1 - 3 * bandwidth / sampleRate;
            _x1 = _x2 = _y1 = _y2 = 0.0;
        }

        public double Filter(double input)
        {
            // Basic IIR notch filter (biquad)
            // y[n] = x[n] - 2*cos(omega)*x[n-1] + x[n-2] + 2*r*cos(omega)*y[n-1] - r^2*y[n-2]
            double y = input - 2 * Math.Cos(_omega) * _x1 + _x2
                + 2 * _r * Math.Cos(_omega) * _y1 - _r * _r * _y2;
            _x2 = _x1;
            _x1 = input;
            _y2 = _y1;
            _y1 = y;
            return y;
        }

        public void Reset()
        {
            _x1 = _x2 = _y1 = _y2 = 0.0;
        }
    }
}
