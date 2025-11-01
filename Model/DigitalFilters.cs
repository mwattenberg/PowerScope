using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerScope.Model
{
    /// <summary>
    /// Lightweight circular buffer optimized for single-threaded digital filter use.
    /// This is separate from RingBuffer<T> because:
    /// - RingBuffer<T> includes thread-safety locking which adds overhead in single-threaded filter operations
    /// - Filters process one sample at a time on a single thread and don't need synchronization
    /// - This implementation is specialized for double values and optimized for the hot path
    /// - Provides only the minimal interface needed by filters (Add, CopyTo, Clear)
    /// </summary>
    internal class CircularBuffer
    {
        private readonly double[] _buffer;
        private readonly int _capacity;
        private int _head;
        private int _count;

        public CircularBuffer(int capacity)
        {
            _capacity = capacity;
            _buffer = new double[capacity];
            _head = 0;
            _count = 0;
        }

        public int Count
        {
            get { return _count; }
        }

        public int Capacity
        {
            get { return _capacity; }
        }

        public void Add(double value)
        {
            _buffer[_head] = value;
            _head = (_head + 1) % _capacity;

            if (_count < _capacity)
                _count++;
        }

        public void CopyTo(double[] destination)
        {
            int startIndex = (_head - _count + _capacity) % _capacity;

            if (startIndex + _count <= _capacity)
            {
                Array.Copy(_buffer, startIndex, destination, 0, _count);
            }
            else
            {
                int firstPart = _capacity - startIndex;
                Array.Copy(_buffer, startIndex, destination, 0, firstPart);
                Array.Copy(_buffer, 0, destination, firstPart, _count - firstPart);
            }
        }

        public void Clear()
        {
            _head = 0;
            _count = 0;
        }

        public double Sum()
        {
            double sum = 0.0;
            for (int i = 0; i < _count; i++)
            {
                int index = (_head - _count + i + _capacity) % _capacity;
                sum += _buffer[index];
            }
            return sum;
        }
    }

    public interface IDigitalFilter
    {
        double Filter(double input);
        void Reset();
        string GetFilterType();
        Dictionary<string, double> GetFilterParameters();
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

        /// <summary>
        /// Returns the filter type as a string
        /// </summary>
        /// <returns>Filter type name</returns>
        public string GetFilterType()
        {
            return "Exponential Low Pass";
        }

        /// <summary>
        /// Returns all filter parameters as a dictionary
        /// </summary>
        /// <returns>Dictionary containing filter parameters</returns>
        public Dictionary<string, double> GetFilterParameters()
        {
            Dictionary<string, double> parameters = new Dictionary<string, double>();
            parameters.Add("Alpha", Alpha);
            return parameters;
        }
    }

    /// <summary>
    /// Exponential High Pass Filter implementation
    /// </summary>
    public class ExponentialHighPassFilter : IDigitalFilter
    {
        private readonly ExponentialLowPassFilter _lowPass;

        /// <summary>
        /// Exposes the alpha value of the internal low pass filter
        /// </summary>
        public double Alpha
        {
            get
            {
                return _lowPass.Alpha;
            }
            set
            {
                _lowPass.Alpha = value;
            }
        }

        /// <summary>
        /// Create a new exponential high pass filter
        /// </summary>
        /// <param name="alpha">Smoothing factor (0 < alpha <= 1). Lower alpha = more smoothing.</param>
        public ExponentialHighPassFilter(double alpha)
        {
            _lowPass = new ExponentialLowPassFilter(alpha);
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
            return highPassValue;
        }

        /// <summary>
        /// Reset the filter to its initial state
        /// </summary>
        public void Reset()
        {
            _lowPass.Reset();
        }

        /// <summary>
        /// Returns the filter type as a string
        /// </summary>
        /// <returns>Filter type name</returns>
        public string GetFilterType()
        {
            return "Exponential High Pass";
        }

        /// <summary>
        /// Returns all filter parameters as a dictionary
        /// </summary>
        /// <returns>Dictionary containing filter parameters</returns>
        public Dictionary<string, double> GetFilterParameters()
        {
            Dictionary<string, double> parameters = new Dictionary<string, double>();
            parameters.Add("Alpha", Alpha);
            return parameters;
        }
    }

    public class MovingAverageFilter : IDigitalFilter
    {
        private CircularBuffer _window;
        private int _windowSize;
        private double _sum;

        public MovingAverageFilter(int windowSize)
        {
            _windowSize = windowSize;
            _window = new CircularBuffer(windowSize);
            _sum = 0.0;
        }

        public double Filter(double input)
        {
            // Track the value being removed if buffer is full
            double removedValue = 0.0;
            bool bufferWasFull = _window.Count == _windowSize;

            if (bufferWasFull)
            {
                // Calculate which value will be overwritten
                // We need to subtract it from the sum before adding the new value
                double[] temp = new double[_windowSize];
                _window.CopyTo(temp);
                removedValue = temp[0]; // Oldest value that will be overwritten
                _sum -= removedValue;
            }

            _window.Add(input);
            _sum += input;

            return _sum / _window.Count;
        }

        public void Reset()
        {
            _window.Clear();
            _sum = 0.0;
        }

        /// <summary>
        /// Returns the filter type as a string
        /// </summary>
        /// <returns>Filter type name</returns>
        public string GetFilterType()
        {
            return "Moving Average";
        }

        /// <summary>
        /// Returns all filter parameters as a dictionary
        /// </summary>
        /// <returns>Dictionary containing filter parameters</returns>
        public Dictionary<string, double> GetFilterParameters()
        {
            Dictionary<string, double> parameters = new Dictionary<string, double>();
            parameters.Add("WindowSize", _windowSize);
            return parameters;
        }
    }

    public class MedianFilter : IDigitalFilter
    {
        private CircularBuffer _window;
        private int _windowSize;
        private double[] _sortBuffer;

        public MedianFilter(int windowSize)
        {
            _windowSize = windowSize;
            _window = new CircularBuffer(windowSize);
            _sortBuffer = new double[windowSize];
        }

        public double Filter(double input)
        {
            _window.Add(input);

            // Copy to pre-allocated sort buffer and sort only the valid portion
            _window.CopyTo(_sortBuffer);
            Array.Sort(_sortBuffer, 0, _window.Count);

            int n = _window.Count;
            if (n % 2 == 1)
                return _sortBuffer[n / 2];
            else
                return (_sortBuffer[n / 2 - 1] + _sortBuffer[n / 2]) / 2.0;
        }

        public void Reset()
        {
            _window.Clear();
        }

        /// <summary>
        /// Returns the filter type as a string
        /// </summary>
        /// <returns>Filter type name</returns>
        public string GetFilterType()
        {
            return "Median";
        }

        /// <summary>
        /// Returns all filter parameters as a dictionary
        /// </summary>
        /// <returns>Dictionary containing filter parameters</returns>
        public Dictionary<string, double> GetFilterParameters()
        {
            Dictionary<string, double> parameters = new Dictionary<string, double>();
            parameters.Add("WindowSize", _windowSize);
            return parameters;
        }
    }

    public class NotchFilter : IDigitalFilter
    {
        private readonly double _omega;
        private readonly double _r;
        private double _x1, _x2, _y1, _y2;
        private readonly double _notchFreq;
        private readonly double _sampleRate;
        private readonly double _bandwidth;

        /// <summary>
        /// Create a basic recursive notch filter
        /// </summary>
        /// <param name="notchFreq">Notch frequency (Hz)</param>
        /// <param name="sampleRate">Sample rate (Hz)</param>
        /// <param name="bandwidth">Bandwidth (Hz), typical values: 1-5 Hz</param>
        public NotchFilter(double notchFreq, double sampleRate, double bandwidth = 2.0)
        {
            _notchFreq = notchFreq;
            _sampleRate = sampleRate;
            _bandwidth = bandwidth;
            _omega = 2 * Math.PI * notchFreq / sampleRate;
            _r = 1 - 3 * bandwidth / sampleRate;
            _x1 = _x2 = _y1 = _y2 = 0.0;
        }

        public double Filter(double input)
        {
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

        public string GetFilterType()
        {
            return "Notch";
        }

        /// <summary>
        /// Returns all filter parameters as a dictionary
        /// </summary>
        /// <returns>Dictionary containing filter parameters</returns>
        public Dictionary<string, double> GetFilterParameters()
        {
            Dictionary<string, double> parameters = new Dictionary<string, double>();
            parameters.Add("NotchFreq", _notchFreq);
            parameters.Add("SampleRate", _sampleRate);
            parameters.Add("Bandwidth", _bandwidth);
            return parameters;
        }
    }

    /// <summary>
    /// Absolute Value Filter implementation - takes the absolute value of input data
    /// </summary>
    public class AbsoluteFilter : IDigitalFilter
    {
        public double Filter(double input)
        {
            return Math.Abs(input);
        }

        public void Reset()
        {
        }

        public string GetFilterType()
        {
            return "Absolute";
        }

        public Dictionary<string, double> GetFilterParameters()
        {
            return new Dictionary<string, double>();
        }
    }

    /// <summary>
    /// Squared Filter implementation - squares the input data
    /// </summary>
    public class SquaredFilter : IDigitalFilter
    {
        public double Filter(double input)
        {
            return input * input;
        }

        public void Reset()
        {
        }

        public string GetFilterType()
        {
            return "Squared";
        }

        public Dictionary<string, double> GetFilterParameters()
        {
            return new Dictionary<string, double>();
        }
    }

    /// <summary>
    /// Downsampling filter - updates output only every Nth sample, with linear interpolation between the two most recent anchors.
    /// This implementation uses the two most recent captured anchors and interpolates between them over the downsampling interval.
    /// </summary>
    public class DownsamplingFilter : IDigitalFilter
    {
        private int _rate;
        private int _counter;
        private double _anchorPrev;
        private double _anchorCurr;
        private int _anchorsCount;

        /// <summary>
        /// Create a new downsampling filter
        /// </summary>
        /// <param name="rate">Downsampling rate (>= 1). For rate=3 anchors are taken every 3rd sample.</param>
        public DownsamplingFilter(int rate)
        {
            _rate = rate;
            _counter = 0;
            _anchorPrev = 0.0;
            _anchorCurr = 0.0;
            _anchorsCount = 0;
        }

        /// <summary>
        /// Filter a new input value. The output is an interpolated value between the two most recent anchors.
        /// An anchor is captured every Nth sample; interpolation uses the two most recent anchors to produce a smooth transition.
        /// </summary>
        /// <param name="input">Input value</param>
        /// <returns>Interpolated/downsampled output value</returns>
        public double Filter(double input)
        {
            if (_anchorsCount == 0)
            {
                _anchorCurr = input;
                _anchorsCount = 1;
                _counter = 0;
                return _anchorCurr;
            }

            _counter++;

            if (_counter >= _rate)
            {
                if (_anchorsCount == 1)
                {
                    _anchorPrev = _anchorCurr;
                    _anchorCurr = input;
                    _anchorsCount = 2;
                }
                else
                {
                    _anchorPrev = _anchorCurr;
                    _anchorCurr = input;
                }

                _counter = 0;

                if (_rate == 1)
                    return _anchorCurr;
            }

            if (_anchorsCount >= 2)
            {
                if (_rate == 1)
                    return _anchorCurr;

                double t = (double)_counter / (double)_rate;
                return _anchorPrev + (_anchorCurr - _anchorPrev) * t;
            }

            return _anchorCurr;
        }

        public void Reset()
        {
            _counter = 0;
            _anchorPrev = 0.0;
            _anchorCurr = 0.0;
            _anchorsCount = 0;
        }

        public string GetFilterType()
        {
            return "Downsampling";
        }

        public Dictionary<string, double> GetFilterParameters()
        {
            Dictionary<string, double> parameters = new Dictionary<string, double>();
            parameters.Add("Rate", _rate);
            return parameters;
        }

        /// <summary>
        /// Public property to get/set the downsampling rate. Setting validates the value.
        /// </summary>
        public int Rate
        {
            get
            {
                return _rate;
            }
            set
            {
                _rate = value;
            }
        }
    }

    /// <summary>
    /// 2-Pole 2-Zero Digital Filter (Biquad) implementation
    /// Uses the difference equation: y[n] = b0*x[n] + b1*x[n-1] + b2*x[n-2] - a1*y[n-1] - a2*y[n-2]
    /// </summary>
    public class BiquadFilter : IDigitalFilter
    {
        public double B0 { get; set; }
        public double B1 { get; set; }
        public double B2 { get; set; }
        public double A1 { get; set; }
        public double A2 { get; set; }

        private double _x1, _x2;
        private double _y1, _y2;

        public BiquadFilter()
        {
            B0 = 1.0;
            B1 = 0.0;
            B2 = 0.0;
            A1 = 0.0;
            A2 = 0.0;
            Reset();
        }

        public BiquadFilter(double b0, double b1, double b2, double a1, double a2)
        {
            B0 = b0;
            B1 = b1;
            B2 = b2;
            A1 = a1;
            A2 = a2;
            Reset();
        }

        /// <summary>
        /// Filter a new input value
        /// </summary>
        /// <param name="input">Input value</param>
        /// <returns>Filtered output</returns>
        public double Filter(double input)
        {
            double output = B0 * input + B1 * _x1 + B2 * _x2 - A1 * _y1 - A2 * _y2;

            _x2 = _x1;
            _x1 = input;

            _y2 = _y1;
            _y1 = output;

            return output;
        }

        public void Reset()
        {
            _x1 = _x2 = 0.0;
            _y1 = _y2 = 0.0;
        }

        public string GetFilterType()
        {
            return "2-Pole 2-Zero Biquad Filter";
        }

        public Dictionary<string, double> GetFilterParameters()
        {
            Dictionary<string, double> parameters = new Dictionary<string, double>();
            parameters.Add("B0", B0);
            parameters.Add("B1", B1);
            parameters.Add("B2", B2);
            parameters.Add("A1", A1);
            parameters.Add("A2", A2);
            return parameters;
        }

        /// <summary>
        /// Sets all filter coefficients at once
        /// </summary>
        public void SetCoefficients(double b0, double b1, double b2, double a1, double a2)
        {
            B0 = b0;
            B1 = b1;
            B2 = b2;
            A1 = a1;
            A2 = a2;
        }
    }
}
