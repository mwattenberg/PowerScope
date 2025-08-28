using System.Collections.Generic;
using System.Timers;
using System.Windows;
using ScottPlot.Plottables;
using ScottPlot.WPF;
using SerialPlotDN_WPF.View.UserControls;
using Color = System.Windows.Media.Color;

namespace SerialPlotDN_WPF.Model
{
    public class PlotManager
    {
        private readonly System.Timers.Timer _updatePlotTimer;
        private readonly WpfPlotGL _plot;
        private readonly VerticalControl _verticalControl;
        private readonly HorizontalControl _horizontalControl;
        private readonly Signal[] _signals;
        private readonly double[][] _data;
        private readonly int _maxChannels;

        // Stream management - simplified interface
        private List<IDataStream> _connectedStreams;

        // Plot settings - centralized configuration
        public PlotSettings Settings { get; private set; }

        /// <summary>
        /// Access to the underlying WpfPlotGL control
        /// </summary>
        public WpfPlotGL Plot => _plot;

        /// <summary>
        /// Current number of channels being plotted
        /// </summary>
        public int NumberOfChannels { get; private set; } = 0;

        // Delegate X-axis properties to PlotSettings
        public int Xmin 
        { 
            get => Settings.Xmin; 
            set => Settings.Xmin = value; 
        }

        public int Xmax 
        { 
            get => Settings.Xmax; 
            set => Settings.Xmax = value; 
        }

        // Delegate Y-axis properties to PlotSettings
        public int Ymin 
        { 
            get => Settings.Ymin; 
            set => Settings.Ymin = value; 
        }

        public int Ymax 
        { 
            get => Settings.Ymax; 
            set => Settings.Ymax = value; 
        }

        public PlotManager(WpfPlotGL wpfPlot1, VerticalControl verticalControl, HorizontalControl horizontalControl, int maxChannels = 12)
        {
            _plot = wpfPlot1;
            _verticalControl = verticalControl;
            _horizontalControl = horizontalControl;
            _maxChannels = maxChannels;
            _signals = new Signal[_maxChannels];
            
            // Initialize plot settings
            Settings = new PlotSettings();
            
            _data = new double[_maxChannels][];
            _updatePlotTimer = new System.Timers.Timer(Settings.TimerInterval) { Enabled = false, AutoReset = true };
            _updatePlotTimer.Elapsed += UpdatePlot;

            // Subscribe to settings changes to update timer interval and plot limits
            Settings.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(PlotSettings.PlotUpdateRateFPS))
                {
                    _updatePlotTimer.Interval = Settings.TimerInterval;
                }
                else if (e.PropertyName == nameof(PlotSettings.Ymin) || e.PropertyName == nameof(PlotSettings.Ymax))
                {
                    Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        _plot.Plot.Axes.SetLimitsY(Settings.Ymin, Settings.Ymax);
                        _plot.Refresh();
                    });
                }
                else if (e.PropertyName == nameof(PlotSettings.Xmax))
                {
                    Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        updateHorizontalScale(null, Settings.Xmax);
                    });
                }
                else if (e.PropertyName == nameof(PlotSettings.SerialPortUpdateRateHz))
                {
                    // Propagate update rate to all connected serial streams
                    if (_connectedStreams != null)
                    {
                        foreach (var stream in _connectedStreams)
                        {
                            if (stream is SerialDataStream serialStream)
                            {
                                serialStream.SerialPortUpdateRateHz = Settings.SerialPortUpdateRateHz;
                            }
                        }
                    }
                }
                else if (e.PropertyName == nameof(PlotSettings.LineWidth) ||
                         e.PropertyName == nameof(PlotSettings.AntiAliasing) ||
                         e.PropertyName == nameof(PlotSettings.ShowRenderTime))
                {
                    ApplyCurrentSettings(); // Update line width, anti-aliasing, and render time for all signals
                }
            };
        }

        /// <summary>
        /// Updates the data streams that provide data for plotting
        /// /// </summary>
        /// <param name="connectedStreams">Currently connected streams</param>
        public void SetDataStreams(List<IDataStream> connectedStreams)
        {
            _connectedStreams = connectedStreams;
        }

        private void setDarkMode()
        {
            _plot.Plot.FigureBackground.Color = ScottPlot.Color.FromHex("#181818");
            _plot.Plot.DataBackground.Color = ScottPlot.Color.FromHex("#1f1f1f");
            _plot.Plot.Axes.Color(ScottPlot.Color.FromHex("#d7d7d7"));
            _plot.Plot.Grid.LineWidth = 1.0f;
            _plot.Plot.Grid.MajorLineColor = ScottPlot.Color.FromHex("#404040");
            _plot.Plot.Legend.BackgroundColor = ScottPlot.Color.FromHex("#404040");
            _plot.Plot.Legend.FontColor = ScottPlot.Color.FromHex("#d7d7d7");
            _plot.Plot.Legend.OutlineColor = ScottPlot.Color.FromHex("#d7d7d7");
            _plot.Plot.Axes.ContinuouslyAutoscale = false;
            _plot.Plot.RenderManager.ClearCanvasBeforeEachRender = true;
            _plot.Plot.Axes.SetLimitsX(Xmin, Xmax);
            _plot.Plot.Axes.SetLimitsY(Ymin, Ymax);
            _plot.Plot.Axes.Bottom.IsVisible = true;
        }

        public void InitializePlot()
        {
            _plot.Plot.Clear();
            _plot.Plot.Add.Palette = new ScottPlot.Palettes.Category10();
            
            // Initialize all potential signal slots but don't add them to plot yet
            for (int i = 0; i < _maxChannels; i++)
            {
                _data[i] = new double[Xmax];
            }

            setDarkMode();
        }

        /// <summary>
        /// Updates the plot to display the specified number of channels
        /// /// </summary>
        /// <param name="channelCount">Number of channels to display</param>
        /// <param name="channelColors">Optional colors for each channel</param>
        public void UpdateChannelDisplay(int channelCount, Color[] channelColors = null)
        {
            // Update the number of channels
            NumberOfChannels = Math.Min(channelCount, _maxChannels);

            // Remove existing signals
            for (int i = 0; i < _maxChannels; i++)
            {
                if (_signals[i] != null)
                {
                    _plot.Plot.Remove(_signals[i]);
                    _signals[i] = null;
                }
            }

            // Add signals for active channels
            for (int i = 0; i < NumberOfChannels; i++)
            {
                _signals[i] = _plot.Plot.Add.Signal(_data[i]);
                
                // Set color if provided, otherwise use palette
                if (channelColors != null && i < channelColors.Length)
                {
                    Color color = channelColors[i];
                    _signals[i].Color = new ScottPlot.Color(color.R, color.G, color.B);
                }
                else
                {
                    _signals[i].Color = _signals[i].Color.Lighten(0.2);
                }

                _signals[i].MarkerShape = ScottPlot.MarkerShape.None;
                _signals[i].Color = _signals[i].Color.WithOpacity(1.0);
                _signals[i].LineWidth = 1;
                _signals[i].LineStyle.AntiAlias = false;
            }

            _plot.Refresh();
        }

        /// <summary>
        /// Gets the colors currently used by the plot signals
        /// /// </summary>
        /// <param name="channelCount">Number of channels to get colors for</param>
        /// <returns>Array of colors</returns>
        public Color[] GetSignalColors(int channelCount)
        {
            Color[] colors = new Color[channelCount];
            for (int i = 0; i < channelCount && i < _maxChannels; i++)
            {
                if (_signals[i] != null)
                {
                    ScottPlot.Color signalColor = _signals[i].Color;
                    colors[i] = Color.FromArgb(signalColor.A, signalColor.R, signalColor.G, signalColor.B);
                }
                else
                {
                    // Fallback to default colors
                    Color[] defaultColors = new Color[]
                    {
                        System.Windows.Media.Colors.Red, System.Windows.Media.Colors.Green, System.Windows.Media.Colors.Blue, System.Windows.Media.Colors.Orange,
                        System.Windows.Media.Colors.Purple, System.Windows.Media.Colors.Brown, System.Windows.Media.Colors.Pink, System.Windows.Media.Colors.Gray,
                        System.Windows.Media.Colors.Yellow, System.Windows.Media.Colors.Cyan, System.Windows.Media.Colors.Magenta, System.Windows.Media.Colors.Lime
                    };
                    colors[i] = defaultColors[i % defaultColors.Length];
                }
            }
            return colors;
        }

        public void startAutoUpdate()
        {
            _updatePlotTimer.Start();
        }

        public void stopAutoUpdate()
        {
            _updatePlotTimer.Stop();
        }

        public void UpdatePlot(object? source, ElapsedEventArgs? e)
        {
            if (Application.Current == null)
                return;

            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                int channelIndex = 0;
                
                // Iterate through all connected streams and their channels
                foreach (var stream in _connectedStreams)
                {
                    for (int streamChannel = 0; streamChannel < stream.ChannelCount; streamChannel++)
                    {
                        stream.CopyLatestTo(streamChannel, _data[streamChannel], Xmax);
                    }
                }

                // Use YAutoScale from Settings instead of VerticalControl
                if (Settings.YAutoScale)
                    _plot.Plot.Axes.AutoScaleY();

                _plot.Refresh();

            }, System.Windows.Threading.DispatcherPriority.Render);
        }

        public void updateHorizontalScale(object? sender, int numberOfPointsToShow)
        {
            Xmax = numberOfPointsToShow;
            bool isRunning = _updatePlotTimer.Enabled;
            if (_updatePlotTimer.Enabled)
                _updatePlotTimer.Stop();
                
            // Recreate data arrays for all channels
            for (int i = 0; i < _maxChannels; i++)
            {
                _data[i] = new double[Xmax];
                
                if (_signals[i] != null)
                {
                    ScottPlot.Color colorOld = _signals[i].Color;
                    float linewidthOld = _signals[i].LineWidth;
                    bool antiAliasingOld = _signals[i].LineStyle.AntiAlias;
                    _plot.Plot.Remove(_signals[i]);
                    _signals[i] = _plot.Plot.Add.Signal(_data[i]);
                    _signals[i].Color = colorOld;
                    _signals[i].LineWidth = linewidthOld;
                    _signals[i].MarkerShape = ScottPlot.MarkerShape.None;
                    _signals[i].LineStyle.AntiAlias = antiAliasingOld;
                }
            }
            
            _plot.Plot.Axes.SetLimitsX(0, Xmax);
            if (isRunning)
                _updatePlotTimer.Start();
            else
                UpdatePlot(null, null);
        }

        public void ApplyPlotSettings(double plotUpdateRateFPS, int lineWidth, bool antiAliasing, bool showRenderTime)
        {
            Settings.PlotUpdateRateFPS = plotUpdateRateFPS;
            Settings.LineWidth = lineWidth;
            Settings.AntiAliasing = antiAliasing;
            Settings.ShowRenderTime = showRenderTime;

            ApplyCurrentSettings();
        }

        /// <summary>
        /// Apply current settings to the plot
        /// </summary>
        public void ApplyCurrentSettings()
        {
            _updatePlotTimer.Interval = Settings.TimerInterval;
            _plot.Plot.Grid.LineWidth = (float)Settings.LineWidth;
            
            for (int i = 0; i < _signals.Length; i++)
            {
                if (_signals[i] != null)
                {
                    _signals[i].LineWidth = (float)Settings.LineWidth;
                    _signals[i].LineStyle.AntiAlias = Settings.AntiAliasing;
                }
            }
            
            _plot.Plot.Benchmark.IsVisible = Settings.ShowRenderTime;
            _plot.Refresh();
        }

        public void SetupPlotUserInput()
        {
            _plot.UserInputProcessor.RemoveAll<ScottPlot.Interactivity.IUserActionResponse>();
            _plot.UserInputProcessor.IsEnabled = true;
            ScottPlot.Interactivity.MouseButton zoomRectangleButton = ScottPlot.Interactivity.StandardMouseButtons.Left;
            ScottPlot.Interactivity.UserActionResponses.MouseDragZoomRectangle zoomRectangleResponse = new ScottPlot.Interactivity.UserActionResponses.MouseDragZoomRectangle(zoomRectangleButton);
            _plot.UserInputProcessor.UserActionResponses.Add(zoomRectangleResponse);

            // Custom right-click auto-scale X only
            _plot.MouseRightButtonUp += (s, e) =>
            {
                // Auto-scale X-axis only
                _plot.Plot.Axes.AutoScaleX();
                _plot.Refresh();
            };

            // Zoom with mouse wheel
            ScottPlot.Interactivity.Key horizontalLock = ScottPlot.Interactivity.StandardKeys.Shift;
            ScottPlot.Interactivity.Key verticalLock= ScottPlot.Interactivity.StandardKeys.Control;

            var wheelZoomResponse = new ScottPlot.Interactivity.UserActionResponses.MouseWheelZoom(horizontalLock, verticalLock);
            _plot.UserInputProcessor.UserActionResponses.Add(wheelZoomResponse);
        }
    }
}
