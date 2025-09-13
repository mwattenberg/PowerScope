using System;
using System.ComponentModel;
using System.Windows.Media;

namespace SerialPlotDN_WPF.Model
{
    /// <summary>
    /// Event args for measurement request
    /// </summary>
    public class MeasurementRequestEventArgs : EventArgs
    {
        public int ChannelIndex { get; }
        public ChannelSettings ChannelSettings { get; }

        public MeasurementRequestEventArgs(int channelIndex, ChannelSettings channelSettings)
        {
            ChannelIndex = channelIndex;
            ChannelSettings = channelSettings;
        }
    }

    public class ChannelSettings : INotifyPropertyChanged
    {
        private string _label = "Channel";
        private Color _color = Colors.Gray;
        private bool _isEnabled = true;
        private double _gain = 1.0;
        private double _offset = 0.0;
        private IDigitalFilter? _filter = null;
        private int _channelIndex = -1;

        public string Label
        {
            get 
            { 
                return _label; 
            }
            set
            {
                if (_label != value)
                {
                    _label = value;
                    OnPropertyChanged(nameof(Label));
                }
            }
        }

        public Color Color
        {
            get 
            { 
                return _color; 
            }
            set
            {
                if (_color != value)
                {
                    _color = value;
                    OnPropertyChanged(nameof(Color));
                    OnPropertyChanged(nameof(DisplayColor));
                }
            }
        }

        public bool IsEnabled
        {
            get 
            { 
                return _isEnabled; 
            }
            set
            {
                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    OnPropertyChanged(nameof(IsEnabled));
                    OnPropertyChanged(nameof(DisplayColor));
                }
            }
        }

        public double Gain
        {
            get 
            { 
                return _gain; 
            }
            set
            {
                if (Math.Abs(_gain - value) > 0.001)
                {
                    _gain = value;
                    OnPropertyChanged(nameof(Gain));
                    //OnPropertyChanged(nameof(GainOffsetDisplayText));
                }
            }
        }

        public double Offset
        {
            get 
            { 
                return _offset; 
            }
            set
            {
                if (Math.Abs(_offset - value) > 0.001)
                {
                    _offset = value;
                    OnPropertyChanged(nameof(Offset));
                    //OnPropertyChanged(nameof(GainOffsetDisplayText));
                }
            }
        }

        public IDigitalFilter Filter
        {
            get 
            { 
                return _filter; 
            }
            set
            {
                if (_filter != value)
                {
                    _filter = value;
                    OnPropertyChanged(nameof(Filter));
                }
            }
        }

        /// <summary>
        /// Channel index for this settings instance
        /// </summary>
        public int ChannelIndex
        {
            get 
            { 
                return _channelIndex; 
            }
            set
            {
                if (_channelIndex != value)
                {
                    _channelIndex = value;
                    OnPropertyChanged(nameof(ChannelIndex));
                }
            }
        }

        public Color DisplayColor
        {
            get
            {
                if (_isEnabled)
                    return _color;
                else
                    return Colors.Gray;
            }
        }

        //public string GainOffsetDisplayText
        //{
        //    get
        //    {
        //        if (_offset >= 0)
        //            return string.Format("= y * {0:F2} + {1:F2}", _gain, _offset);
        //        else
        //            return string.Format("= y * {0:F2} - {1:F2}", _gain, Math.Abs(_offset));
        //    }
        //}

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
                PropertyChanged.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public ChannelSettings()
        {
        }
    }
}