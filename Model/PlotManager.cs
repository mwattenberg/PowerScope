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

        /// <summary>
        /// Current number of channels being plotted
        /// </summary>
        public int NumberOfChannels { get; private set; } = 0;

        public int Xmin { get; set; } = 0;
        public int Xmax { get; set; } = 3000;
        public int Ymin { get; set; } = -200;
        public int Ymax { get; set; } = 4000;

        public PlotManager(WpfPlotGL wpfPlot1, VerticalControl verticalControl, HorizontalControl horizontalControl, int maxChannels = 12)
        {
            _plot = wpfPlot1;
            _verticalControl = verticalControl;
            _horizontalControl = horizontalControl;
            _maxChannels = maxChannels;
            _signals = new Signal[_maxChannels];
            
            _data = new double[_maxChannels][];
            _updatePlotTimer = new System.Timers.Timer(33) { Enabled = true, AutoReset = true };
            _updatePlotTimer.Elapsed += UpdatePlot;
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

                if (_verticalControl.IsAutoScale)
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
                    _signals[i].LineStyle.AntiAlias = antiAliasingOld;
                }
            }
            
            _plot.Plot.Axes.SetLimitsX(0, Xmax);
            if (isRunning)
                _updatePlotTimer.Start();
            else
                UpdatePlot(null, null);
        }

        public void ApplyPlotSettings(int plotUpdateRateFPS, int lineWidth, bool antiAliasing, bool showRenderTime)
        {
            _updatePlotTimer.Interval = 1000.0 / plotUpdateRateFPS;
            _plot.Plot.Grid.LineWidth = (float)lineWidth / 2;
            for (int i = 0; i < _signals.Length; i++)
            {
                if (_signals[i] != null)
                {
                    _signals[i].LineWidth = (float)lineWidth;
                    _signals[i].LineStyle.AntiAlias = antiAliasing;
                }
            }
            _plot.Plot.Benchmark.IsVisible = showRenderTime;
            _plot.Refresh();
        }

        public void SetupPlotUserInput()
        {
            _plot.UserInputProcessor.Reset();
            _plot.UserInputProcessor.IsEnabled = false;
            ScottPlot.Interactivity.MouseButton zoomRectangleButton = ScottPlot.Interactivity.StandardMouseButtons.Right;
            ScottPlot.Interactivity.UserActionResponses.MouseDragZoomRectangle zoomRectangleResponse = new ScottPlot.Interactivity.UserActionResponses.MouseDragZoomRectangle(zoomRectangleButton);
            _plot.UserInputProcessor.UserActionResponses.Add(zoomRectangleResponse);
        }

        public void SetYLimits(int ymin, int ymax)
        {
            Ymin = ymin;
            Ymax = ymax;
            _plot.Plot.Axes.SetLimitsY(Ymin, Ymax);
            _plot.Refresh();
        }

        public int CurrentPlotUpdateRateFPS
        {
            get
            {
                return (int)(1000.0 / _updatePlotTimer.Interval);
            }
        }

        public int CurrentLineWidth
        {
            get
            {
                return (int)_signals[0].LineWidth;   
            }
        }

        public bool CurrentAntiAliasing
        {
            get
            {
                return _signals[0].LineStyle.AntiAlias;   
            }
        }

        public bool ShowRenderTime
        {
            get
            {
                return _plot.Plot.Benchmark.IsVisible;
            }
        }
    }
}
