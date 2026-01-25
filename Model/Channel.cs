using System;
using System.ComponentModel;
using System.Collections.ObjectModel;

namespace PowerScope.Model
{
    /// <summary>
    /// Represents a single channel that encapsulates both the data source and settings
    /// The stream can have multiple channels, each represented by a Channel instance
    /// This eliminates the need for global channel indices and complex resolution logic
    /// Now also manages its own measurements
    /// Supports both physical channels (backed by IDataStream) and virtual channels (VirtualDataStream)
    /// </summary>
    public class Channel : INotifyPropertyChanged
    {
        private readonly IDataStream _stream;
        private readonly int _indexWithinDatastream;
        private ChannelSettings _settings;
        private bool _disposed = false;

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Collection of measurements for this channel
        /// </summary>
        public ObservableCollection<Measurement> Measurements { get; private set; } = new ObservableCollection<Measurement>();

        /// <summary>
        /// Creates a new Channel instance (physical channel backed by IDataStream)
        /// </summary>
        /// <param name="stream">The data stream that owns this channel</param>
        /// <param name="localChannelIndex">The local index within the owner stream (0-based)</param>
        /// <param name="settings">Channel settings for display and processing</param>
        public Channel(IDataStream stream, int localChannelIndex, ChannelSettings settings)
        {
            if (localChannelIndex < 0 || localChannelIndex >= stream.ChannelCount)
                throw new ArgumentOutOfRangeException(nameof(localChannelIndex));

            _stream = stream;
            _indexWithinDatastream = localChannelIndex;
            _settings = settings;

            InitializeChannel();
        }

        /// <summary>
        /// Creates a new virtual Channel from a single source channel (for filtering/transformation)
        /// </summary>
        /// <param name="sourceChannel">The channel to use as source</param>
        /// <param name="settings">Channel settings for display and processing</param>
        public Channel(Channel sourceChannel, ChannelSettings settings)
        {
            if (sourceChannel == null)
                throw new ArgumentNullException(nameof(sourceChannel));

            // Create virtual data stream
            _stream = new VirtualDataStream(sourceChannel);
            _indexWithinDatastream = 0; // Virtual streams always use channel 0
            _settings = settings;

            InitializeChannel();
        }

        /// <summary>
        /// Creates a new virtual Channel from two sources with mathematical operation
        /// </summary>
        public Channel(Channel sourceChannel1, Channel sourceChannel2, VirtualChannelOperationType operation, ChannelSettings settings)
        {
            if (sourceChannel1 == null)
                throw new ArgumentNullException(nameof(sourceChannel1));
            if (sourceChannel2 == null)
                throw new ArgumentNullException(nameof(sourceChannel2));

            _stream = new VirtualDataStream(sourceChannel1, sourceChannel2, operation);
            _indexWithinDatastream = 0;
            _settings = settings;

            InitializeChannel();
        }

        /// <summary>
        /// Common initialization for both physical and virtual channels
        /// </summary>
        private void InitializeChannel()
        {
            // Establish back-reference in ChannelSettings for ViewModel pattern
            _settings.SetOwnerChannel(this);
            
            // Subscribe to settings changes
            _settings.PropertyChanged += OnSettingsChanged;

            // Apply settings to the stream if it supports channel configuration
            if (_stream is IChannelConfigurable configurableStream)
            {
                configurableStream.SetChannelSetting(_indexWithinDatastream, _settings);
            }
        }

        /// <summary>
        /// The data stream that owns this channel
        /// </summary>
        public IDataStream OwnerStream 
        { 
            get { return _stream; } 
        }

        /// <summary>
        /// The local channel index within the owner stream (0-based)
        /// </summary>
        public int LocalChannelIndex 
        { 
            get { return _indexWithinDatastream; } 
        }

        /// <summary>
        /// Channel settings for display and processing
        /// </summary>
        public ChannelSettings Settings
        {
            get { return _settings; }
            set
            {
                if (_settings != value)
                {
                    // Unsubscribe from old settings
                    if (_settings != null)
                    {
                        _settings.PropertyChanged -= OnSettingsChanged;
                    }

                    _settings = value;

                    // Subscribe to new settings
                    _settings.PropertyChanged += OnSettingsChanged;

                    // Apply new settings to the stream
                    if (_stream is IChannelConfigurable configurableStream)
                    {
                        configurableStream.SetChannelSetting(_indexWithinDatastream, _settings);
                    }

                    OnPropertyChanged(nameof(Settings));
                }
            }
        }

        /// <summary>
        /// Gets the channel label for display purposes
        /// </summary>
        public string Label 
        { 
            get { return _settings.Label; } 
        }

        /// <summary>
        /// Gets whether this channel is currently enabled
        /// </summary>
        public bool IsEnabled 
        { 
            get { return _settings.IsEnabled; } 
        }

        /// <summary>
        /// Gets the display color for this channel
        /// </summary>
        public System.Windows.Media.Color Color 
        { 
            get { return _settings.Color; } 
        }

        /// <summary>
        /// Copies the latest data from this channel to the destination array
        /// </summary>
        /// <param name="destination">Destination array for the data</param>
        /// <param name="requestedSamples">Number of samples to copy</param>
        /// <returns>Actual number of samples copied</returns>
        public int CopyLatestDataTo(double[] destination, int requestedSamples)
        {
            if (_disposed)
                return 0;

            return _stream.CopyLatestTo(_indexWithinDatastream, destination, requestedSamples);
        }

        /// <summary>
        /// Gets whether the owner stream is currently connected
        /// </summary>
        public bool IsStreamConnected 
        { 
            get { return _stream.IsConnected; } 
        }

        /// <summary>
        /// Gets whether the owner stream is currently streaming data
        /// </summary>
        public bool IsStreamStreaming 
        { 
            get { return _stream.IsStreaming; } 
        }

        /// <summary>
        /// Gets the type of the owner stream
        /// </summary>
        public string StreamType 
        { 
            get { return _stream.StreamType; } 
        }

        /// <summary>
        /// Adds a measurement to this channel
        /// </summary>
        /// <param name="measurementType">Type of measurement to add</param>
        public void AddMeasurement(MeasurementType measurementType)
        {
            if (!IsStreamConnected)
                return;

            Measurement measurement = new Measurement(measurementType, _stream, _indexWithinDatastream, _settings);
            
            // Subscribe to removal request
            measurement.RemoveRequested += OnMeasurementRemoveRequested;
            
            Measurements.Add(measurement);
        }

        /// <summary>
        /// Removes a specific measurement from this channel
        /// </summary>
        /// <param name="measurement">The measurement to remove</param>
        public void RemoveMeasurement(Measurement measurement)
        {
            if (measurement != null && Measurements.Contains(measurement))
            {
                measurement.RemoveRequested -= OnMeasurementRemoveRequested;
                measurement.Dispose();
                Measurements.Remove(measurement);
            }
        }

        /// <summary>
        /// Updates all measurements for this channel
        /// Called by MeasurementBar timer
        /// </summary>
        public void UpdateAllMeasurements()
        {
            if (_disposed)
                return;

            foreach (Measurement measurement in Measurements)
            {
                if (!measurement.IsDisposed)
                {
                    measurement.UpdateMeasurement();
                }
            }
        }

        /// <summary>
        /// Creates a globally unique identifier for this channel
        /// Useful for tracking channels across UI components
        /// </summary>
        public string GetChannelId()
        {
            return $"{_stream.GetHashCode()}_{_indexWithinDatastream}";
        }

        private void OnMeasurementRemoveRequested(object sender, EventArgs e)
        {
            if (sender is Measurement measurement)
            {
                RemoveMeasurement(measurement);
            }
        }

        private void OnSettingsChanged(object sender, PropertyChangedEventArgs e)
        {
            // Forward settings property changes
            OnPropertyChanged($"Settings.{e.PropertyName}");

            // Also forward specific properties that UI might bind to directly
            switch (e.PropertyName)
            {
                case nameof(ChannelSettings.Label):
                    OnPropertyChanged(nameof(Label));
                    break;
                case nameof(ChannelSettings.IsEnabled):
                    OnPropertyChanged(nameof(IsEnabled));
                    break;
                case nameof(ChannelSettings.Color):
                    OnPropertyChanged(nameof(Color));
                    break;
            }

            // Apply updated settings to the stream
            if (_stream is IChannelConfigurable configurableStream)
            {
                configurableStream.SetChannelSetting(_indexWithinDatastream, _settings);
            }
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        /// <summary>
        /// Disposes the channel and cleans up resources
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                // Dispose all measurements
                foreach (Measurement measurement in Measurements)
                {
                    measurement.RemoveRequested -= OnMeasurementRemoveRequested;
                    measurement.Dispose();
                }
                Measurements.Clear();

                if (_settings != null)
                {
                    _settings.PropertyChanged -= OnSettingsChanged;
                }

                _disposed = true;
            }
        }

        public override string ToString()
        {
            return $"Channel: {Label} ({StreamType} Stream, Local Index: {LocalChannelIndex}, {Measurements.Count} measurements)";
        }
    }
}