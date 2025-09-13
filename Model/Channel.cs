using System;
using System.ComponentModel;
using System.Collections.ObjectModel;

namespace SerialPlotDN_WPF.Model
{
    /// <summary>
    /// Represents a single channel that encapsulates both the data source and settings
    /// This eliminates the need for global channel indices and complex resolution logic
    /// Now also manages its own measurements
    /// </summary>
    public class Channel : INotifyPropertyChanged
    {
        private readonly IDataStream _ownerStream;
        private readonly int _indexWithinDatastream;
        private ChannelSettings _settings;
        private bool _disposed = false;

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Collection of measurements for this channel
        /// </summary>
        public ObservableCollection<Measurement> Measurements { get; private set; } = new ObservableCollection<Measurement>();

        /// <summary>
        /// Creates a new Channel instance
        /// </summary>
        /// <param name="ownerStream">The data stream that owns this channel</param>
        /// <param name="localChannelIndex">The local index within the owner stream (0-based)</param>
        /// <param name="settings">Channel settings for display and processing</param>
        public Channel(IDataStream ownerStream, int localChannelIndex, ChannelSettings settings)
        {
            if (ownerStream == null)
                throw new ArgumentNullException(nameof(ownerStream));
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            if (localChannelIndex < 0 || localChannelIndex >= ownerStream.ChannelCount)
                throw new ArgumentOutOfRangeException(nameof(localChannelIndex));

            _ownerStream = ownerStream;
            _indexWithinDatastream = localChannelIndex;
            _settings = settings;

            // Subscribe to settings changes
            _settings.PropertyChanged += OnSettingsChanged;
            
            // Subscribe to measurement requests from the settings
            _settings.MeasurementRequested += OnMeasurementRequested;

            // Apply settings to the stream if it supports channel configuration
            if (_ownerStream is IChannelConfigurable configurableStream)
            {
                configurableStream.SetChannelSetting(_indexWithinDatastream, _settings);
            }
        }

        /// <summary>
        /// The data stream that owns this channel
        /// </summary>
        public IDataStream OwnerStream 
        { 
            get { return _ownerStream; } 
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
                if (value == null)
                    throw new ArgumentNullException(nameof(value));

                if (_settings != value)
                {
                    // Unsubscribe from old settings
                    if (_settings != null)
                    {
                        _settings.PropertyChanged -= OnSettingsChanged;
                        _settings.MeasurementRequested -= OnMeasurementRequested;
                    }

                    _settings = value;

                    // Subscribe to new settings
                    _settings.PropertyChanged += OnSettingsChanged;
                    _settings.MeasurementRequested += OnMeasurementRequested;

                    // Apply new settings to the stream
                    if (_ownerStream is IChannelConfigurable configurableStream)
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

            return _ownerStream.CopyLatestTo(_indexWithinDatastream, destination, requestedSamples);
        }

        /// <summary>
        /// Gets whether the owner stream is currently connected
        /// </summary>
        public bool IsStreamConnected 
        { 
            get { return _ownerStream.IsConnected; } 
        }

        /// <summary>
        /// Gets whether the owner stream is currently streaming data
        /// </summary>
        public bool IsStreamStreaming 
        { 
            get { return _ownerStream.IsStreaming; } 
        }

        /// <summary>
        /// Gets the type of the owner stream
        /// </summary>
        public string StreamType 
        { 
            get { return _ownerStream.StreamType; } 
        }

        /// <summary>
        /// Adds a measurement to this channel
        /// </summary>
        /// <param name="measurementType">Type of measurement to add</param>
        public void AddMeasurement(MeasurementType measurementType)
        {
            if (!IsStreamConnected)
                return;

            Measurement measurement = new Measurement(measurementType, _ownerStream, _indexWithinDatastream, _settings);
            
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
            return $"{_ownerStream.GetHashCode()}_{_indexWithinDatastream}";
        }

        private void OnMeasurementRemoveRequested(object sender, EventArgs e)
        {
            if (sender is Measurement measurement)
            {
                RemoveMeasurement(measurement);
            }
        }

        /// <summary>
        /// Handles measurement requests from the ChannelSettings
        /// </summary>
        /// <param name="sender">ChannelSettings that requested the measurement</param>
        /// <param name="measurementType">Type of measurement to add</param>
        private void OnMeasurementRequested(object sender, MeasurementType measurementType)
        {
            AddMeasurement(measurementType);
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
            if (_ownerStream is IChannelConfigurable configurableStream)
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
                    _settings.MeasurementRequested -= OnMeasurementRequested;
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