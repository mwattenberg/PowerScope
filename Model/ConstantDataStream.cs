using System;
using System.ComponentModel;

namespace PowerScope.Model
{
    /// <summary>
    /// Special data stream that always returns a constant value
    /// Ultra-lightweight - no threading, no ring buffer, no state
    /// Used to represent constant operands in virtual channel operations
    /// </summary>
    public class ConstantDataStream : IDataStream
    {
        private readonly double _value;
        private bool _disposed;

        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler Disposing;

        public ConstantDataStream(double value)
        {
            _value = value;
        }

        /// <summary>
        /// Fills the destination array with the constant value
        /// This loop gets vectorized by JIT on modern .NET - extremely fast
        /// </summary>
        public int CopyLatestTo(int channel, double[] destination, int n)
        {
            if (_disposed || channel != 0 || destination == null || n <= 0)
                return 0;

            int count = Math.Min(n, destination.Length);
            
            for (int i = 0; i < count; i++)
            {
                destination[i] = _value;
            }

            return count;
        }

        public bool IsConnected
        {
            get { return !_disposed; }
        }

        public bool IsStreaming
        {
            get { return !_disposed; }
        }

        public string StreamType
        {
            get { return $"Constant ({_value:G})"; }
        }

        public string StatusMessage
        {
            get 
            { 
                if (_disposed)
                    return "Disposed";
                return $"Constant: {_value:G}";
            }
        }

        public int ChannelCount
        {
            get { return 1; }
        }

        public double SampleRate
        {
            get { return 0.0; }
        }

        public long TotalSamples
        {
            get { return long.MaxValue; }
        }

        public long TotalBits
        {
            get { return 0; }
        }

        public void Connect()
        {
        }

        public void Disconnect()
        {
        }

        public void StartStreaming()
        {
        }

        public void StopStreaming()
        {
        }

        public void clearData()
        {
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Disposing?.Invoke(this, EventArgs.Empty);
                _disposed = true;
            }
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
