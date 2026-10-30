using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Media;

namespace PowerScope.Model
{
    /// <summary>
    /// ChannelSettings serves as the MVVM ViewModel for channel UI display and interaction.
    /// Contains both display properties (Label, Color) and interaction properties (Gain, Offset, Filter).
    /// Maintains a back-reference to the owner Channel to provide access to Measurements collection.
    /// This eliminates the need for separate ViewModel classes while keeping concerns clean.
    /// </summary>
    public class ChannelSettings : INotifyPropertyChanged
    {
        private string _label = "Channel";
        private Color _color = Colors.Gray;
        private bool _isEnabled = true;
        private double _gain = 1.0;
        private double _offset = 0.0;
        private IDigitalFilter? _filter = null;
        private Channel _ownerChannel;

        public event EventHandler<MeasurementType> MeasurementRequested;
        public event PropertyChangedEventHandler PropertyChanged;

        public string Label
        {
            get { return _label; }
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
            get { return _color; }
            set
            {
                if (_color != value)
                {
                    _color = value;
                    OnPropertyChanged(nameof(Color));
                    OnPropertyChanged(nameof(DisplayColor));
                    OnPropertyChanged(nameof(ReferenceColor));
                }
            }
        }

        public bool IsEnabled
        {
            get { return _isEnabled; }
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
            get { return _gain; }
            set
            {
                if (Math.Abs(_gain - value) > 1e-9)
                {
                    _gain = value;
                    OnPropertyChanged(nameof(Gain));
                }
            }
        }

        public double Offset
        {
            get { return _offset; }
            set
            {
                if (Math.Abs(_offset - value) > 1e-9)
                {
                    _offset = value;
                    OnPropertyChanged(nameof(Offset));
                }
            }
        }

        public IDigitalFilter Filter
        {
            get { return _filter; }
            set
            {
                if (_filter != value)
                {
                    _filter = value;
                    OnPropertyChanged(nameof(Filter));
                    OnPropertyChanged(nameof(HasFilter));
                }
            }
        }

        public bool HasFilter
        {
            get { return _filter != null; }
        }

        private bool _hasReferenceWaveform = false;

        /// <summary>
        /// Whether a frozen reference waveform is currently captured for this channel.
        /// Toggling this from the UI is what PlotManager listens for to capture/clear
        /// the static overlay trace - the captured samples themselves live in PlotManager,
        /// not here, since this class only carries display/interaction state.
        /// </summary>
        public bool HasReferenceWaveform
        {
            get { return _hasReferenceWaveform; }
            set
            {
                if (_hasReferenceWaveform != value)
                {
                    _hasReferenceWaveform = value;
                    OnPropertyChanged(nameof(HasReferenceWaveform));
                }
            }
        }

        /// <summary>
        /// Gets whether this channel is backed by a VirtualDataStream.
        /// Computed property - automatically correct based on the underlying stream type.
        /// No manual setting required, eliminating possibility of desynchronization.
        /// </summary>
        public bool IsVirtual
        {
            get { return _ownerChannel?.OwnerStream is VirtualDataStream; }
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
        /// The reference-waveform overlay's color: this channel's Color, desaturated.
        /// Single source of truth for that color - PlotManager reads it for the plotted
        /// overlay and ChannelControl binds to it for the capture button's highlight, so
        /// the two always match without either side re-deriving the value independently.
        /// </summary>
        public Color ReferenceColor
        {
            get { return Desaturate(_color, 0.5); }
        }

        /// <summary>
        /// Reduces a color's saturation by the given fraction (0-1) in HSL space, keeping
        /// hue and lightness unchanged.
        /// </summary>
        private static Color Desaturate(Color color, double desaturateAmount)
        {
            double r = color.R / 255.0;
            double g = color.G / 255.0;
            double b = color.B / 255.0;

            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double l = (max + min) / 2.0;
            double h = 0, s = 0;

            if (max != min)
            {
                double d = max - min;
                s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
                if (max == r)
                    h = (g - b) / d + (g < b ? 6 : 0);
                else if (max == g)
                    h = (b - r) / d + 2;
                else
                    h = (r - g) / d + 4;
                h /= 6.0;
            }

            s *= 1.0 - desaturateAmount;

            double r2, g2, b2;
            if (s == 0)
            {
                r2 = g2 = b2 = l;
            }
            else
            {
                double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
                double p = 2 * l - q;
                r2 = HueToRgb(p, q, h + 1.0 / 3.0);
                g2 = HueToRgb(p, q, h);
                b2 = HueToRgb(p, q, h - 1.0 / 3.0);
            }

            return Color.FromRgb((byte)Math.Round(r2 * 255), (byte)Math.Round(g2 * 255), (byte)Math.Round(b2 * 255));
        }

        private static double HueToRgb(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6.0) return p + (q - p) * 6 * t;
            if (t < 1.0 / 2.0) return q;
            if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6;
            return p;
        }

        public Channel OwnerChannel
        {
            get { return _ownerChannel; }
        }

        /// <summary>
        /// Gets the first parent channel if this is a virtual channel
        /// Returns null for physical channels
        /// </summary>
        public Channel ParentChannelA
        {
            get
            {
                if (_ownerChannel?.OwnerStream is VirtualDataStream virtualStream)
                {
                    return virtualStream.GetParentChannelA();
                }
                return null;
            }
        }

        /// <summary>
        /// Gets the second parent channel if this is a binary virtual channel
        /// Returns null for physical channels or single-source virtuals
        /// </summary>
        public Channel ParentChannelB
        {
            get
            {
                if (_ownerChannel?.OwnerStream is VirtualDataStream virtualStream)
                {
                    return virtualStream.GetParentChannelB();
                }
                return null;
            }
        }

        /// <summary>
        /// Gets whether this is a binary operation virtual channel (two parents)
        /// </summary>
        public bool IsBinaryVirtual
        {
            get
            {
                if (_ownerChannel?.OwnerStream is VirtualDataStream virtualStream)
                {
                    return virtualStream.IsBinaryOperation;
                }
                return false;
            }
        }

        public ObservableCollection<Measurement> Measurements
        {
            get
            {
                if (_ownerChannel != null)
                {
                    return _ownerChannel.Measurements;
                }
                return new ObservableCollection<Measurement>();
            }
        }

        public int MeasurementCount
        {
            get
            {
                if (_ownerChannel != null)
                {
                    return _ownerChannel.Measurements.Count;
                }
                return 0;
            }
        }

        public bool HasMeasurements
        {
            get { return MeasurementCount > 0; }
        }

        public void AddMeasurement(MeasurementType measurementType)
        {
            if (_ownerChannel != null)
            {
                _ownerChannel.AddMeasurement(measurementType);
            }
        }

        public void RemoveMeasurement(Measurement measurement)
        {
            if (_ownerChannel != null && measurement != null)
            {
                _ownerChannel.RemoveMeasurement(measurement);
            }
        }

        public void RequestMeasurement(MeasurementType measurementType)
        {
            MeasurementRequested?.Invoke(this, measurementType);
        }

        internal void SetOwnerChannel(Channel ownerChannel)
        {
            if (_ownerChannel != null)
            {
                _ownerChannel.Measurements.CollectionChanged -= OnMeasurementsCollectionChanged;
            }

            _ownerChannel = ownerChannel;

            if (_ownerChannel != null)
            {
                _ownerChannel.Measurements.CollectionChanged += OnMeasurementsCollectionChanged;
            }
        }

        private void OnMeasurementsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(MeasurementCount));
            OnPropertyChanged(nameof(HasMeasurements));
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public ChannelSettings()
        {
        }
    }
}