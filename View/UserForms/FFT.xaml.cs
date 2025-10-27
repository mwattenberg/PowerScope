using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace PowerScope.View.UserForms
{
    /// <summary>
    /// Sort column enumeration for FFT peaks
    /// </summary>
    public enum SortColumn
    {
        Frequency,
        Amplitude,
        AmplitudeDb
    }

    /// <summary>
    /// Sort direction enumeration
    /// </summary>
    public enum SortDirection
    {
        Ascending,
        Descending
    }

    /// <summary>
    /// Interaction logic for FFT.xaml
    /// FFT Spectrum window for displaying frequency domain analysis
    /// </summary>
    public partial class FFT : Window
    {
        //maybe this is a bit hacky but it works
        private DispatcherTimer _autoScaleTimer;

        // Sorting state - synced from Model on load
        private SortColumn _currentSortColumn = SortColumn.Amplitude;
        private SortDirection _currentSortDirection = SortDirection.Descending;

        public FFT()
        {
            InitializeComponent();
            // Initialize FFT plot when the window is loaded
            Loaded += FFT_Loaded;
        }

        /// <summary>
        /// Handle window loaded event
        /// </summary>
        private void FFT_Loaded(object sender, RoutedEventArgs e)
        {
            InitializeFFTPlot();
            StartAutoScaleTimer();
            
            // Initialize sort state from the measurement model to sync with persisted preferences
            if (DataContext is PowerScope.Model.Measurement measurement)
            {
                var (column, direction) = measurement.GetFFTPeakSortState();
                _currentSortColumn = column;
                _currentSortDirection = direction;
            }
            
            // Initialize sort indicators to show current sort state
            UpdateSortIndicators();
        }

        /// <summary>
        /// Initialize the FFT plot with appropriate settings for spectrum display
        /// </summary>
        public void InitializeFFTPlot()
        {
            // Apply dark theme similar to main plot
            WpfPlotFFT.Plot.FigureBackground.Color = ScottPlot.Color.FromHex("#181818");
            WpfPlotFFT.Plot.DataBackground.Color = ScottPlot.Color.FromHex("#1f1f1f");
            WpfPlotFFT.Plot.Axes.Color(ScottPlot.Color.FromHex("#d7d7d7"));
            WpfPlotFFT.Plot.Grid.MajorLineColor = ScottPlot.Color.FromHex("#404040");
            WpfPlotFFT.Plot.Axes.Bottom.Label.Text = "Frequency (Hz)";
            WpfPlotFFT.Plot.Axes.Left.Label.Text = "Magnitude (dB)";
            WpfPlotFFT.Plot.Title("FFT Spectrum");
            
            // Disable auto scaling to prevent automatic axis adjustments
            WpfPlotFFT.Plot.Axes.ContinuouslyAutoscale = false;
            
            // Setup user input for zoom/pan
            SetupPlotUserInput();

            WpfPlotFFT.Refresh();
        }

        /// <summary>
        /// Start a timer to auto-scale the plot after initialization delay
        /// </summary>
        private void StartAutoScaleTimer()
        {
            _autoScaleTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _autoScaleTimer.Tick += AutoScaleTimer_Tick;
            _autoScaleTimer.Start();
        }

        /// <summary>
        /// Handle auto-scale timer tick - scales the plot and stops the timer
        /// </summary>
        private void AutoScaleTimer_Tick(object sender, EventArgs e)
        {
            // Stop the timer (one-time execution)
            _autoScaleTimer.Stop();
            _autoScaleTimer.Tick -= AutoScaleTimer_Tick;
            _autoScaleTimer = null;

            // Apply auto-scale after initialization delay
            WpfPlotFFT.Plot.Axes.AutoScale();
            WpfPlotFFT.Refresh();
        }

        /// <summary>
        /// Setup user input handlers for the FFT plot
        /// </summary>
        private void SetupPlotUserInput()
        {
            // Clear existing input handlers
            WpfPlotFFT.UserInputProcessor.UserActionResponses.Clear();
            WpfPlotFFT.UserInputProcessor.IsEnabled = true;
            
            // Left-click-drag pan
            var panButton = ScottPlot.Interactivity.StandardMouseButtons.Left;
            var panResponse = new ScottPlot.Interactivity.UserActionResponses.MouseDragPan(panButton);
            WpfPlotFFT.UserInputProcessor.UserActionResponses.Add(panResponse);

            // Middle-click-drag zoom rectangle
            var zoomRectangleButton = ScottPlot.Interactivity.StandardMouseButtons.Middle;
            var zoomRectangleResponse = new ScottPlot.Interactivity.UserActionResponses.MouseDragZoomRectangle(zoomRectangleButton);
            WpfPlotFFT.UserInputProcessor.UserActionResponses.Add(zoomRectangleResponse);

            // Right-click auto-scale
            var autoscaleButton = ScottPlot.Interactivity.StandardMouseButtons.Right;
            var autoscaleResponse = new ScottPlot.Interactivity.UserActionResponses.SingleClickAutoscale(autoscaleButton);
            WpfPlotFFT.UserInputProcessor.UserActionResponses.Add(autoscaleResponse);

            // Mouse wheel zoom with proper constructor parameters
            var wheelZoomResponse = new ScottPlot.Interactivity.UserActionResponses.MouseWheelZoom(
                ScottPlot.Interactivity.StandardKeys.Shift, 
                ScottPlot.Interactivity.StandardKeys.Control);
            WpfPlotFFT.UserInputProcessor.UserActionResponses.Add(wheelZoomResponse);
        }

        /// <summary>
        /// Handle close button click
        /// </summary>
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // Clean up timer if still running
            if (_autoScaleTimer != null)
            {
                _autoScaleTimer.Stop();
                _autoScaleTimer.Tick -= AutoScaleTimer_Tick;
                _autoScaleTimer = null;
            }
            Close();
        }

        /// <summary>
        /// Handle title bar drag for window movement
        /// </summary>
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        /// <summary>
        /// Handle frequency column header click for sorting
        /// </summary>
        private void FrequencyHeader_Click(object sender, RoutedEventArgs e)
        {
            HandleColumnHeaderClick(SortColumn.Frequency);
        }

        /// <summary>
        /// Handle amplitude column header click for sorting
        /// </summary>
        private void AmplitudeHeader_Click(object sender, RoutedEventArgs e)
        {
            HandleColumnHeaderClick(SortColumn.Amplitude);
        }

        /// <summary>
        /// Handle amplitude (dB) column header click for sorting
        /// </summary>
        private void AmplitudeDbHeader_Click(object sender, RoutedEventArgs e)
        {
            HandleColumnHeaderClick(SortColumn.AmplitudeDb);
        }

        /// <summary>
        /// Handle column header click - toggles sort direction or changes sort column
        /// </summary>
        private void HandleColumnHeaderClick(SortColumn column)
        {
            // If clicking the same column, toggle direction; otherwise, set new column with descending as default
            if (_currentSortColumn == column)
            {
                _currentSortDirection = _currentSortDirection == SortDirection.Ascending 
                    ? SortDirection.Descending 
                    : SortDirection.Ascending;
            }
            else
            {
                _currentSortColumn = column;
                _currentSortDirection = SortDirection.Descending; // Default to descending for new column
            }

            // Apply sorting to the data
            ApplySorting();

            // Update visual indicators
            UpdateSortIndicators();
        }

        /// <summary>
        /// Apply sorting to the FFT peaks data
        /// </summary>
        private void ApplySorting()
        {
            if (DataContext is PowerScope.Model.Measurement measurement)
            {
                measurement.SortFFTPeaks(_currentSortColumn, _currentSortDirection);
            }
        }

        /// <summary>
        /// Update visual sort indicators on column headers
        /// </summary>
        private void UpdateSortIndicators()
        {
            // Clear all indicators first
            FrequencySortIndicator.Text = "";
            AmplitudeSortIndicator.Text = "";
            AmplitudeDbSortIndicator.Text = "";

            // Set indicator for current sort column
            string indicator = _currentSortDirection == SortDirection.Ascending ? "▲" : "▼";

            switch (_currentSortColumn)
            {
                case SortColumn.Frequency:
                    FrequencySortIndicator.Text = indicator;
                    break;
                case SortColumn.Amplitude:
                    AmplitudeSortIndicator.Text = indicator;
                    break;
                case SortColumn.AmplitudeDb:
                    AmplitudeDbSortIndicator.Text = indicator;
                    break;
            }
        }
    }
}
