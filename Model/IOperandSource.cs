using System;
using System.ComponentModel;

namespace PowerScope.Model
{
    /// <summary>
    /// Represents a source for virtual channel operations
    /// Both channels and constants are represented as Channel objects
    /// Constants use ConstantDataStream internally
    /// </summary>
    public interface IVirtualSource : INotifyPropertyChanged
    {
        /// <summary>
        /// Gets the channel for this operand
        /// For constants, this returns a Channel backed by ConstantDataStream
        /// </summary>
        Channel Channel { get; }

        /// <summary>
        /// Gets a display string for this operand
        /// </summary>
        string DisplayString { get; }
    }

    /// <summary>
    /// Channel-based operand source
    /// </summary>
    public class ChannelOperand : IVirtualSource
    {
        private Channel _channel;

        public Channel Channel
        {
            get { return _channel; }
            set
            {
                if (_channel != value)
                {
                    _channel = value;
                    OnPropertyChanged(nameof(Channel));
                    OnPropertyChanged(nameof(DisplayString));
                }
            }
        }

        public string DisplayString
        {
            get { return _channel?.Label ?? "(null)"; }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public ChannelOperand(Channel channel)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public override string ToString()
        {
            return DisplayString;
        }
    }

    /// <summary>
    /// Constant value operand source
    /// Creates a hidden Channel backed by ConstantDataStream
    /// </summary>
    public class ConstantOperand : IVirtualSource
    {
        private double _value;
        private Channel _constantChannel;

        public Channel Channel
        {
            get { return _constantChannel; }
        }

        public string DisplayString
        {
            get { return _value.ToString("G"); }
        }

        public double ConstantValue
        {
            get { return _value; }
            set
            {
                if (Math.Abs(_value - value) > 1e-15)
                {
                    _value = value;
                    RecreateConstantChannel();
                    OnPropertyChanged(nameof(ConstantValue));
                    OnPropertyChanged(nameof(DisplayString));
                    OnPropertyChanged(nameof(Channel));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public ConstantOperand(double value = 0.0)
        {
            _value = value;
            RecreateConstantChannel();
        }

        private void RecreateConstantChannel()
        {
            if (_constantChannel != null)
            {
                _constantChannel.Dispose();
            }

            ConstantDataStream stream = new ConstantDataStream(_value);

            ChannelSettings settings = new ChannelSettings
            {
                Label = $"Constant: {_value:G}",
                Color = System.Windows.Media.Colors.DarkGray,
                IsEnabled = true,
                IsVirtual = false
            };

            _constantChannel = new Channel(stream, 0, settings);
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public override string ToString()
        {
            return DisplayString;
        }
    }
}
