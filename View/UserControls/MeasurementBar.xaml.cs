using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SerialPlotDN_WPF.Model;
using System.Collections.Generic;

namespace SerialPlotDN_WPF.View.UserControls
{
    /// <summary>
    /// Interaction logic for MeasurementBar.xaml
    /// Gets channels from ChannelControlBar and displays all their measurements
    /// Much simpler - no XAML flattening needed!
    /// </summary>
    public partial class MeasurementBar : UserControl, IDisposable
    {
        /// <summary>
        /// Flattened collection of all measurements from all channels for UI binding
        /// </summary>
        public ObservableCollection<Measurement> AllMeasurements { get; private set; } = new ObservableCollection<Measurement>();

        // Dependencies
        private ChannelControlBar _channelControlBar;
        
        // Self-managed timer for measurement updates
        private readonly DispatcherTimer _measurementTimer;
        private bool _disposed = false;

        /// <summary>
        /// Whether measurement updates are currently running
        /// </summary>
        public bool IsRunning { get; private set; }

        public ChannelControlBar ChannelControlBar
        {
            get 
            { 
                return _channelControlBar; 
            }
            set 
            { 
                if (value == null)
                    throw new ArgumentNullException(nameof(value));
                _channelControlBar = value;
                RefreshAllMeasurements();
            }
        }

        public MeasurementBar()
        {
            InitializeComponent();
            
            // Bind UI to the flattened measurements collection
            MeasurementItemsControl.ItemsSource = AllMeasurements;
            
            // Initialize measurement update timer
            _measurementTimer = new DispatcherTimer(DispatcherPriority.Background);
            _measurementTimer.Interval = TimeSpan.FromMilliseconds(90); // ~11 FPS for measurements
            _measurementTimer.Tick += MeasurementTimer_Tick;
        }

        private void MeasurementTimer_Tick(object sender, EventArgs e)
        {
            // Update all measurements and refresh the flattened collection
            UpdateAllChannelMeasurements();
            RefreshAllMeasurements();
        }

        /// <summary>
        /// Updates all measurements across all channels
        /// Simple iteration through channels from ChannelControlBar
        /// </summary>
        private void UpdateAllChannelMeasurements()
        {
            if (_channelControlBar?.DataStreamBar == null)
                return;

            // Update measurements on background thread for CPU-intensive calculations
            Task.Run(() =>
            {
                // Get all channels from the ChannelControlBar's DataStreamBar
                Channel[] channels = _channelControlBar.DataStreamBar.Channels.ToArray();
                
                // Update all measurements in all channels
                Parallel.ForEach(channels, channel =>
                {
                    channel.UpdateAllMeasurements();
                });
            });
        }

        /// <summary>
        /// Refreshes the flattened measurements collection for UI binding
        /// Called when channels change or measurements are added/removed
        /// </summary>
        private void RefreshAllMeasurements()
        {
            if (_channelControlBar?.DataStreamBar == null)
                return;

            // Clear the flattened collection
            AllMeasurements.Clear();

            // Add all measurements from all channels
            foreach (Channel channel in _channelControlBar.DataStreamBar.Channels)
            {
                foreach (Measurement measurement in channel.Measurements)
                {
                    AllMeasurements.Add(measurement);
                }
            }
        }

        /// <summary>
        /// Public method to refresh measurements when channels change
        /// Called by external components when measurements are added/removed
        /// </summary>
        public void RefreshMeasurements()
        {
            RefreshAllMeasurements();
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
        /// Dispose of all resources
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                StopUpdates();
                // Measurements are owned by channels, so they'll be disposed when channels are disposed
            }
        }
    }
}
