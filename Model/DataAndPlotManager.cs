using System;
using System.Windows;
using System.Timers;
using ScottPlot;
using ScottPlot.Plottables;
using ScottPlot.WPF;
using System.Windows.Media;
using SerialPlotDN_WPF.View.UserControls;
using Color = System.Windows.Media.Color;

namespace SerialPlotDN_WPF.Model
{
    public class DataAndPlotManager
    {
        private readonly System.Timers.Timer _updatePlotTimer;
        private readonly WpfPlotGL WpfPlot1;
        private readonly ChannelControlBar ChannelControlBar;
        private readonly VerticalControl VerticalControl;
        private readonly HorizontalControl HorizontalControl;
        private readonly Signal[] _signals;
        private readonly double[][] _linearizedDataArrays;
        private readonly int _channelCount;
        public int DisplayElements { get; set; } = 3000;
        public int Ymin { get; set; } = -200;
        public int Ymax { get; set; } = 4000;

        public DataAndPlotManager(WpfPlotGL wpfPlot1, ChannelControlBar channelControlBar, VerticalControl verticalControl, HorizontalControl horizontalControl, int channelCount = 8)
        {
            WpfPlot1 = wpfPlot1;
            ChannelControlBar = channelControlBar;
            VerticalControl = verticalControl;
            HorizontalControl = horizontalControl;
            _channelCount = channelCount;
            _signals = new Signal[_channelCount];
            _linearizedDataArrays = new double[_channelCount][];
            _updatePlotTimer = new System.Timers.Timer(33) { Enabled = true, AutoReset = true };
            _updatePlotTimer.Elapsed += UpdatePlot;
        }

        public void InitializePlot()
        {
            WpfPlot1.Plot.Clear();
            WpfPlot1.Plot.Add.Palette = new ScottPlot.Palettes.Category10();
            foreach (var axis in WpfPlot1.Plot.Axes.GetAxes())
            {
                axis.TickLabelStyle.FontSize *= 2;
                axis.Label.FontSize *= 2;
            }
            for (int i = 0; i < _channelCount; i++)
            {
                _linearizedDataArrays[i] = new double[DisplayElements];
                _signals[i] = WpfPlot1.Plot.Add.Signal(_linearizedDataArrays[i]);
                _signals[i].Color = _signals[i].Color.Lighten(0.2);
                _signals[i].Color = _signals[i].Color.WithOpacity(1.0);
                _signals[i].LineWidth = 1;
                _signals[i].LineStyle.AntiAlias = false;
            }

            WpfPlot1.Plot.FigureBackground.Color = ScottPlot.Color.FromHex("#181818");
            WpfPlot1.Plot.DataBackground.Color = ScottPlot.Color.FromHex("#1f1f1f");
            WpfPlot1.Plot.Axes.Color(ScottPlot.Color.FromHex("#d7d7d7"));
            WpfPlot1.Plot.Grid.LineWidth = 1.0f;
            WpfPlot1.Plot.Grid.MajorLineColor = ScottPlot.Color.FromHex("#404040");
            WpfPlot1.Plot.Legend.BackgroundColor = ScottPlot.Color.FromHex("#404040");
            WpfPlot1.Plot.Legend.FontColor = ScottPlot.Color.FromHex("#d7d7d7");
            WpfPlot1.Plot.Legend.OutlineColor = ScottPlot.Color.FromHex("#d7d7d7");
            WpfPlot1.Plot.Axes.ContinuouslyAutoscale = false;
            WpfPlot1.Plot.RenderManager.ClearCanvasBeforeEachRender = true;
            WpfPlot1.Plot.Axes.SetLimitsX(0, DisplayElements);
            WpfPlot1.Plot.Axes.SetLimitsY(Ymin, Ymax);
            WpfPlot1.Plot.Axes.Bottom.IsVisible = true;
        }

        public void InitializeChannelControlBar()
        {
            for (int i = 0; i < _channelCount; i++)
            {
                ChannelControl channel = new ChannelControl();
                var channelColor = _signals[i].Color;
                var wpfColor = Color.FromArgb(channelColor.A, channelColor.R, channelColor.G, channelColor.B);
                channel.Color = wpfColor;
                channel.Label = $"CH{i + 1}";
                channel.Gain = 1.0;
                channel.Offset = 0.0;
                ChannelControlBar.AddChannel(channel);
            }
        }

        public void StartPlotTimer() => _updatePlotTimer.Start();
        public void StopPlotTimer() => _updatePlotTimer.Stop();

        public void UpdatePlot(object? source, ElapsedEventArgs? e)
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                if (VerticalControl.IsAutoScale)
                    WpfPlot1.Plot.Axes.AutoScaleY();
                WpfPlot1.Refresh();
            }, System.Windows.Threading.DispatcherPriority.Render);
        }

        public void OnWindowSizeChanged(object? sender, int newSize)
        {
            DisplayElements = newSize;
            bool isRunning = _updatePlotTimer.Enabled;
            if (_updatePlotTimer.Enabled)
                _updatePlotTimer.Stop();
            for (int i = 0; i < _channelCount; i++)
            {
                var colorOld = _signals[i].Color;
                var linewidthOld = _signals[i].LineWidth;
                var antiAliasingOld = _signals[i].LineStyle.AntiAlias;
                WpfPlot1.Plot.Remove(_signals[i]);
                _linearizedDataArrays[i] = new double[DisplayElements];
                _signals[i] = WpfPlot1.Plot.Add.Signal(_linearizedDataArrays[i]);
                _signals[i].Color = colorOld;
                _signals[i].LineWidth = linewidthOld;
                _signals[i].LineStyle.AntiAlias = antiAliasingOld;
            }
            WpfPlot1.Plot.Axes.SetLimitsX(0, DisplayElements);
            if (isRunning)
                _updatePlotTimer.Start();
            else
                UpdatePlot(null, null);
        }

        public void ApplyPlotSettings(int plotUpdateRateFPS, int lineWidth, bool antiAliasing, bool showRenderTime)
        {
            _updatePlotTimer.Interval = 1000.0 / plotUpdateRateFPS;
            WpfPlot1.Plot.Grid.LineWidth = (float)lineWidth / 2;
            for (int i = 0; i < _signals.Length; i++)
            {
                if (_signals[i] != null)
                {
                    _signals[i].LineWidth = (float)lineWidth;
                    _signals[i].LineStyle.AntiAlias = antiAliasing;
                }
            }
            WpfPlot1.Plot.Benchmark.IsVisible = showRenderTime;
            WpfPlot1.Refresh();
        }

        public void SetupPlotUserInput()
        {
            WpfPlot1.UserInputProcessor.Reset();
            WpfPlot1.UserInputProcessor.IsEnabled = false;
            var zoomRectangleButton = ScottPlot.Interactivity.StandardMouseButtons.Right;
            var zoomRectangleResponse = new ScottPlot.Interactivity.UserActionResponses.MouseDragZoomRectangle(zoomRectangleButton);
            WpfPlot1.UserInputProcessor.UserActionResponses.Add(zoomRectangleResponse);
        }

        public void SetYLimits(int ymin, int ymax)
        {
            Ymin = ymin;
            Ymax = ymax;
            WpfPlot1.Plot.Axes.SetLimitsY(Ymin, Ymax);
            WpfPlot1.Refresh();
        }

        public int CurrentPlotUpdateRateFPS => (int)(1000.0 / _updatePlotTimer.Interval);
        public int CurrentLineWidth => (int)(_signals[0]?.LineWidth ?? 1);
        public bool CurrentAntiAliasing => _signals[0]?.LineStyle.AntiAlias ?? false;
    }
}
