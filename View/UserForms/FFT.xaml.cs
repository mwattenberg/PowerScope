using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using PowerScope.Model;

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

        // Cached FFT plot signal for efficient updates (PlotManager pattern)
        private ScottPlot.Plottables.SignalXY _spectrumSignal;
        private int _lastSpectrumLength = -1; // Track when we need to rebuild the signal

        private FFTAnalysis FFTAnalysis => (DataContext as Measurement)?.FFT;

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

            if (DataContext is Measurement measurement)
            {
                Title = "FFT Spectrum - " + measurement.ChannelSettings.Label;

                // Initialize sort state from the model to sync with persisted preferences
                var (column, direction) = measurement.FFT.GetSortState();
                _currentSortColumn = column;
                _currentSortDirection = direction;

                // Spectrum arrays are only computed while a window is open and listening
                measurement.FFT.ComputeSpectrum = true;
                measurement.FFT.SpectrumUpdated += FFTAnalysis_SpectrumUpdated;
            }

            // Initialize sort indicators to show current sort state
            UpdateSortIndicators();

            RefreshSpectrumPlot();
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

            FFTAnalysis fft = FFTAnalysis;
            if (fft != null)
            {
                WpfPlotFFT.Plot.Title("FFT Spectrum - " + fft.ChannelLabel + " (Fs = " + fft.SampleRate.ToString("F1") + " Hz)");

                double nyquistFrequency = fft.SampleRate / 2.0;
                WpfPlotFFT.Plot.Axes.SetLimitsX(0, nyquistFrequency);
                WpfPlotFFT.Plot.Axes.SetLimitsY(-60, 100);
            }
            else
            {
                WpfPlotFFT.Plot.Title("FFT Spectrum");
            }

            // Disable auto scaling to prevent automatic axis adjustments
            WpfPlotFFT.Plot.Axes.ContinuouslyAutoscale = false;

            // Setup user input for zoom/pan
            SetupPlotUserInput();

            // Reset signal tracking
            _spectrumSignal = null;
            _lastSpectrumLength = -1;

            WpfPlotFFT.Refresh();
        }

        /// <summary>
        /// Handle a new spectrum being computed on the model side (raised on whatever thread
        /// the measurement update runs on - currently always the UI thread, but dispatch
        /// defensively in case that ever changes).
        /// </summary>
        private void FFTAnalysis_SpectrumUpdated(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(RefreshSpectrumPlot));
        }

        /// <summary>
        /// Update the spectrum plot with current data (efficient updates following PlotManager
        /// pattern) - only recreates the signal when data length changes (FFT size/interpolation).
        /// </summary>
        public void RefreshSpectrumPlot()
        {
            FFTAnalysis fft = FFTAnalysis;
            if (fft == null)
                return;

            double[] frequencies = fft.SpectrumFrequencies;
            double[] magnitudes = fft.SpectrumMagnitudes;
            int currentSpectrumLength = frequencies.Length;

            bool needsRebuild = _spectrumSignal == null || _lastSpectrumLength != currentSpectrumLength;

            if (needsRebuild)
            {
                if (_spectrumSignal != null)
                    WpfPlotFFT.Plot.Remove(_spectrumSignal);

                _spectrumSignal = WpfPlotFFT.Plot.Add.SignalXY(frequencies, magnitudes);

                ScottPlot.Color scColor = new ScottPlot.Color(fft.ChannelColor.R, fft.ChannelColor.G, fft.ChannelColor.B);
                _spectrumSignal.Color = scColor;
                _spectrumSignal.LineWidth = 2.0f;
                _spectrumSignal.LineStyle.AntiAlias = true;
                _spectrumSignal.MarkerShape = ScottPlot.MarkerShape.None;

                _lastSpectrumLength = currentSpectrumLength;
            }

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
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            // Clean up timer if still running
            if (_autoScaleTimer != null)
            {
                _autoScaleTimer.Stop();
                _autoScaleTimer.Tick -= AutoScaleTimer_Tick;
                _autoScaleTimer = null;
            }

            if (DataContext is Measurement measurement)
            {
                measurement.FFT.SpectrumUpdated -= FFTAnalysis_SpectrumUpdated;
                measurement.FFT.ComputeSpectrum = false;
            }

            _spectrumSignal = null;
            _lastSpectrumLength = -1;

            base.OnClosed(e);
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
            FFTAnalysis?.SortPeaks(_currentSortColumn, _currentSortDirection);
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
