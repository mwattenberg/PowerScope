using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using ScottPlot;
using ScottPlot.WPF;
using ScottPlot.Plottables;
using SerialPlotDN_WPF.Model;
using Color = System.Windows.Media.Color;

namespace SerialPlotDN_WPF.Model
{
    /// <summary>
    /// Manager class for ScottPlot integration in WPF
    /// Extended to bind directly to channel settings with automatic updates
    /// Now manages its own update timer
    /// </summary>
    public class PlotManager : INotifyPropertyChanged, IDisposable
    {
        private readonly WpfPlotGL _plot;
        private readonly DispatcherTimer _updateTimer;
        private bool _disposed;
        private readonly Signal[] _signals;
        private readonly double[][] _data;
        private readonly int _maxChannels;
        private List<IDataStream> _connectedStreams;
        private ObservableCollection<ChannelSettings> _channelSettings;

        public PlotSettings Settings { get; private set; }
        
        public WpfPlotGL Plot 
        { 
            get { return _plot; } 
        }

        public int NumberOfChannels { get; private set; }

        /// <summary>
        /// Gets a color from the ScottPlot Category10 palette
        /// </summary>
        /// <param name="index">Index of the color to retrieve</param>
        /// <returns>WPF Color from the palette</returns>
        public static Color GetColor(int index)
        {
            IPalette palette = new ScottPlot.Palettes.Tsitsulin();
            ScottPlot.Color scottPlotColor = palette.GetColor(index);
            return Color.FromArgb(scottPlotColor.A, scottPlotColor.R, scottPlotColor.G, scottPlotColor.B);
        }

        /// <summary>
        /// Whether plot updates are currently running
        /// </summary>
        public bool IsRunning { get; private set; }

        public PlotManager(WpfPlotGL wpfPlot, int maxChannels = 16)
        {                
            _plot = wpfPlot;
            Settings = new PlotSettings();
            _connectedStreams = new List<IDataStream>();
            _channelSettings = new ObservableCollection<ChannelSettings>();

            
            // Initialize update timer with correct interval from settings
            _updateTimer = new DispatcherTimer(DispatcherPriority.Render);
            _updateTimer.Interval = TimeSpan.FromMilliseconds(Settings.TimerInterval); // Use settings, not hardcoded!
            _updateTimer.Tick += UpdatePlot;

            Settings.PropertyChanged += OnSettingsChanged;

            _maxChannels = maxChannels;
            _signals = new Signal[_maxChannels];
            _data = new double[_maxChannels][];
        }



        /// <summary>
        /// Start plot updates
        /// </summary>
        public void StartUpdates()
        {
            if (!IsRunning && !_disposed)
            {
                // Update timer interval from settings
                _updateTimer.Interval = TimeSpan.FromMilliseconds(Settings.TimerInterval);
                _updateTimer.Start();
                IsRunning = true;
                OnPropertyChanged(nameof(IsRunning));
            }
        }

        /// <summary>
        /// Stop plot updates
        /// </summary>
        public void StopUpdates()
        {
            if (IsRunning)
            {
                _updateTimer.Stop();
                IsRunning = false;
                OnPropertyChanged(nameof(IsRunning));
            }
        }

        private void OnSettingsChanged(object sender, PropertyChangedEventArgs e)
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
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
                        
                    case nameof(PlotSettings.PlotUpdateRateFPS):
                    case nameof(PlotSettings.PlotUpdateRateFpsOption):
                        // Update timer interval if it changed
                        if (IsRunning)
                        {
                            _updateTimer.Interval = TimeSpan.FromMilliseconds(Settings.TimerInterval);
                        }
                        break;
                }
            });
        }

        public void SetChannelSettings(ObservableCollection<ChannelSettings> channelSettings)
        {

            _channelSettings.CollectionChanged -= OnChannelSettingsCollectionChanged;
            foreach (ChannelSettings setting in _channelSettings)
            {
                setting.PropertyChanged -= OnChannelSettingChanged;
            }


            _channelSettings = channelSettings;


            _channelSettings.CollectionChanged += OnChannelSettingsCollectionChanged;
            foreach (ChannelSettings setting in _channelSettings)
            {
                setting.PropertyChanged += OnChannelSettingChanged;
            }

            
            UpdateDataStreamChannelSettings();
        }

        private void OnChannelSettingsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
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

        private void OnChannelSettingChanged(object? sender, PropertyChangedEventArgs e)
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
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
            _plot.Plot.Add.Palette = new ScottPlot.Palettes.Tsitsulin();
            
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
                bool isChannelEnabled = true;
                if (_channelSettings != null && i < _channelSettings.Count && _channelSettings[i] != null)
                    isChannelEnabled = _channelSettings[i].IsEnabled;

                if (isChannelEnabled)
                {
                    _signals[i] = _plot.Plot.Add.Signal(_data[i]);
                    
                    // Get color from ChannelSettings or fallback to palette
                    Color channelColor = GetColor(i);
                    if (_channelSettings != null && i < _channelSettings.Count && _channelSettings[i] != null)
                        channelColor = _channelSettings[i].Color;
                    
                    _signals[i].Color = new ScottPlot.Color(channelColor.R, channelColor.G, channelColor.B);
                    _signals[i].MarkerShape = ScottPlot.MarkerShape.None;
                    _signals[i].LineWidth = (float)Settings.LineWidth;
                    _signals[i].LineStyle.AntiAlias = Settings.AntiAliasing;
                }
            }

            _plot.Refresh();
        }

        // Update the UpdatePlot method signature to accept nullable sender
        public void UpdatePlot(object? sender, EventArgs e)
        {
            if (Application.Current == null)
                return;

            _plot.Plot.RenderManager.EnableRendering = false;

            CopyStreamDataToPlot();

            if (Settings.YAutoScale)
                _plot.Plot.Axes.AutoScaleY();

            _plot.Plot.RenderManager.EnableRendering = true;
            _plot.Refresh();
        }

        private void CopyStreamDataToPlot()
        {
            if (_connectedStreams == null) 
                return;
            
            int channelIndex = 0;
            foreach (IDataStream stream in _connectedStreams)
            {
                for (int streamChannel = 0; streamChannel < stream.ChannelCount; streamChannel++)
                {
                    bool isChannelEnabled = true;
                    if (channelIndex < _channelSettings.Count && _channelSettings[channelIndex] != null)
                        isChannelEnabled = _channelSettings[channelIndex].IsEnabled;

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

        public void SetupPlotUserInput()
        {
            // Clear existing input handlers
            _plot.UserInputProcessor.UserActionResponses.Clear();
            _plot.UserInputProcessor.IsEnabled = true;
            
            // Left-click-drag pan
            var panButton = ScottPlot.Interactivity.StandardMouseButtons.Left;
            var panResponse = new ScottPlot.Interactivity.UserActionResponses.MouseDragPan(panButton);
            _plot.UserInputProcessor.UserActionResponses.Add(panResponse);

            // Middle-click-drag zoom rectangle
            var zoomRectangleButton = ScottPlot.Interactivity.StandardMouseButtons.Middle;
            var zoomRectangleResponse = new ScottPlot.Interactivity.UserActionResponses.MouseDragZoomRectangle(zoomRectangleButton);
            _plot.UserInputProcessor.UserActionResponses.Add(zoomRectangleResponse);

            // Right-click auto-scale
            var autoscaleButton = ScottPlot.Interactivity.StandardMouseButtons.Right;
            var autoscaleResponse = new ScottPlot.Interactivity.UserActionResponses.SingleClickAutoscale(autoscaleButton);
            _plot.UserInputProcessor.UserActionResponses.Add(autoscaleResponse);

            // Mouse wheel zoom with modifier keys
            ScottPlot.Interactivity.UserActionResponses.MouseWheelZoom wheelZoomResponse = new ScottPlot.Interactivity.UserActionResponses.MouseWheelZoom(ScottPlot.Interactivity.StandardKeys.Shift, ScottPlot.Interactivity.StandardKeys.Control);
            _plot.UserInputProcessor.UserActionResponses.Add(wheelZoomResponse);
        }

        private void UpdateDataStreamChannelSettings()
        {
            if (_connectedStreams == null || _channelSettings == null)
                return;

            int globalChannelIndex = 0;
            
            foreach (IDataStream stream in _connectedStreams)
            {
                IChannelConfigurable configurableStream = stream as IChannelConfigurable;
                if (configurableStream != null)
                {
                    List<ChannelSettings> streamChannelSettings = new List<ChannelSettings>();
                    
                    for (int streamChannel = 0; streamChannel < stream.ChannelCount; streamChannel++)
                    {
                        if (globalChannelIndex < _channelSettings.Count)
                            streamChannelSettings.Add(_channelSettings[globalChannelIndex]);
                        else
                            streamChannelSettings.Add(new ChannelSettings());

                        globalChannelIndex++;
                    }
                    
                    configurableStream.UpdateChannelSettings(streamChannelSettings);
                }
                else
                    globalChannelIndex += stream.ChannelCount;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                StopUpdates();
                
                // DispatcherTimer doesn't have Dispose method, just stop it
                if (Settings != null)
                {
                    Settings.PropertyChanged -= OnSettingsChanged;
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
