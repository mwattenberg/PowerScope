using System;
using System.ComponentModel;
using System.Windows.Media;

namespace PowerScope.Model
{
    public class ChannelSettings : INotifyPropertyChanged
    {
        private string _label = "Channel";
        private Color _color = Colors.Gray;
        private bool _isEnabled = true;
        private double _gain = 1.0;
        private double _offset = 0.0;
        private IDigitalFilter? _filter = null;

        /// <summary>
        /// Event raised when a measurement is requested for this channel
        /// Channel subscribes to this and handles the measurement creation
        /// </summary>
        public event EventHandler<MeasurementType> MeasurementRequested;

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

        /// <summary>
        /// Requests a measurement of the specified type to be added to this channel
        /// Called by UI controls, handled by the Channel that owns these settings
        /// </summary>
        /// <param name="measurementType">Type of measurement to add</param>
        public void RequestMeasurement(MeasurementType measurementType)
        {
            MeasurementRequested?.Invoke(this, measurementType);
        }

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