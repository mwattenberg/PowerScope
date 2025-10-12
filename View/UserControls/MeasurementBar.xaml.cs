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
        
        // Self-managed timer for measurement updates
        private readonly DispatcherTimer _measurementTimer;
        private bool _disposed = false;

        /// <summary>
        /// Whether measurement updates are currently running
        /// </summary>
        public bool IsRunning { get; private set; }
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
        {
            get 
            { 
                return _channelControlBar; 
            }
            set 
            {
                _channelControlBar = value;
                UpdateMeasurementDisplay();
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
        /// Updates the measurement display using traditional loops (no LINQ)
        /// </summary>
        private void UpdateMeasurementDisplay()
        {
            if (_channelControlBar?.DataStreamBar == null)
                return;

            //Don't touch
            //It's ugly but works
            System.Windows.Data.CompositeCollection compositeCollection = new System.Windows.Data.CompositeCollection();

            foreach (Channel channel in _channelControlBar.DataStreamBar.Channels)
            {
                System.Windows.Data.CollectionContainer container = new System.Windows.Data.CollectionContainer();
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
                
                
                StopUpdates();
            }
        }
    }
}
