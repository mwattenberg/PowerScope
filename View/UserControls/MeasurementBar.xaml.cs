using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using SerialPlotDN_WPF.Model;

namespace SerialPlotDN_WPF.View.UserControls
{
    /// <summary>
    /// Interaction logic for MeasurementBar.xaml
    /// </summary>
    public partial class MeasurementBar : UserControl
    {
        private int _measurementCounter = 0;
        
        // Lists to manage measurements (similar to DataStreamBar pattern)
        public List<Measurement> ActiveMeasurements { get; private set; } = new List<Measurement>();
        public List<MeasurementBox> MeasurementBoxes { get; private set; } = new List<MeasurementBox>();
        
        // Reference to DataStreamBar to access connected streams
        private DataStreamBar _dataStreamBar;
        
        // Reference to ChannelSettings collection
        private ObservableCollection<ChannelSettings> _channelSettings;
        
        // Reference to SystemManager for measurement registration
        private SystemManager _systemManager;
        
        /// <summary>
        /// DataStreamBar dependency - must be set after construction when using XAML instantiation
        /// </summary>
        public DataStreamBar DataStreamBar
        {
            get => _dataStreamBar;
            set => _dataStreamBar = value ?? throw new ArgumentNullException(nameof(value));
        }
        
        /// <summary>
        /// ChannelSettings dependency - must be set after construction when using XAML instantiation
        /// </summary>
        public ObservableCollection<ChannelSettings> ChannelSettings
        {
            get => _channelSettings;
            set => _channelSettings = value ?? throw new ArgumentNullException(nameof(value));
        }
        
        /// <summary>
        /// SystemManager dependency - must be set after construction for measurement updates
        /// </summary>
        public SystemManager SystemManager
        {
            get => _systemManager;
            set => _systemManager = value;
        }
        
        public MeasurementBar()
        {
            InitializeComponent();
        }

        private void Button_Cursor_Click(object sender, RoutedEventArgs e)
        {
            // Add cursor functionality here - placeholder for future implementation
            // This could show/hide plot cursors or create cursor-related measurements
        }

        /// <summary>
        /// Add a measurement for a specific channel and measurement type
        /// </summary>
        /// <param name="measurementType">Type of measurement to create</param>
        /// <param name="channelIndex">Zero-based channel index</param>
        public void AddMeasurement(MeasurementType measurementType, int channelIndex)
        {
            // Check if DataStreamBar is set
            if (_dataStreamBar == null)
            {
                MessageBox.Show("DataStreamBar is not initialized. This is a programming error.",
                              "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Check if ChannelSettings is set
            if (_channelSettings == null)
            {
                MessageBox.Show("ChannelSettings is not initialized. This is a programming error.",
                              "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Check if we have any connected streams
            if (_dataStreamBar.ConnectedDataStreams.Count == 0)
            {
                MessageBox.Show("No data streams are connected. Please add and connect a data stream first.",
                              "No Data Available", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Check if we have channel settings for this channel
            if (_channelSettings.Count <= channelIndex)
            {
                MessageBox.Show($"Channel {channelIndex + 1} settings are not available.",
                              "Channel Settings Not Available", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Find which data stream contains this channel
            var (dataStream, localChannelIndex) = FindDataStreamForChannel(channelIndex);
            if (dataStream == null)
            {
                MessageBox.Show($"Channel {channelIndex + 1} is not available. Please check your data streams.",
                              "Channel Not Available", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _measurementCounter++;

            // Create the measurement - now it handles all data management internally!
            var measurement = new Measurement(measurementType, dataStream, localChannelIndex);
            ActiveMeasurements.Add(measurement);
            
            // Register measurement with SystemManager for updates
            _systemManager?.RegisterMeasurement(measurement);

            // Get the actual ChannelSettings object for this channel
            var channelSetting = _channelSettings[channelIndex];
            
            // Create the UI representation
            var measurementBox = new MeasurementBox();
            // Set the actual ChannelSettings as DataContext - no more ChannelInfo needed!
            measurementBox.DataContext = channelSetting;
            
            // Set the measurement for result updates - MeasurementBox can now bind directly to measurement.Result
            measurementBox.SetMeasurement(measurement);
            
            // Subscribe to remove event
            measurementBox.OnRemoveClickedEvent += (s, args) => 
            {
                RemoveMeasurement(measurement, measurementBox);
            };
            
            MeasurementBoxes.Add(measurementBox);
            Panel_MeasurementBoxes.Children.Add(measurementBox);
        }
        
        /// <summary>
        /// Find which data stream contains the specified global channel index
        /// </summary>
        /// <param name="globalChannelIndex">Global channel index across all streams</param>
        /// <returns>Tuple of (DataStream, LocalChannelIndex) or (null, -1) if not found</returns>
        private (IDataStream dataStream, int localChannelIndex) FindDataStreamForChannel(int globalChannelIndex)
        {
            int currentChannelOffset = 0;
            
            foreach (var dataStream in _dataStreamBar.ConnectedDataStreams)
            {
                if (globalChannelIndex < currentChannelOffset + dataStream.ChannelCount)
                {
                    // This stream contains our target channel
                    int localChannelIndex = globalChannelIndex - currentChannelOffset;
                    return (dataStream, localChannelIndex);
                }
                currentChannelOffset += dataStream.ChannelCount;
            }
            
            return (null, -1); // Channel not found
        }
        
        /// <summary>
        /// Remove a measurement and its UI representation
        /// </summary>
        private void RemoveMeasurement(Measurement measurement, MeasurementBox measurementBox)
        {
            // Unregister from SystemManager
            _systemManager?.UnregisterMeasurement(measurement);
            
            // Dispose the measurement (stops timer and cleans up)
            measurement?.Dispose();
            
            // Remove from lists
            ActiveMeasurements.Remove(measurement);
            MeasurementBoxes.Remove(measurementBox);
            
            // Remove from UI
            Panel_MeasurementBoxes.Children.Remove(measurementBox);
        }
        
        /// <summary>
        /// Clear all measurements
        /// </summary>
        public void ClearAllMeasurements()
        {
            // Unregister and dispose all measurements
            foreach (var measurement in ActiveMeasurements)
            {
                _systemManager?.UnregisterMeasurement(measurement);
                measurement?.Dispose();
            }
            
            // Clear lists
            ActiveMeasurements.Clear();
            MeasurementBoxes.Clear();
            
            // Clear UI
            Panel_MeasurementBoxes.Children.Clear();
            
            _measurementCounter = 0;
        }
        
        /// <summary>
        /// Dispose of all resources
        /// </summary>
        public void Dispose()
        {
            ClearAllMeasurements();
        }
    }
}
