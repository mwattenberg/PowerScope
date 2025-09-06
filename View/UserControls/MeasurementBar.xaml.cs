using System;
using System.Collections.Generic;
using System.Linq;
using System.Timers;
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
        
        public MeasurementBar()
        {
            InitializeComponent();
        }
        
        /// <summary>
        /// Initialize with reference to DataStreamBar to access data streams
        /// </summary>
        public void Initialize(DataStreamBar dataStreamBar)
        {
            _dataStreamBar = dataStreamBar;
        }

        private void Button_Cursor_Click(object sender, RoutedEventArgs e)
        {
            // Add cursor functionality here - placeholder for future implementation
            // This could show/hide plot cursors or create cursor-related measurements
        }

        private void Button_Measure_Click(object sender, RoutedEventArgs e)
        {
            // Create RMS measurement for Channel 1 as requested
            AddMeasurement(MeasurementType.Rms, 0); // Channel 0 (which displays as Channel 1)
        }
        
        /// <summary>
        /// Add a measurement for a specific channel and measurement type
        /// </summary>
        /// <param name="measurementType">Type of measurement to create</param>
        /// <param name="channelIndex">Zero-based channel index</param>
        private void AddMeasurement(MeasurementType measurementType, int channelIndex)
        {
            // Check if we have any connected streams
            if (_dataStreamBar == null || _dataStreamBar.ConnectedDataStreams.Count == 0)
            {
                MessageBox.Show("No data streams are connected. Please add and connect a data stream first.", 
                              "No Data Available", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            // Verify the channel exists by trying to get some data
            if (!VerifyChannelExists(channelIndex))
            {
                MessageBox.Show($"Channel {channelIndex + 1} is not available. Please check your data streams.", 
                              "Channel Not Available", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            _measurementCounter++;
            
            // Create a simple buffer that will be refreshed each time
            double[] dataBuffer = new double[5000]; // Fixed size buffer
            
            // Create the measurement - since we can't change the constructor, 
            // we'll have to update the buffer regularly
            var measurement = new Measurement(measurementType, dataBuffer);
            
            // Start a separate timer to update the buffer with fresh data
            var dataUpdateTimer = new System.Timers.Timer(90); // Slightly faster than measurement timer
            dataUpdateTimer.Elapsed += (s, e) => UpdateDataBuffer(channelIndex, dataBuffer);
            dataUpdateTimer.Start();
            
            ActiveMeasurements.Add(measurement);
            
            // Create the UI representation
            var measurementBox = new MeasurementBox();
            measurementBox.DataContext = measurement; // Set the Measurement as DataContext
            
            // Subscribe to remove event
            measurementBox.OnRemoveClickedEvent += (s, args) => 
            {
                dataUpdateTimer?.Dispose(); // Clean up the data update timer
                RemoveMeasurement(measurement, measurementBox);
            };
            
            MeasurementBoxes.Add(measurementBox);
            Panel_MeasurementBoxes.Children.Add(measurementBox);
        }
        
        /// <summary>
        /// Verify that a channel exists and has data
        /// </summary>
        private bool VerifyChannelExists(int channelIndex)
        {
            int currentChannelOffset = 0;
            
            foreach (var dataStream in _dataStreamBar.ConnectedDataStreams)
            {
                if (channelIndex < currentChannelOffset + dataStream.ChannelCount)
                {
                    // Channel exists in this stream
                    return true;
                }
                currentChannelOffset += dataStream.ChannelCount;
            }
            
            return false; // Channel not found
        }
        
        /// <summary>
        /// Update the data buffer with fresh data from the specified channel
        /// </summary>
        private void UpdateDataBuffer(int channelIndex, double[] buffer)
        {
            int currentChannelOffset = 0;
            
            foreach (var dataStream in _dataStreamBar.ConnectedDataStreams)
            {
                if (channelIndex < currentChannelOffset + dataStream.ChannelCount)
                {
                    // This stream contains our target channel
                    int localChannelIndex = channelIndex - currentChannelOffset;
                    
                    // Update the buffer with latest data
                    try
                    {
                        dataStream.CopyLatestTo(localChannelIndex, buffer, buffer.Length);
                    }
                    catch
                    {
                        // Ignore errors - measurement will just use stale data
                    }
                    return;
                }
                currentChannelOffset += dataStream.ChannelCount;
            }
        }
        
        /// <summary>
        /// Remove a measurement and its UI representation
        /// </summary>
        private void RemoveMeasurement(Measurement measurement, MeasurementBox measurementBox)
        {
            // Dispose the measurement (stops timer)
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
            // Dispose all measurements
            foreach (var measurement in ActiveMeasurements)
            {
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
