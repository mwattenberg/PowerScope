using System;
using System.ComponentModel;
using System.Windows.Media;
using SerialPlotDN_WPF.View.UserControls;

namespace SerialPlotDN_WPF.Model
{
    /// <summary>
    /// Model class that holds all channel-related settings and configuration
    /// Implements INotifyPropertyChanged for data binding support
    /// </summary>
    public class ChannelSettings : INotifyPropertyChanged
    {
        private string _label = "Channel";
        private Color _color = Colors.Gray;
        private bool _isEnabled = true;
        private double _gain = 1.0;
        private double _offset = 0.0;
        private ChannelControl.CouplingMode _coupling = ChannelControl.CouplingMode.DC;
        private ChannelControl.FilterMode _filter = ChannelControl.FilterMode.None;

        /// <summary>
        /// Display label for the channel
        /// </summary>
        public string Label
        {
            get => _label;
            set
            {
                if (_label != value)
                {
                    _label = value;
                    OnPropertyChanged(nameof(Label));
                }
            }
        }

        /// <summary>
        /// Color used for the channel display
        /// </summary>
        public Color Color
        {
            get => _color;
            set
            {
                if (_color != value)
                {
                    _color = value;
                    OnPropertyChanged(nameof(Color));
                }
            }
        }

        /// <summary>
        /// Whether the channel is enabled/visible
        /// </summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    OnPropertyChanged(nameof(IsEnabled));
                }
            }
        }

        /// <summary>
        /// Gain/amplification factor for the channel
        /// </summary>
        public double Gain
        {
            get => _gain;
            set
            {
                if (Math.Abs(_gain - value) > 0.001) // Use tolerance for double comparison
                {
                    _gain = Math.Max(0.125, Math.Min(16.0, value)); // Clamp between 0.125-16
                    OnPropertyChanged(nameof(Gain));
                    OnPropertyChanged(nameof(GainOffsetDisplayText)); // Update display text
                }
            }
        }

        /// <summary>
        /// Offset/DC bias for the channel
        /// </summary>
        public double Offset
        {
            get => _offset;
            set
            {
                if (Math.Abs(_offset - value) > 0.001) // Use tolerance for double comparison
                {
                    _offset = value;
                    OnPropertyChanged(nameof(Offset));
                    OnPropertyChanged(nameof(GainOffsetDisplayText)); // Update display text
                }
            }
        }

        /// <summary>
        /// Coupling mode (DC or AC)
        /// </summary>
        public ChannelControl.CouplingMode Coupling
        {
            get => _coupling;
            set
            {
                if (_coupling != value)
                {
                    _coupling = value;
                    OnPropertyChanged(nameof(Coupling));
                }
            }
        }

        /// <summary>
        /// Filter mode (LPF, HPF, ABS, Squared, None)
        /// </summary>
        public ChannelControl.FilterMode Filter
        {
            get => _filter;
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
        /// Computed property for displaying gain and offset in the UI
        /// </summary>
        public string GainOffsetDisplayText
        {
            get
            {
                if (_offset >= 0)
                    return $"= y * {_gain:F2} + {_offset:F2}";
                else
                    return $"= y * {_gain:F2} - {Math.Abs(_offset):F2}";
            }
        }

        /// <summary>
        /// Event fired when any property changes
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Raises the PropertyChanged event
        /// </summary>
        /// <param name="propertyName">Name of the property that changed</param>
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Copy settings from another ChannelSettings instance
        /// </summary>
        /// <param name="other">Source ChannelSettings to copy from</param>
        public void CopyFrom(ChannelSettings other)
        {
            Label = other.Label;
            Color = other.Color;
            IsEnabled = other.IsEnabled;
            Gain = other.Gain;
            Offset = other.Offset;
            Coupling = other.Coupling;
            Filter = other.Filter;
        }

        /// <summary>
        /// Create a copy of this ChannelSettings instance
        /// </summary>
        /// <returns>New ChannelSettings instance with copied values</returns>
        public ChannelSettings Clone()
        {
            return new ChannelSettings
            {
                Label = this.Label,
                Color = this.Color,
                IsEnabled = this.IsEnabled,
                Gain = this.Gain,
                Offset = this.Offset,
                Coupling = this.Coupling,
                Filter = this.Filter
            };
        }

        /// <summary>
        /// Constructor with default values
        /// </summary>
        public ChannelSettings()
        {
            // Default values are already set in field declarations
        }

        /// <summary>
        /// Constructor with custom label and color
        /// </summary>
        /// <param name="label">Channel label</param>
        /// <param name="color">Channel color</param>
        public ChannelSettings(string label, Color color)
        {
            _label = label;
            _color = color;
        }
    }
}