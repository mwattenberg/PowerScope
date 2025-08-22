using System;
using System.Windows;
using System.Timers;
using ScottPlot;
using ScottPlot.Plottables;
using ScottPlot.WPF;
using System.Windows.Media;
using SerialPlotDN_WPF.View.UserControls;
using Color = System.Windows.Media.Color;
using System.Collections.ObjectModel;

namespace SerialPlotDN_WPF.Model
{
    public class PlotManager
    {
        private readonly System.Timers.Timer _updatePlotTimer;
        private readonly WpfPlotGL _plot;
        private readonly ChannelControlBar _channelControlBar;
        private readonly VerticalControl _verticalControl;
        private readonly HorizontalControl _horizontalControl;
        private readonly Signal[] _signals;
        private readonly double[][] _data;
        private readonly int _channelCount;
        public List<SerialDataStream> _dataStreams = new List<SerialDataStream>();
        
        public int Xmin { get; set; } = 0;
        public int Xmax { get; set; } = 3000;
        public int Ymin { get; set; } = -200;
        public int Ymax { get; set; } = 4000;

        public PlotManager(WpfPlotGL wpfPlot1, ChannelControlBar channelControlBar, VerticalControl verticalControl, HorizontalControl horizontalControl, SerialDataStream datastream)
        {
            _plot = wpfPlot1;
            _channelControlBar = channelControlBar;
            _verticalControl = verticalControl;
            _horizontalControl = horizontalControl;
            _channelCount = datastream.Parser.NumberOfChannels;
            _dataStreams.Add(datastream);
            _signals = new Signal[_channelCount];
            _data = new double[_channelCount][];
            _updatePlotTimer = new System.Timers.Timer(33) { Enabled = true, AutoReset = true };
            _updatePlotTimer.Elapsed += UpdatePlot;
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
            //foreach (var axis in _plot.Plot.Axes.GetAxes())
            //{
            //    axis.TickLabelStyle.FontSize *= 2;
            //    axis.Label.FontSize *= 2;
            //}
            for (int i = 0; i < _channelCount; i++)
            {
                _data[i] = new double[Xmax];
                _signals[i] = _plot.Plot.Add.Signal(_data[i]);
                _signals[i].Color = _signals[i].Color.Lighten(0.2);
                _signals[i].Color = _signals[i].Color.WithOpacity(1.0);
                _signals[i].LineWidth = 1;
                _signals[i].LineStyle.AntiAlias = false;
            }

            setDarkMode();
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
                _channelControlBar.AddChannel(channel);
            }
        }

        public void startAutoUpdate() => _updatePlotTimer.Start();
        public void stopAutoUpdate() => _updatePlotTimer.Stop();

        public void UpdatePlot(object? source, ElapsedEventArgs? e)
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                //for (int channel = 0; channel < _channelCount; channel++)
                //{
                //    // Efficiently copy data to pre-allocated array without memory allocation
                //    _dataStreams[0].CopyLatestDataTo(channel, _data[channel], Xmax);
                //}

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
            for (int i = 0; i < _channelCount; i++)
            {
                var colorOld = _signals[i].Color;
                var linewidthOld = _signals[i].LineWidth;
                var antiAliasingOld = _signals[i].LineStyle.AntiAlias;
                _plot.Plot.Remove(_signals[i]);
                _data[i] = new double[Xmax];
                _signals[i] = _plot.Plot.Add.Signal(_data[i]);
                _signals[i].Color = colorOld;
                _signals[i].LineWidth = linewidthOld;
                _signals[i].LineStyle.AntiAlias = antiAliasingOld;
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
            var zoomRectangleButton = ScottPlot.Interactivity.StandardMouseButtons.Right;
            var zoomRectangleResponse = new ScottPlot.Interactivity.UserActionResponses.MouseDragZoomRectangle(zoomRectangleButton);
            _plot.UserInputProcessor.UserActionResponses.Add(zoomRectangleResponse);
        }

        public void SetYLimits(int ymin, int ymax)
        {
            Ymin = ymin;
            Ymax = ymax;
            _plot.Plot.Axes.SetLimitsY(Ymin, Ymax);
            _plot.Refresh();
        }

        public int CurrentPlotUpdateRateFPS => (int)(1000.0 / _updatePlotTimer.Interval);
        public int CurrentLineWidth => (int)(_signals[0]?.LineWidth ?? 1);
        public bool CurrentAntiAliasing => _signals[0]?.LineStyle.AntiAlias ?? false;
        public bool ShowRenderTime => _plot.Plot.Benchmark.IsVisible;
    }
}
