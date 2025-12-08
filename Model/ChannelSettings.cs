using System;
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
        private bool _isVirtual = false;
        private Channel _ownerChannel;

        /// <summary>
        /// Event raised when a measurement is requested for this channel
        /// Channel subscribes to this and handles the measurement creation
        /// </summary>
        public event EventHandler<MeasurementType> MeasurementRequested;

        public event PropertyChangedEventHandler PropertyChanged;

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
                if (Math.Abs(_gain - value) > 1e-9)
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
                if (Math.Abs(_offset - value) > 1e-9)
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

        public bool IsVirtual
        {
            get 
            { 
                return _isVirtual; 
            }
            set
            {
                if (_isVirtual != value)
                {
                    _isVirtual = value;
                    OnPropertyChanged(nameof(IsVirtual));
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
        /// Gets the measurements collection from the owner channel.
        /// Returns an empty collection if no owner channel is set.
        /// This enables UI binding without creating circular dependencies.
        /// </summary>
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

        /// <summary>
        /// Gets the count of active measurements.
        /// Raises PropertyChanged when measurements are added/removed.
        /// Used for UI button styling (e.g., highlight when count > 0).
        /// </summary>
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

        /// <summary>
        /// Gets whether any measurements are active.
        /// Useful for boolean binding in XAML (e.g., button styling).
        /// </summary>
        public bool HasMeasurements
        {
            get { return MeasurementCount > 0; }
        }

        /// <summary>
        /// Adds a measurement of the specified type to this channel.
        /// Direct method call (not event-based) for clean architecture.
        /// Delegates to the owner channel if available.
        /// </summary>
        /// <param name="measurementType">Type of measurement to add</param>
        public void AddMeasurement(MeasurementType measurementType)
        {
            if (_ownerChannel != null)
            {
                _ownerChannel.AddMeasurement(measurementType);
            }
        }

        /// <summary>
        /// Removes a specific measurement from this channel.
        /// Direct method call (not event-based) for clean architecture.
        /// Delegates to the owner channel if available.
        /// </summary>
        /// <param name="measurement">The measurement to remove</param>
        public void RemoveMeasurement(Measurement measurement)
        {
      if (_ownerChannel != null && measurement != null)
  {
        _ownerChannel.RemoveMeasurement(measurement);
 }
        }

   /// <summary>
        /// Requests a measurement of the specified type to be added to this channel.
        /// Called by UI controls, handled by the Channel that owns these settings.
        /// DEPRECATED: Use AddMeasurement() instead for cleaner event-free architecture.
        /// </summary>
        /// <param name="measurementType">Type of measurement to add</param>
    public void RequestMeasurement(MeasurementType measurementType)
  {
            MeasurementRequested?.Invoke(this, measurementType);
        }

        /// <summary>
        /// Internal method called by Channel to establish the back-reference.
        /// This is set during Channel construction and never changes.
        /// Ensures ChannelSettings can access its owner Channel's Measurements.
        /// </summary>
        /// <param name="ownerChannel">The Channel that owns this ChannelSettings</param>
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

        /// <summary>
        /// Called when measurements are added or removed.
        /// Raises PropertyChanged for MeasurementCount and HasMeasurements.
        /// </summary>
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