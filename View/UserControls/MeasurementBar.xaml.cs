using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PowerScope.Model;

namespace PowerScope.View.UserControls
{
    /// <summary>
    /// Interaction logic for MeasurementBar.xaml
    /// Simplified to access channels and measurements directly - no redundant collections!
    /// </summary>
    public partial class MeasurementBar : UserControl, IDisposable
    {
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
                UpdateMeasurementDisplay();
            }
        }

        public MeasurementBar()
        {
            InitializeComponent();
            
            // Initialize measurement update timer
            _measurementTimer = new DispatcherTimer(DispatcherPriority.Background);
            _measurementTimer.Interval = TimeSpan.FromMilliseconds(200);
            _measurementTimer.Tick += UpdateAllChannelMeasurements;
        }

        /// <summary>
        /// Updates all measurements across all channels
        /// Channels manage their own measurement collections automatically
        /// </summary>
        private void UpdateAllChannelMeasurements(object? sender, EventArgs e)
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
        /// Updates the measurement display using direct channel access
        /// </summary>
        private void UpdateMeasurementDisplay()
        {
            if (_channelControlBar?.DataStreamBar == null)
                return;

            // Use CompositeCollection to directly bind to channel measurements without flattening
            var compositeCollection = new System.Windows.Data.CompositeCollection();
            
            foreach (Channel channel in _channelControlBar.DataStreamBar.Channels)
            {
                var container = new System.Windows.Data.CollectionContainer();
                container.Collection = channel.Measurements;
                compositeCollection.Add(container);
            }
            
            MeasurementItemsControl.ItemsSource = compositeCollection;
        }

        /// <summary>
        /// Public method to refresh measurement display when channels change
        /// Called by external components when channels are added/removed
        /// </summary>
        public void RefreshMeasurements()
        {
            UpdateMeasurementDisplay();
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
