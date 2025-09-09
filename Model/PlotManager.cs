using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using ScottPlot;
using ScottPlot.Plottables;
using ScottPlot.WPF;
using Color = System.Windows.Media.Color;

namespace SerialPlotDN_WPF.Model
{
    public class PlotManager
    {
        private readonly WpfPlotGL _plot;
        private readonly Signal[] _signals;
        private readonly double[][] _data;
        private readonly int _maxChannels;
        private List<IDataStream> _connectedStreams;
        private ObservableCollection<ChannelSettings> _channelSettings;

        public PlotSettings Settings { get; private set; }
        public WpfPlotGL Plot => _plot;
        public int NumberOfChannels { get; private set; } = 0;

        /// <summary>
        /// Gets a color from the ScottPlot Category10 palette
        /// </summary>
        /// <param name="index">Index of the color to retrieve</param>
        /// <returns>WPF Color from the palette</returns>
        public static Color GetColor(int index)
        {
            IPalette palette = new ScottPlot.Palettes.Tsitsulin();
            //ScottPlot.Palettes.Penumbra palette = new ScottPlot.Palettes.Penumbra();
            ScottPlot.Color scottPlotColor = palette.GetColor(index);
            return Color.FromArgb(scottPlotColor.A, scottPlotColor.R, scottPlotColor.G, scottPlotColor.B);
        }

        public PlotManager(WpfPlotGL wpfPlot, int maxChannels = 12)
        {
            _plot = wpfPlot;
            _maxChannels = maxChannels;
            _signals = new Signal[_maxChannels];
            _data = new double[_maxChannels][];
            
            Settings = new PlotSettings();
            Settings.PropertyChanged += OnSettingsChanged;
        }

        private void OnSettingsChanged(object sender, PropertyChangedEventArgs e)
        {
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                switch (e.PropertyName)
                {
                    case nameof(PlotSettings.Ymin):
                    case nameof(PlotSettings.Ymax):
                        _plot.Plot.Axes.SetLimitsY(Settings.Ymin, Settings.Ymax);
                        _plot.Refresh();
                        break;
                        
                    case nameof(PlotSettings.Xmax):
                        RebuildSignalsForNewXRange();
                        break;
                        
                    case nameof(PlotSettings.LineWidth):
                    case nameof(PlotSettings.AntiAliasing):
                    case nameof(PlotSettings.ShowRenderTime):
                        ApplyVisualSettings();
                        break;
                }
            });
        }

        public void SetChannelSettings(ObservableCollection<ChannelSettings> channelSettings)
        {
            // Unsubscribe from old settings
            if (_channelSettings != null)
            {
                _channelSettings.CollectionChanged -= OnChannelSettingsCollectionChanged;
                foreach (ChannelSettings setting in _channelSettings)
                {
                    setting.PropertyChanged -= OnChannelSettingChanged;
                }
            }

            _channelSettings = channelSettings;

            // Subscribe to new settings
            if (_channelSettings != null)
            {
                _channelSettings.CollectionChanged += OnChannelSettingsCollectionChanged;
                foreach (ChannelSettings setting in _channelSettings)
                {
                    setting.PropertyChanged += OnChannelSettingChanged;
                }
            }
            
            UpdateDataStreamChannelSettings();
        }

        private void OnChannelSettingsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (ChannelSettings setting in e.NewItems)
                {
                    setting.PropertyChanged += OnChannelSettingChanged;
                }
            }

            if (e.OldItems != null)
            {
                foreach (ChannelSettings setting in e.OldItems)
                {
                    setting.PropertyChanged -= OnChannelSettingChanged;
                }
            }
        }

        private void OnChannelSettingChanged(object sender, PropertyChangedEventArgs e)
        {
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                switch (e.PropertyName)
                {
                    case nameof(ChannelSettings.IsEnabled):
                        UpdateChannelDisplay(NumberOfChannels);
                        break;
                        
                    case nameof(ChannelSettings.Color):
                        ApplyChannelColors();
                        break;
                        
                    case nameof(ChannelSettings.Gain):
                    case nameof(ChannelSettings.Offset):
                    case nameof(ChannelSettings.Filter):
                        UpdateDataStreamChannelSettings();
                        break;
                }
            });
        }

        public void SetDataStreams(List<IDataStream> connectedStreams)
        {
            _connectedStreams = connectedStreams;
            UpdateDataStreamChannelSettings();
        }

        public void InitializePlot()
        {
            _plot.Plot.Clear();
            _plot.Plot.Add.Palette = new ScottPlot.Palettes.Category10();
            
            // Initialize data arrays
            for (int i = 0; i < _maxChannels; i++)
            {
                _data[i] = new double[Settings.Xmax];
            }

            // Apply dark theme
            ApplyDarkTheme();
        }

        private void ApplyDarkTheme()
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

        public void UpdateChannelDisplay(int channelCount)
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
                bool isChannelEnabled = _channelSettings?[i]?.IsEnabled ?? true;

                if (isChannelEnabled)
                {
                    _signals[i] = _plot.Plot.Add.Signal(_data[i]);
                    
                    // Get color from ChannelSettings or fallback to palette
                    Color channelColor = _channelSettings?[i]?.Color ?? GetColor(i);
                    
                    _signals[i].Color = new ScottPlot.Color(channelColor.R, channelColor.G, channelColor.B);
                    _signals[i].MarkerShape = ScottPlot.MarkerShape.None;
                    _signals[i].LineWidth = (float)Settings.LineWidth;
                    _signals[i].LineStyle.AntiAlias = Settings.AntiAliasing;
                }
            }

            _plot.Refresh();
        }

        /// <summary>
        /// Update the plot - called by SystemManager
        /// </summary>
        public void UpdatePlot()
        {
            if (Application.Current == null)
                return;

            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                _plot.Plot.RenderManager.EnableRendering = false;
                
                CopyStreamDataToPlot();
                
                if (Settings.YAutoScale)
                    _plot.Plot.Axes.AutoScaleY();
                
                _plot.Plot.RenderManager.EnableRendering = true;
                _plot.Refresh();
                
            }, System.Windows.Threading.DispatcherPriority.Render);
        }

        private void CopyStreamDataToPlot()
        {
            if (_connectedStreams == null) return;
            
            int channelIndex = 0;
            foreach (IDataStream stream in _connectedStreams)
            {
                for (int streamChannel = 0; streamChannel < stream.ChannelCount; streamChannel++)
                {
                    bool isChannelEnabled = _channelSettings?[channelIndex]?.IsEnabled ?? true;

                    if (isChannelEnabled && channelIndex < _maxChannels)
                    {
                        stream.CopyLatestTo(streamChannel, _data[channelIndex], Settings.Xmax);
                    }
                    
                    channelIndex++;
                }
            }
        }

        private void RebuildSignalsForNewXRange()
        {
            // Recreate data arrays with new size
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
            _plot.Refresh();
        }

        private void ApplyVisualSettings()
        {
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

        private void ApplyChannelColors()
        {
            if (_channelSettings == null) return;

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

        public void SetupPlotUserInput()
        {
            _plot.UserInputProcessor.RemoveAll<ScottPlot.Interactivity.IUserActionResponse>();
            _plot.UserInputProcessor.IsEnabled = true;
            
            // Left-click drag zoom rectangle
            var zoomRectangleResponse = new ScottPlot.Interactivity.UserActionResponses.MouseDragZoomRectangle(
                ScottPlot.Interactivity.StandardMouseButtons.Left);
            _plot.UserInputProcessor.UserActionResponses.Add(zoomRectangleResponse);

            // Mouse wheel zoom with modifier keys
            var wheelZoomResponse = new ScottPlot.Interactivity.UserActionResponses.MouseWheelZoom(
                ScottPlot.Interactivity.StandardKeys.Shift, 
                ScottPlot.Interactivity.StandardKeys.Control);
            _plot.UserInputProcessor.UserActionResponses.Add(wheelZoomResponse);

            // Right-click auto-scale X
            _plot.MouseRightButtonUp += (sender, e) =>
            {
                _plot.Plot.Axes.AutoScaleX();
                _plot.Refresh();
            };
        }

        private void UpdateDataStreamChannelSettings()
        {
            if (_connectedStreams == null || _channelSettings == null)
                return;

            int globalChannelIndex = 0;
            
            foreach (IDataStream stream in _connectedStreams)
            {
                if (stream is IChannelConfigurable configurableStream)
                {
                    var streamChannelSettings = new List<ChannelSettings>();
                    
                    for (int streamChannel = 0; streamChannel < stream.ChannelCount; streamChannel++)
                    {
                        if (globalChannelIndex < _channelSettings.Count)
                        {
                            streamChannelSettings.Add(_channelSettings[globalChannelIndex]);
                        }
                        else
                        {
                            streamChannelSettings.Add(new ChannelSettings());
                        }
                        globalChannelIndex++;
                    }
                    
                    configurableStream.UpdateChannelSettings(streamChannelSettings);
                }
                else
                {
                    globalChannelIndex += stream.ChannelCount;
                }
            }
        }

    }
}
