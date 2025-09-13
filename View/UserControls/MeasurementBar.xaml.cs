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
    /// Now manages its own measurement updates with DispatcherTimer
    /// </summary>
    public partial class MeasurementBar : UserControl, IDisposable
    {
        /// <summary>
        /// Collection of Measurements that drives the UI via DataTemplate
        /// </summary>
        public ObservableCollection<Measurement> Measurements { get; private set; } = new ObservableCollection<Measurement>();

        // Dependencies
        private DataStreamBar _dataStreamBar;
        private ObservableCollection<ChannelSettings> _channelSettings;
        
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

        public ObservableCollection<ChannelSettings> ChannelSettings
        {
            get 
            { 
                return _channelSettings; 
            }
            set 
            { 
                if (value == null)
                    throw new ArgumentNullException(nameof(value));
                _channelSettings = value;
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
        /// </summary>
        /// <param name="measurementType">Type of measurement to create</param>
        /// <param name="channelIndex">Zero-based channel index</param>
        public void AddMeasurement(MeasurementType measurementType, int channelIndex)
        {
            // Validation logic
            if (_dataStreamBar == null || _dataStreamBar.ConnectedDataStreams.Count == 0)
            {
                MessageBox.Show("No data streams are connected.", "No Data Available", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_channelSettings == null || _channelSettings.Count <= channelIndex)
            {
                MessageBox.Show($"Channel {channelIndex + 1} settings are not available.", "Channel Settings Not Available", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Use DataStreamBar's encapsulated channel resolution logic
            (IDataStream dataStream, int localChannelIndex) = _dataStreamBar.ResolveChannelToStream(channelIndex);
            if (dataStream == null)
            {
                MessageBox.Show($"Channel {channelIndex + 1} is not available.", "Channel Not Available", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Create measurement with ChannelSettings reference
            ChannelSettings channelSetting = _channelSettings[channelIndex];
            Measurement measurement = new Measurement(measurementType, dataStream, localChannelIndex, channelSetting);

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
