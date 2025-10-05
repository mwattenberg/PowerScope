using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using PowerScope.Model;

namespace PowerScope.View.UserControls
{
    /// <summary>
    /// Interaction logic for MeasurementBar.xaml
    /// Simplified to focus only on measurement display and cursor UI presentation
    /// PlotManager now owns cursor functionality - cleaner separation of concerns
    /// </summary>
    public partial class MeasurementBar : UserControl, IDisposable
    {
        private ChannelControlBar _channelControlBar;
        
        // Self-managed timer for measurement updates
        private readonly DispatcherTimer _measurementTimer;
        private bool _disposed = false;

        private readonly ObservableCollection<Measurement> _allMeasurements = new();

        /// <summary>
        /// Whether measurement updates are currently running
        /// </summary>
        public bool IsRunning { get; private set; }

        // PlotManager dependency - now for both plot access and cursor management
        private PlotManager _plotManager;
        public PlotManager PlotManager
        {
            get { return _plotManager; }
            set 
            { 
                _plotManager = value;
                
                // Set cursor model for UI controls to use PlotManager's cursor
                if (_plotManager != null)
                {
                    CursorVerticalControl.CursorModel = _plotManager.Cursor;
                    CursorHorizontalControl.CursorModel = _plotManager.Cursor;
                }
            }
        }

        public ChannelControlBar ChannelControlBar
        {            get 
            { 
                return _channelControlBar; 
            }
            set 
            { 
                // Unsubscribe from previous DataStreamBar if it exists
                if (_channelControlBar?.DataStreamBar != null)
                {
                    _channelControlBar.DataStreamBar.Channels.CollectionChanged -= OnChannelsCollectionChanged;
                }

                _channelControlBar = value;
                
                // Subscribe to new DataStreamBar collection changes
                if (_channelControlBar?.DataStreamBar != null)
                {
                    _channelControlBar.DataStreamBar.Channels.CollectionChanged += OnChannelsCollectionChanged;
                }

                UpdateMeasurementDisplay();
                
                // Update PlotManager with channels (it will handle cursor channel data automatically)
                if (_plotManager != null)
                {
                    _plotManager.SetChannels(_channelControlBar.DataStreamBar.Channels);
                }
            }
        }



        public MeasurementBar()
        {
            InitializeComponent();
            
            // Initialize measurement update timer
            _measurementTimer = new DispatcherTimer(DispatcherPriority.Background);
            _measurementTimer.Interval = TimeSpan.FromMilliseconds(125);
            _measurementTimer.Tick += UpdateAllChannelMeasurements;
        }

        /// <summary>
        /// Handles changes to the channels collection (add/remove)
        /// </summary>
        /// <param name="sender">The ObservableCollection sender</param>
        /// <param name="e">Collection change event args</param>
        private void OnChannelsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            // PlotManager handles cursor channel data updates automatically via SetChannels
            // No need for manual coordination here
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

            // Update cursor channel values on UI thread (if cursors are active)
            // PlotManager handles this automatically during cursor updates, but we can trigger it here too
            if (_plotManager?.HasActiveCursors == true && CursorDisplayPanel.Visibility == Visibility.Visible)
            {
                _plotManager.UpdateCursorValues(_plotManager.Cursor);
            }
        }

        /// <summary>
        /// Updates the measurement display using direct channel access
        /// </summary>
        private void UpdateMeasurementDisplay()
        {

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

        private void Button_CursorHorizontal_Click(object sender, RoutedEventArgs e)
        {
            if (_plotManager == null)
                throw new InvalidOperationException("PlotManager dependency not set on MeasurementBar.");

            // Get UI brush resources
            Brush defaultBrush = (Brush)Application.Current.Resources["PlotSettings_TextBoxBackgroundBrush"];
            Brush selectedBrush = Brushes.DimGray;

            if (_plotManager.ActiveCursorMode == CursorMode.Horizontal)
            {
                // Disable horizontal cursors
                _plotManager.DisableCursors();
                HideCursorUI();
                ResetButtonStates(defaultBrush);
            }
            else
            {
                // Enable horizontal cursors
                _plotManager.EnableHorizontalCursors();
                ShowHorizontalCursorUI();
                SetHorizontalButtonSelected(defaultBrush, selectedBrush);
            }
        }

        private void Button_CursorVertical_Click(object sender, RoutedEventArgs e)
        {
            if (_plotManager == null)
                throw new InvalidOperationException("PlotManager dependency not set on MeasurementBar.");

            // Get UI brush resources
            Brush defaultBrush = (Brush)Application.Current.Resources["PlotSettings_TextBoxBackgroundBrush"];
            Brush selectedBrush = Brushes.DimGray;

            if (_plotManager.ActiveCursorMode == CursorMode.Vertical)
            {
                // Disable vertical cursors
                _plotManager.DisableCursors();
                HideCursorUI();
                ResetButtonStates(defaultBrush);
            }
            else
            {
                // Enable vertical cursors
                _plotManager.EnableVerticalCursors();
                ShowVerticalCursorUI();
                SetVerticalButtonSelected(defaultBrush, selectedBrush);
            }
        }

        /// <summary>
        /// Shows the vertical cursor UI elements - simplified UI management only
        /// </summary>
        private void ShowVerticalCursorUI()
        {
            // Show the cursor display panel and vertical cursor control
            CursorDisplayPanel.Visibility = Visibility.Visible;
            CursorVerticalControl.Visibility = Visibility.Visible;
            CursorHorizontalControl.Visibility = Visibility.Collapsed;
            
            // Bind to PlotManager's cursor data - no manual updates needed
            CursorChannelItemsControl.ItemsSource = _plotManager.Cursor.ChannelData;
        }

        /// <summary>
        /// Shows the horizontal cursor UI elements - simplified UI management only
        /// </summary>
        private void ShowHorizontalCursorUI()
        {
            // Show the cursor display panel and horizontal cursor control
            CursorDisplayPanel.Visibility = Visibility.Visible;
            CursorVerticalControl.Visibility = Visibility.Collapsed;
            CursorHorizontalControl.Visibility = Visibility.Visible;
            
            // Don't show channel-specific cursor controls for horizontal cursors
            // Horizontal cursors only show Y-axis delta, not per-channel measurements
            CursorChannelItemsControl.ItemsSource = null;
        }

        /// <summary>
        /// Hides all cursor UI elements - simplified UI management only
        /// </summary>
        private void HideCursorUI()
        {
            CursorDisplayPanel.Visibility = Visibility.Collapsed;
            CursorVerticalControl.Visibility = Visibility.Collapsed;
            CursorHorizontalControl.Visibility = Visibility.Collapsed;
            CursorChannelItemsControl.ItemsSource = null;
        }

        /// <summary>
        /// Sets vertical button as selected and horizontal as default
        /// </summary>
        private void SetVerticalButtonSelected(Brush defaultBrush, Brush selectedBrush)
        {
            Button_CursorVertical.Background = selectedBrush;
            Button_CursorHorizontal.Background = defaultBrush;
        }

        /// <summary>
        /// Sets horizontal button as selected and vertical as default
        /// </summary>
        private void SetHorizontalButtonSelected(Brush defaultBrush, Brush selectedBrush)
        {
            Button_CursorHorizontal.Background = selectedBrush;
            Button_CursorVertical.Background = defaultBrush;
        }

        /// <summary>
        /// Resets both buttons to default state
        /// </summary>
        private void ResetButtonStates(Brush defaultBrush)
        {
            Button_CursorVertical.Background = defaultBrush;
            Button_CursorHorizontal.Background = defaultBrush;
        }

        /// <summary>
        /// Dispose of all resources
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                
                // No need to dispose cursor - PlotManager owns it
                
                // Unsubscribe from collection changes
                if (_channelControlBar?.DataStreamBar != null)
                {
                    _channelControlBar.DataStreamBar.Channels.CollectionChanged -= OnChannelsCollectionChanged;
                }
                
                StopUpdates();
            }
        }
    }
}
