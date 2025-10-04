using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Input;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using PowerScope.Model;
using ScottPlot.Plottables;

namespace PowerScope.View.UserControls
{
    /// <summary>
    /// Interaction logic for MeasurementBar.xaml
    /// Simplified to access channels and measurements directly - no redundant collections!
    /// Now uses MVVM pattern for cursor channel display
    /// </summary>
    public partial class MeasurementBar : UserControl, IDisposable
    {
        //ChannelControlBar gives us access to the channel settings
        //We store the request for a measurement in the ChannelSettings
        //Maybe weird but okay for now
        private ChannelControlBar _channelControlBar;
        
        // Self-managed timer for measurement updates
        private readonly DispatcherTimer _measurementTimer;
        private bool _disposed = false;

        // View models for cursor channels - proper MVVM approach
        private readonly ObservableCollection<CursorChannelModel> _cursorChannelModels;

        /// <summary>
        /// Whether measurement updates are currently running
        /// </summary>
        public bool IsRunning { get; private set; }

        // PlotManager dependency for accessing displayed plot data and plot control
        private PlotManager _plotManager;
        public PlotManager PlotManager
        {
            get { return _plotManager; }
            set 
            { 
                if (_plotManager != null)
                {
                    _plotManager.Plot.MouseDown -= Plot_MouseDown;
                    _plotManager.Plot.MouseUp -= Plot_MouseUp;
                    _plotManager.Plot.MouseMove -= Plot_MouseMove;
                }

                _plotManager = value; 

                if (_plotManager != null)
                {
                    _plotManager.Plot.MouseDown += Plot_MouseDown;
                    _plotManager.Plot.MouseUp += Plot_MouseUp;
                    _plotManager.Plot.MouseMove += Plot_MouseMove;
                }
            }
        }

        // Two horizontal draggable lines (toggled by CursorHorizontal button)
        private HorizontalLine _horizontalLineA;
        private HorizontalLine _horizontalLineB;

        // Two vertical draggable lines (toggled by CursorVertical button)
        private VerticalLine _verticalLineA;
        private VerticalLine _verticalLineB;

        // Currently dragged AxisLine
        private AxisLine _plottableBeingDragged;

        public ChannelControlBar ChannelControlBar
        {
            get 
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
                UpdateCursorChannelViewModels();
            }
        }

        public MeasurementBar()
        {
            InitializeComponent();

            // Initialize view model collection for cursor channels
            _cursorChannelModels = new ObservableCollection<CursorChannelModel>();
            
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
            // Update cursor view models when channels change
            UpdateCursorChannelViewModels();
            
            // Update cursor display when channels are added or removed
            if (_verticalLineA != null && _verticalLineB != null)
            {
                UpdateVerticalCursorDisplay();
            }
        }

        /// <summary>
        /// Updates the cursor channel view models collection based on enabled channels
        /// /// </summary>
        private void UpdateCursorChannelViewModels()
        {
            // Clear existing view models
            foreach (CursorChannelModel viewModel in _cursorChannelModels)
            {
                viewModel.Dispose();
            }
            _cursorChannelModels.Clear();

            if (_channelControlBar?.DataStreamBar == null)
                return;

            // Create view models for enabled channels
            var enabledChannels = _channelControlBar.DataStreamBar.Channels.Where(c => c.Settings.IsEnabled);
            foreach (Channel channel in enabledChannels)
            {
                CursorChannelModel viewModel = new CursorChannelModel(channel);
                _cursorChannelModels.Add(viewModel);
            }

            // If vertical cursors are active, update the ItemsControl binding
            if (_verticalLineA != null && _verticalLineB != null && CursorDisplayPanel.Visibility == Visibility.Visible)
            {
                CursorChannelItemsControl.ItemsSource = _cursorChannelModels;
            }
        }

        private void Plot_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_plotManager?.Plot == null) 
                return;

            Point pos = e.GetPosition(_plotManager.Plot);
            AxisLine line = GetLineUnderMouse((float)pos.X, (float)pos.Y);
            if (line != null)
            {
                _plottableBeingDragged = line;
                _plotManager.Plot.UserInputProcessor.Disable();
                e.Handled = true;
            }
        }

        private void Plot_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_plotManager?.Plot == null) 
                return;

            _plottableBeingDragged = null;
            _plotManager.Plot.UserInputProcessor.Enable();
            _plotManager.Plot.Refresh();
            Mouse.OverrideCursor = null;
        }

        private void Plot_MouseMove(object sender, MouseEventArgs e)
        {
            if (_plotManager?.Plot == null) 
                return;

            Point pos = e.GetPosition(_plotManager.Plot);
            var rect = _plotManager.Plot.Plot.GetCoordinateRect((float)pos.X, (float)pos.Y, radius: 10);

            if (_plottableBeingDragged == null)
            {
                AxisLine lineUnderMouse = GetLineUnderMouse((float)pos.X, (float)pos.Y);
                if (lineUnderMouse == null)
                    Mouse.OverrideCursor = null;
                else if (lineUnderMouse.IsDraggable && lineUnderMouse is VerticalLine)
                    Mouse.OverrideCursor = Cursors.SizeWE;
                else if (lineUnderMouse.IsDraggable && lineUnderMouse is HorizontalLine)
                    Mouse.OverrideCursor = Cursors.SizeNS;
            }
            else
            {
                if (_plottableBeingDragged is HorizontalLine horizontalLine)
                {
                    horizontalLine.Y = rect.VerticalCenter;
                    horizontalLine.Text = $"{horizontalLine.Y:0.0}";
                    
                    // Update cursor display when horizontal lines are moved
                    UpdateHorizontalCursorDisplay();
                }
                else if (_plottableBeingDragged is VerticalLine verticalLine)
                {
                    verticalLine.X = rect.HorizontalCenter;
                    verticalLine.Text = $"{verticalLine.X:0}";
                    
                    // Update cursor display when vertical lines are moved
                    UpdateVerticalCursorDisplay();
                }
                _plotManager.Plot.Refresh();
                e.Handled = true;
            }
        }

        private AxisLine GetLineUnderMouse(float x, float y)
        {
            var rect = _plotManager.Plot.Plot.GetCoordinateRect(x, y, radius: 10);
            foreach (AxisLine axLine in _plotManager.Plot.Plot.GetPlottables<AxisLine>().Reverse())
            {
                if (axLine.IsUnderMouse(rect))
                    return axLine;
            }
            return null;
        }

        /// <summary>
        /// Gets the current sample rate from the first available channel
        /// /// </summary>
        /// <returns>Sample rate in Hz, or 0 if no channels available</returns>
        private double GetCurrentSampleRate()
        {
            if (_channelControlBar?.DataStreamBar == null)
                return 0;

            Channel firstChannel = _channelControlBar.DataStreamBar.Channels.FirstOrDefault();
            if (firstChannel?.OwnerStream == null)
                return 0;

            return firstChannel.OwnerStream.SampleRate;
        }

        /// <summary>
        /// Updates the cursor display with current vertical cursor positions
        /// /// </summary>
        private void UpdateVerticalCursorDisplay()
        {
            if (_verticalLineA == null || _verticalLineB == null)
                return;

            // Get cursor positions (X values represent sample positions in time)
            double cursorASample = _verticalLineA.X;
            double cursorBSample = _verticalLineB.X;

            double sampleRate = GetCurrentSampleRate();
            
            if (sampleRate > 0)
            {
                // Update with valid sample rate
                CursorVerticalControl.UpdateCursorData(cursorASample, cursorBSample, sampleRate);
            }
            else
            {
                // Update only sample positions when no sample rate available
                CursorVerticalControl.UpdateCursorSamplePositions(cursorASample, cursorBSample);
            }

            // Update channel-specific cursor values using view models
            UpdateVerticalCursorChannelValues();
        }

        /// <summary>
        /// Updates the cursor display with current horizontal cursor positions
        /// </summary>
        private void UpdateHorizontalCursorDisplay()
        {
            if (_horizontalLineA == null || _horizontalLineB == null)
                return;

            // Get cursor Y positions
            double cursorAYValue = _horizontalLineA.Y;
            double cursorBYValue = _horizontalLineB.Y;

            // Update horizontal cursor display
            CursorHorizontalControl.UpdateCursorData(cursorAYValue, cursorBYValue);
        }

        /// <summary>
        /// Updates channel-specific cursor values using view models (proper MVVM approach)
        /// No more visual tree traversal needed!
        /// </summary>
        private void UpdateVerticalCursorChannelValues()
        {
            if (_verticalLineA == null || _verticalLineB == null)
                return;

            double cursorASample = _verticalLineA.X;
            double cursorBSample = _verticalLineB.X;
            
            // Convert to integer sample indices
            int sampleIndexA = (int)Math.Round(cursorASample);
            int sampleIndexB = (int)Math.Round(cursorBSample);

            // Update each view model - UI updates automatically via data binding!
            foreach (CursorChannelModel viewModel in _cursorChannelModels)
            {
                double? valueA = GetChannelValueAtSample(viewModel.Channel, sampleIndexA);
                double? valueB = GetChannelValueAtSample(viewModel.Channel, sampleIndexB);
                
                viewModel.UpdateCursorValues(valueA, valueB);
            }
        }

        /// <summary>
        /// Gets the signal value for a specific channel at a given sample index
        /// This now gets data from the plot (what's actually displayed) rather than directly from the channel data stream
        /// </summary>
        /// <param name="channel">The channel to get the value from</param>
        /// <param name="sampleIndex">The sample index (0 = leftmost sample on plot)</param>
        /// <returns>The channel value at that sample, or null if not available</returns>
        private double? GetChannelValueAtSample(Channel channel, int sampleIndex)
        {
            if (channel == null || _plotManager == null)
                return null;
            
            // Get data from the plot manager (what's actually displayed)
            return _plotManager.GetPlotDataAt(channel, sampleIndex);
        }

        /// <summary>
        /// Updates all measurements across all channels AND cursor channel values
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

            // Update cursor channel values on UI thread (if vertical cursors are active)
            // This ensures cursor values are updated at the same rate as measurements
            if (_verticalLineA != null && _verticalLineB != null && CursorDisplayPanel.Visibility == Visibility.Visible)
            {
                UpdateVerticalCursorChannelValues();
            }
        }

        /// <summary>
        /// Updates the measurement display using direct channel access
        /// /// </summary>
        private void UpdateMeasurementDisplay()
        {
            if (_channelControlBar?.DataStreamBar == null)
                return;

            // Use CompositeCollection to directly bind to channel measurements without flattening
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
            // Select horizontal cursor: set horizontal button to selected color and vertical to default
            Brush defaultBrush = (Brush)Application.Current.Resources["PlotSettings_TextBoxBackgroundBrush"];
            //var selectedBrush = Brushes.DarkOrange;
            Brush selectedBrush = Brushes.DimGray;

            // Toggle two draggable horizontal axis lines on the plot
            if (_plotManager?.Plot == null)
                throw new InvalidOperationException("PlotManager dependency not set on MeasurementBar.");

            // If vertical lines are present, remove them to enforce mutual exclusivity
            if (_verticalLineA != null || _verticalLineB != null)
            {
                if (_verticalLineA != null)
                {
                    _plotManager.Plot.Plot.Remove(_verticalLineA);
                    _verticalLineA = null;
                }
                if (_verticalLineB != null)
                {
                    _plotManager.Plot.Plot.Remove(_verticalLineB);
                    _verticalLineB = null;
                }
            }

            if (_horizontalLineA == null && _horizontalLineB == null)
            {
                // place lines at 25% and 75% of current y-axis span
                var limits = _plotManager.Plot.Plot.Axes.GetYAxes().First().Range;
                double y1 = limits.Min + (limits.Max - limits.Min) * 0.25;
                double y2 = limits.Min + (limits.Max - limits.Min) * 0.75;

                // Get the highlight colors from App.xaml resources
                var highlightNormal = (Color)Application.Current.Resources["Highlight_Normal"];
                var highlightComplementary = (Color)Application.Current.Resources["Highlight_Complementary"];

                _horizontalLineA = _plotManager.Plot.Plot.Add.HorizontalLine(y1);
                _horizontalLineA.IsDraggable = true;
                _horizontalLineA.Text = "A";
                _horizontalLineA.Color = new ScottPlot.Color(highlightNormal.R, highlightNormal.G, highlightNormal.B);

                _horizontalLineB = _plotManager.Plot.Plot.Add.HorizontalLine(y2);
                _horizontalLineB.IsDraggable = true;
                _horizontalLineB.Text = "B";
                _horizontalLineB.Color = new ScottPlot.Color(highlightComplementary.R, highlightComplementary.G, highlightComplementary.B);

                // set button state to selected when lines are present
                Button_CursorHorizontal.Background = selectedBrush;
                Button_CursorVertical.Background = defaultBrush;

                ShowHorizontalCursorDisplay();
                
                // Initial update of horizontal cursor display when lines are created
                UpdateHorizontalCursorDisplay();
                
                _plotManager.Plot.Refresh();
            }
            else
            {
                // remove existing lines
                if (_horizontalLineA != null)
                {
                    _plotManager.Plot.Plot.Remove(_horizontalLineA);
                    _horizontalLineA = null;
                }
                if (_horizontalLineB != null)
                {
                    _plotManager.Plot.Plot.Remove(_horizontalLineB);
                    _horizontalLineB = null;
                }

                // revert button state to default when lines are removed
                Button_CursorHorizontal.Background = defaultBrush;
                Button_CursorVertical.Background = defaultBrush;

                HideCursorDisplay();
                _plotManager.Plot.Refresh();
            }
        }

        private void Button_CursorVertical_Click(object sender, RoutedEventArgs e)
        {
            // Select vertical cursor: set vertical button to selected color and horizontal to default
            Brush defaultBrush = (Brush)Application.Current.Resources["PlotSettings_TextBoxBackgroundBrush"];
            Brush selectedBrush = Brushes.DimGray;

            // Toggle two draggable vertical axis lines on the plot
            if (_plotManager?.Plot == null)
                throw new InvalidOperationException("PlotManager dependency not set on MeasurementBar.");

            // If horizontal lines are present, remove them to enforce mutual exclusivity
            if (_horizontalLineA != null || _horizontalLineB != null)
            {
                if (_horizontalLineA != null)
                {
                    _plotManager.Plot.Plot.Remove(_horizontalLineA);
                    _horizontalLineA = null;
                }
                if (_horizontalLineB != null)
                {
                    _plotManager.Plot.Plot.Remove(_horizontalLineB);
                    _horizontalLineB = null;
                }
            }

            if (_verticalLineA == null && _verticalLineB == null)
            {
                // place lines at 25% and 75% of current x-axis span
                var xAxisRange = _plotManager.Plot.Plot.Axes.GetXAxes().First().Range;
                double x1 = xAxisRange.Min + (xAxisRange.Max - xAxisRange.Min) * 0.25;
                double x2 = xAxisRange.Min + (xAxisRange.Max - xAxisRange.Min) * 0.75;

                // Get the highlight colors from App.xaml resources
                var highlightNormal = (Color)Application.Current.Resources["Highlight_Normal"];
                var highlightComplementary = (Color)Application.Current.Resources["Highlight_Complementary"];

                _verticalLineA = _plotManager.Plot.Plot.Add.VerticalLine(x1);
                _verticalLineA.IsDraggable = true;
                _verticalLineA.Text = "A";
                _verticalLineA.Color = new ScottPlot.Color(highlightNormal.R, highlightNormal.G, highlightNormal.B);

                _verticalLineB = _plotManager.Plot.Plot.Add.VerticalLine(x2);
                _verticalLineB.IsDraggable = true;
                _verticalLineB.Text = "B";
                _verticalLineB.Color = new ScottPlot.Color(highlightComplementary.R, highlightComplementary.G, highlightComplementary.B);
                

                Button_CursorVertical.Background = selectedBrush;
                Button_CursorHorizontal.Background = defaultBrush;

                ShowVerticalCursorDisplay();
                
                // Initial update of cursor display when lines are created
                UpdateVerticalCursorDisplay();
                
                _plotManager.Plot.Refresh();
            }
            else
            {
                if (_verticalLineA != null)
                {
                    _plotManager.Plot.Plot.Remove(_verticalLineA);
                    _verticalLineA = null;
                }
                if (_verticalLineB != null)
                {
                    _plotManager.Plot.Plot.Remove(_verticalLineB);
                    _verticalLineB = null;
                }

                Button_CursorVertical.Background = defaultBrush;
                Button_CursorHorizontal.Background = defaultBrush;

                HideCursorDisplay();
                _plotManager.Plot.Refresh();
            }
        }

        private void ShowVerticalCursorDisplay()
        {
            // Show the cursor display panel and vertical cursor control
            CursorDisplayPanel.Visibility = Visibility.Visible;
            CursorVerticalControl.Visibility = Visibility.Visible;
            CursorHorizontalControl.Visibility = Visibility.Collapsed;
            
            if (_channelControlBar?.DataStreamBar == null)
            {
                // No DataStreamBar available - show only cursor control with no channel-specific data
                CursorChannelItemsControl.ItemsSource = null;
                return;
            }

            // Ensure view models are up to date
            UpdateCursorChannelViewModels();
            
            // Use Dispatcher to ensure UI binding happens on the correct thread after layout update
            Dispatcher.BeginInvoke(new Action(() =>
            {
                // Bind to view models instead of channels directly
                CursorChannelItemsControl.ItemsSource = _cursorChannelModels;
                
                // Immediately update cursor channel values if cursors are positioned
                if (_verticalLineA != null && _verticalLineB != null)
                {
                    UpdateVerticalCursorChannelValues();
                }
            }), DispatcherPriority.Render);
        }

        private void ShowHorizontalCursorDisplay()
        {
            // Show the cursor display panel and horizontal cursor control
            CursorDisplayPanel.Visibility = Visibility.Visible;
            CursorVerticalControl.Visibility = Visibility.Collapsed;
            CursorHorizontalControl.Visibility = Visibility.Visible;
            
            // Don't show channel-specific cursor controls for horizontal cursors
            // Horizontal cursors only show Y-axis delta, not per-channel measurements
            CursorChannelItemsControl.ItemsSource = null;
        }

        private void HideCursorDisplay()
        {
            CursorDisplayPanel.Visibility = Visibility.Collapsed;
            CursorVerticalControl.Visibility = Visibility.Collapsed;
            CursorHorizontalControl.Visibility = Visibility.Collapsed;
            CursorChannelItemsControl.ItemsSource = null;
        }

        /// <summary>
        /// Dispose of all resources
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                
                // Dispose view models
                foreach (CursorChannelModel viewModel in _cursorChannelModels)
                {
                    viewModel.Dispose();
                }
                _cursorChannelModels.Clear();
                
                // Unsubscribe from collection changes
                if (_channelControlBar?.DataStreamBar != null)
                {
                    _channelControlBar.DataStreamBar.Channels.CollectionChanged -= OnChannelsCollectionChanged;
                }
                
                // Unsubscribe from PlotManager events if set
                if (_plotManager != null)
                {
                    _plotManager.Plot.MouseDown -= Plot_MouseDown;
                    _plotManager.Plot.MouseUp -= Plot_MouseUp;
                    _plotManager.Plot.MouseMove -= Plot_MouseMove;
                }
                
                StopUpdates();
                // Measurements are owned by channels, so they'll be disposed when channels are disposed
            }
        }
    }
}
