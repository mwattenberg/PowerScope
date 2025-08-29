using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Timers;
using System.Windows;
using ScottPlot.Plottables;
using ScottPlot.WPF;
using Color = System.Windows.Media.Color;

namespace SerialPlotDN_WPF.Model
{
    public class PlotManager
    {
        private readonly System.Timers.Timer _updatePlotTimer;
        private readonly WpfPlotGL _plot;
        private readonly Signal[] _signals;
        private readonly double[][] _data;
        private readonly int _maxChannels;

        // Stream management
        private List<IDataStream> _connectedStreams;
        
        // Channel settings for enabled/disabled state
        private ObservableCollection<ChannelSettings> _channelSettings;

        // Plot settings - centralized configuration
        public PlotSettings Settings { get; private set; }

        public WpfPlotGL Plot 
        { 
            get 
            { 
                return _plot; 
            } 
        }

        public int NumberOfChannels { get; private set; } = 0;

        /// <summary>
        /// Gets a color from the ScottPlot Category10 palette
        /// </summary>
        /// <param name="index">Index of the color to retrieve</param>
        /// <returns>WPF Color from the palette</returns>
        public static Color GetColor(int index)
        {
            ScottPlot.Palettes.Category10 palette = new ScottPlot.Palettes.Category10();
            ScottPlot.Color scottPlotColor = palette.GetColor(index);
            return Color.FromArgb(scottPlotColor.A, scottPlotColor.R, scottPlotColor.G, scottPlotColor.B);
        }

        public PlotManager(WpfPlotGL wpfPlot1, int maxChannels = 12)
        {
            _plot = wpfPlot1;
            _maxChannels = maxChannels;
            _signals = new Signal[_maxChannels];
            
            Settings = new PlotSettings();
            
            _data = new double[_maxChannels][];
            _updatePlotTimer = new System.Timers.Timer(Settings.TimerInterval);
            _updatePlotTimer.Enabled = false;
            _updatePlotTimer.AutoReset = true;
            _updatePlotTimer.Elapsed += UpdatePlot;

            Settings.PropertyChanged += Settings_PropertyChanged;
        }

        private void Settings_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PlotSettings.PlotUpdateRateFPS))
            {
                _updatePlotTimer.Interval = Settings.TimerInterval;
            }
            else if (e.PropertyName == nameof(PlotSettings.Ymin) || e.PropertyName == nameof(PlotSettings.Ymax))
            {
                if (Application.Current != null)
                {
                    Application.Current.Dispatcher.BeginInvoke(() =>
                    {
                        _plot.Plot.Axes.SetLimitsY(Settings.Ymin, Settings.Ymax);
                        _plot.Refresh();
                    });
                }
            }
            else if (e.PropertyName == nameof(PlotSettings.Xmax))
            {
                if (Application.Current != null)
                {
                    Application.Current.Dispatcher.BeginInvoke(() =>
                    {
                        updateHorizontalScale(null, Settings.Xmax);
                    });
                }
            }
            else if (e.PropertyName == nameof(PlotSettings.SerialPortUpdateRateHz))
            {
                if (_connectedStreams != null)
                {
                    foreach (IDataStream stream in _connectedStreams)
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
                ApplyCurrentSettings();
            }
        }

        public void SetChannelSettings(ObservableCollection<ChannelSettings> channelSettings)
        {
            // Unsubscribe from old channel settings
            if (_channelSettings != null)
            {
                _channelSettings.CollectionChanged -= ChannelSettings_CollectionChanged;
                foreach (ChannelSettings setting in _channelSettings)
                {
                    setting.PropertyChanged -= ChannelSetting_PropertyChanged;
                }
            }

            _channelSettings = channelSettings;

            // Subscribe to new channel settings
            if (_channelSettings != null)
            {
                _channelSettings.CollectionChanged += ChannelSettings_CollectionChanged;
                foreach (ChannelSettings setting in _channelSettings)
                {
                    setting.PropertyChanged += ChannelSetting_PropertyChanged;
                }
            }
        }

        private void ChannelSettings_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            // Subscribe to PropertyChanged for new items
            if (e.NewItems != null)
            {
                foreach (ChannelSettings setting in e.NewItems)
                {
                    setting.PropertyChanged += ChannelSetting_PropertyChanged;
                }
            }

            // Unsubscribe from PropertyChanged for removed items
            if (e.OldItems != null)
            {
                foreach (ChannelSettings setting in e.OldItems)
                {
                    setting.PropertyChanged -= ChannelSetting_PropertyChanged;
                }
            }
        }

        private void ChannelSetting_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ChannelSettings.IsEnabled))
            {
                // Refresh the channel display when enabled state changes
                if (Application.Current != null)
                {
                    Application.Current.Dispatcher.BeginInvoke(() =>
                    {
                        UpdateChannelDisplay(NumberOfChannels);
                    });
                }
            }
            else if (e.PropertyName == nameof(ChannelSettings.Color))
            {
                // Update signal color when channel color changes
                if (Application.Current != null)
                {
                    Application.Current.Dispatcher.BeginInvoke(() =>
                    {
                        UpdateSignalColors();
                    });
                }
            }
        }

        private void UpdateSignalColors()
        {
            if (_channelSettings == null)
                return;

            for (int i = 0; i < NumberOfChannels && i < _maxChannels; i++)
            {
                if (_signals[i] != null && i < _channelSettings.Count)
                {
                    Color channelColor = _channelSettings[i].Color;
                    _signals[i].Color = new ScottPlot.Color(channelColor.R, channelColor.G, channelColor.B);
                }
            }
            
            _plot.Refresh();
        }

        public void SetDataStreams(List<IDataStream> connectedStreams)
        {
            _connectedStreams = connectedStreams;
        }

        private void setDarkMode()
        {
            _plot.Plot.FigureBackground.Color = ScottPlot.Color.FromHex("#181818");
            _plot.Plot.DataBackground.Color = ScottPlot.Color.FromHex("#1f1f1f");
            _plot.Plot.Axes.Color(ScottPlot.Color.FromHex("#d7d7d7"));
            _plot.Plot.Grid.LineWidth = (float)Settings.LineWidth;
            _plot.Plot.Grid.MajorLineColor = ScottPlot.Color.FromHex("#404040");
            _plot.Plot.Legend.BackgroundColor = ScottPlot.Color.FromHex("#404040");
            _plot.Plot.Legend.FontColor = ScottPlot.Color.FromHex("#d7d7d7");
            _plot.Plot.Legend.OutlineColor = ScottPlot.Color.FromHex("#d7d7d7");
            _plot.Plot.Axes.ContinuouslyAutoscale = false;
            _plot.Plot.RenderManager.ClearCanvasBeforeEachRender = true;
            _plot.Plot.Axes.SetLimitsX(Settings.Xmin, Settings.Xmax);
            _plot.Plot.Axes.SetLimitsY(Settings.Ymin, Settings.Ymax);
            _plot.Plot.Axes.Bottom.IsVisible = true;
        }

        public void InitializePlot()
        {
            _plot.Plot.Clear();
            _plot.Plot.Add.Palette = new ScottPlot.Palettes.Category10();
            
            for (int i = 0; i < _maxChannels; i++)
            {
                _data[i] = new double[Settings.Xmax];
            }

            setDarkMode();
        }

        public void UpdateChannelDisplay(int channelCount, Color[] channelColors = null)
        {
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

            // Add signals only for enabled channels
            for (int i = 0; i < NumberOfChannels; i++)
            {
                bool isChannelEnabled = true;
                if (_channelSettings != null && i < _channelSettings.Count)
                {
                    isChannelEnabled = _channelSettings[i].IsEnabled;
                }

                if (isChannelEnabled)
                {
                    _signals[i] = _plot.Plot.Add.Signal(_data[i]);
                    
                    // Always use the color from ChannelSettings for consistency
                    Color channelColor;
                    if (_channelSettings != null && i < _channelSettings.Count)
                    {
                        channelColor = _channelSettings[i].Color;
                    }
                    else if (channelColors != null && i < channelColors.Length)
                    {
                        channelColor = channelColors[i];
                    }
                    else
                    {
                        channelColor = GetColor(i);
                    }
                    
                    _signals[i].Color = new ScottPlot.Color(channelColor.R, channelColor.G, channelColor.B);
                    _signals[i].MarkerShape = ScottPlot.MarkerShape.None;
                    _signals[i].LineWidth = (float)Settings.LineWidth;
                    _signals[i].LineStyle.AntiAlias = Settings.AntiAliasing;
                }
            }

            _plot.Refresh();
        }

        public Color[] GetSignalColors(int channelCount)
        {
            Color[] colors = new Color[channelCount];
            for (int i = 0; i < channelCount && i < _maxChannels; i++)
            {
                if (_channelSettings != null && i < _channelSettings.Count)
                {
                    // Use color from channel settings
                    colors[i] = _channelSettings[i].Color;
                }
                else if (_signals[i] != null)
                {
                    // Fallback to current signal color
                    ScottPlot.Color signalColor = _signals[i].Color;
                    colors[i] = Color.FromArgb(signalColor.A, signalColor.R, signalColor.G, signalColor.B);
                }
                else
                {
                    // Final fallback to palette color
                    colors[i] = GetColor(i);
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
                
                if (_connectedStreams != null)
                {
                    foreach (IDataStream stream in _connectedStreams)
                    {
                        for (int streamChannel = 0; streamChannel < stream.ChannelCount; streamChannel++)
                        {
                            bool isChannelEnabled = true;
                            if (_channelSettings != null && channelIndex < _channelSettings.Count)
                            {
                                isChannelEnabled = _channelSettings[channelIndex].IsEnabled;
                            }

                            if (isChannelEnabled && channelIndex < _maxChannels)
                            {
                                stream.CopyLatestTo(streamChannel, _data[channelIndex], Settings.Xmax);
                            }
                            
                            channelIndex++;
                        }
                    }
                }

                if (Settings.YAutoScale)
                    _plot.Plot.Axes.AutoScaleY();

                _plot.Refresh();

            }, System.Windows.Threading.DispatcherPriority.Render);
        }

        public void updateHorizontalScale(object? sender, int numberOfPointsToShow)
        {
            Settings.Xmax = numberOfPointsToShow;
            bool isRunning = _updatePlotTimer.Enabled;
            if (_updatePlotTimer.Enabled)
                _updatePlotTimer.Stop();
                
            for (int i = 0; i < _maxChannels; i++)
            {
                _data[i] = new double[Settings.Xmax];
                
                if (_signals[i] != null)
                {
                    ScottPlot.Color colorOld = _signals[i].Color;
                    _plot.Plot.Remove(_signals[i]);
                    _signals[i] = _plot.Plot.Add.Signal(_data[i]);
                    _signals[i].Color = colorOld;
                    _signals[i].LineWidth = (float)Settings.LineWidth;
                    _signals[i].MarkerShape = ScottPlot.MarkerShape.None;
                    _signals[i].LineStyle.AntiAlias = Settings.AntiAliasing;
                }
            }
            
            _plot.Plot.Axes.SetLimitsX(0, Settings.Xmax);
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

            _plot.MouseRightButtonUp += Plot_MouseRightButtonUp;

            ScottPlot.Interactivity.Key horizontalLock = ScottPlot.Interactivity.StandardKeys.Shift;
            ScottPlot.Interactivity.Key verticalLock= ScottPlot.Interactivity.StandardKeys.Control;

            ScottPlot.Interactivity.UserActionResponses.MouseWheelZoom wheelZoomResponse = new ScottPlot.Interactivity.UserActionResponses.MouseWheelZoom(horizontalLock, verticalLock);
            _plot.UserInputProcessor.UserActionResponses.Add(wheelZoomResponse);
        }

        private void Plot_MouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _plot.Plot.Axes.AutoScaleX();
            _plot.Refresh();
        }
    }
}
