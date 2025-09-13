using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SerialPlotDN_WPF.Model;

namespace SerialPlotDN_WPF.View.UserControls
{
    /// <summary>
    /// Interaction logic for MeasurementBar.xaml
    /// Now uses simplified DataStreamBar channel management
    /// </summary>
    public partial class MeasurementBar : UserControl, IDisposable
    {
        /// <summary>
        /// Collection of Measurements that drives the UI via DataTemplate
        /// </summary>
        public ObservableCollection<Measurement> Measurements { get; private set; } = new ObservableCollection<Measurement>();

        // Dependencies - simplified with direct channel management
        private DataStreamBar _dataStreamBar;
        
        // Self-managed timer for measurement updates
        private readonly DispatcherTimer _measurementTimer;
        private bool _disposed = false;

        /// <summary>
        /// Whether measurement updates are currently running
        /// </summary>
        public bool IsRunning { get; private set; }

        public DataStreamBar DataStreamBar
        {
            get 
            { 
                return _dataStreamBar; 
            }
            set 
            { 
                if (value == null)
                    throw new ArgumentNullException(nameof(value));
                _dataStreamBar = value;
            }
        }

        public MeasurementBar()
        {
            InitializeComponent();
            MeasurementItemsControl.ItemsSource = Measurements;
            
            // Initialize measurement update timer
            _measurementTimer = new DispatcherTimer(DispatcherPriority.Background);
            _measurementTimer.Interval = TimeSpan.FromMilliseconds(90); // ~11 FPS for measurements
            _measurementTimer.Tick += MeasurementTimer_Tick;
        }

        private void MeasurementTimer_Tick(object sender, EventArgs e)
        {
            // Update measurements on background thread for CPU-intensive calculations
            Task.Run(() =>
            {
                Measurement[] measurementsToUpdate = Measurements.ToArray(); // Copy to avoid collection modification
                Parallel.ForEach(measurementsToUpdate, measurement =>
                {
                    if (!measurement.IsDisposed)
                    {
                        measurement.UpdateMeasurement();
                    }
                });
            });
        }

        /// <summary>
        /// Start measurement updates
        /// </summary>
        public void StartUpdates()
        {
            if (!IsRunning && !_disposed)
            {
                _measurementTimer.Start();
                IsRunning = true;
            }
        }

        /// <summary>
        /// Stop measurement updates
        /// </summary>
        public void StopUpdates()
        {
            if (IsRunning)
            {
                _measurementTimer.Stop();
                IsRunning = false;
            }
        }

        private void Button_Cursor_Click(object sender, RoutedEventArgs e)
        {
            // Add cursor functionality here - placeholder for future implementation
        }

        /// <summary>
        /// Add a measurement for a specific channel and measurement type
        /// Uses the simplified channel-centric approach
        /// </summary>
        /// <param name="measurementType">Type of measurement to create</param>
        /// <param name="channelIndex">Zero-based channel index</param>
        public void AddMeasurement(MeasurementType measurementType, int channelIndex)
        {
            // Validation - much simpler with direct channel management
            if (_dataStreamBar == null)
            {
                MessageBox.Show("DataStreamBar is not initialized.", "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (_dataStreamBar.TotalChannelCount == 0)
            {
                MessageBox.Show("No channels are available.", "No Data Available", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Get the channel directly - no complex resolution needed!
            Channel channel = _dataStreamBar.GetChannelByIndex(channelIndex);
            if (channel == null)
            {
                MessageBox.Show($"Channel {channelIndex + 1} is not available.", "Channel Not Available", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!channel.IsStreamConnected)
            {
                MessageBox.Show($"Channel {channelIndex + 1} stream is not connected.", "Stream Not Connected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Create measurement using the channel - much cleaner!
            Measurement measurement = new Measurement(measurementType, channel.OwnerStream, channel.LocalChannelIndex, channel.Settings);

            // Subscribe to removal request
            measurement.RemoveRequested += OnMeasurementRemoveRequested;

            // Add to collection - UI updates automatically!
            Measurements.Add(measurement);
        }

        /// <summary>
        /// Add a measurement for a specific channel object
        /// This is the preferred method with the new architecture
        /// </summary>
        /// <param name="measurementType">Type of measurement to create</param>
        /// <param name="channel">The channel to create a measurement for</param>
        public void AddMeasurementForChannel(MeasurementType measurementType, Channel channel)
        {
            if (channel == null)
            {
                MessageBox.Show("Invalid channel.", "Channel Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!channel.IsStreamConnected)
            {
                MessageBox.Show($"Channel '{channel.Label}' stream is not connected.", "Stream Not Connected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Create measurement using the channel
            Measurement measurement = new Measurement(measurementType, channel.OwnerStream, channel.LocalChannelIndex, channel.Settings);

            // Subscribe to removal request
            measurement.RemoveRequested += OnMeasurementRemoveRequested;

            // Add to collection - UI updates automatically!
            Measurements.Add(measurement);
        }

        private void OnMeasurementRemoveRequested(object sender, EventArgs e)
        {
            if (sender is Measurement measurement)
            {
                measurement.RemoveRequested -= OnMeasurementRemoveRequested;
                measurement.Dispose();
                Measurements.Remove(measurement);
            }
        }

        /// <summary>
        /// Clear all measurements
        /// </summary>
        public void ClearAllMeasurements()
        {
            // Dispose all measurements
            foreach (Measurement measurement in Measurements)
            {
                measurement.Dispose();
            }
            
            Measurements.Clear();
        }

        /// <summary>
        /// Dispose of all resources
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                StopUpdates();
                // DispatcherTimer doesn't have Dispose method, just stop it
                ClearAllMeasurements();
            }
        }
    }
}
