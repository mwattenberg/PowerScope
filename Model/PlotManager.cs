using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using ScottPlot;
using ScottPlot.WPF;
using ScottPlot.Plottables;
using PowerScope.Model;
using Color = System.Windows.Media.Color;

namespace PowerScope.Model
{
    /// <summary>
    /// Manager class for ScottPlot integration in WPF
    /// Migrated to Channel-centric architecture - works directly with Channel objects
    /// that encapsulate both data streams and settings
    /// </summary>
    public class PlotManager : INotifyPropertyChanged, IDisposable
    {
        private readonly WpfPlotGL _plot;
        private readonly DispatcherTimer _updateTimer;
        private bool _disposed;
        private readonly Signal[] _signals;
        private readonly double[][] _data;
        private readonly int _maxChannels;
        private ObservableCollection<Channel> _channels;

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
            _channels = new ObservableCollection<Channel>();

            // Initialize update timer with correct interval from settings
            _updateTimer = new DispatcherTimer(DispatcherPriority.Render);
            _updateTimer.Interval = TimeSpan.FromMilliseconds(Settings.TimerInterval);
            _updateTimer.Tick += UpdatePlot;

            Settings.PropertyChanged += OnSettingsChanged;

            _maxChannels = maxChannels;
            _signals = new Signal[_maxChannels];
            _data = new double[_maxChannels][];
        }

        /// <summary>
        /// Sets the channels collection for the plot manager
        /// </summary>
        /// <param name="channels">Collection of channels to display</param>
        public void SetChannels(ObservableCollection<Channel> channels)
        {
            // Unsubscribe from old channels
            if (_channels != null)
            {
                _channels.CollectionChanged -= OnChannelsCollectionChanged;
                foreach (Channel channel in _channels)
                {
                    channel.PropertyChanged -= OnChannelPropertyChanged;
                }
            }

            _channels = channels ?? new ObservableCollection<Channel>();

            // Subscribe to new channels
            _channels.CollectionChanged += OnChannelsCollectionChanged;
            foreach (Channel channel in _channels)
            {
                channel.PropertyChanged += OnChannelPropertyChanged;
            }

            // Update the display
            UpdateChannelDisplay();
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
                        
                    case nameof(PlotSettings.BufferSize):
                        UpdateDataStreamBufferSizes();
                        break;
                        
                    case nameof(PlotSettings.TriggerModeEnabled):
                        // Handle trigger mode changes - for future trigger implementation
                        System.Diagnostics.Debug.WriteLine($"Trigger mode changed to: {Settings.TriggerModeEnabled}");
                        break;
                }
            });
        }

        private void OnChannelsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (Channel channel in e.NewItems)
                {
                    channel.PropertyChanged += OnChannelPropertyChanged;
                }
            }

            if (e.OldItems != null)
            {
                foreach (Channel channel in e.OldItems)
                {
                    channel.PropertyChanged -= OnChannelPropertyChanged;
                }
            }

            UpdateChannelDisplay();
        }

        private void OnChannelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                switch (e.PropertyName)
                {
                    case nameof(Channel.IsEnabled):
                    case "Settings.IsEnabled":
                        UpdateChannelDisplay();
                        break;
                        
                    case nameof(Channel.Color):
                    case "Settings.Color":
                        ApplyChannelColors();
                        break;
                        
                    // Other channel setting changes are handled by the Channel itself
                    // since it applies settings directly to its owner stream
                }
            });
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

        private void UpdateChannelDisplay()
        {
            if (_channels == null)
            {
                NumberOfChannels = 0;
                return;
            }

            NumberOfChannels = Math.Min(_channels.Count, _maxChannels);

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
                Channel channel = _channels[i];
                
                if (channel.IsEnabled)
                {
                    _signals[i] = _plot.Plot.Add.Signal(_data[i]);
                    
                    // Use channel color
                    Color channelColor = channel.Color;
                    _signals[i].Color = new ScottPlot.Color(channelColor.R, channelColor.G, channelColor.B);
                    _signals[i].MarkerShape = ScottPlot.MarkerShape.None;
                    _signals[i].LineWidth = (float)Settings.LineWidth;
                    _signals[i].LineStyle.AntiAlias = Settings.AntiAliasing;
                }
            }

            _plot.Refresh();
        }

        private void UpdatePlot(object? sender, EventArgs e)
        {
            if (Application.Current == null)
                return;

            _plot.Plot.RenderManager.EnableRendering = false;

            CopyChannelDataToPlot();

            if (Settings.YAutoScale)
                _plot.Plot.Axes.AutoScaleY();

            _plot.Plot.RenderManager.EnableRendering = true;
            _plot.Refresh();
        }

        private void CopyChannelDataToPlot()
        {
            if (_channels == null) 
                return;
            
            for (int i = 0; i < _channels.Count && i < _maxChannels; i++)
            {
                Channel channel = _channels[i];
                
                if (channel.IsEnabled)
                {
                    channel.CopyLatestDataTo(_data[i], Settings.Xmax);
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
            
            // Populate the new data arrays with current data from channels
            // This ensures the plot shows data even when the update timer is stopped
            CopyChannelDataToPlot();
            
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
            if (_channels == null) 
                return;

            for (int i = 0; i < NumberOfChannels && i < _maxChannels; i++)
            {
                if (_signals[i] != null && i < _channels.Count)
                {
                    Color channelColor = _channels[i].Color;
                    _signals[i].Color = new ScottPlot.Color(channelColor.R, channelColor.G, channelColor.B);
                }
            }
            
            _plot.Refresh();
        }

        /// <summary>
        /// Updates buffer sizes for all data streams that support it
        /// Called when PlotSettings.BufferSize changes
        /// </summary>
        private void UpdateDataStreamBufferSizes()
        {
            // Get unique data streams from channels
            var uniqueStreams = new HashSet<IDataStream>();
            foreach (Channel channel in _channels)
            {
                uniqueStreams.Add(channel.OwnerStream);
            }

            // Update buffer size for streams that support it
            foreach (IDataStream stream in uniqueStreams)
            {
                if (stream is IBufferResizable resizableStream)
                {

                    resizableStream.SetBufferSize(Settings.BufferSize);
                }
            }
        }

        /// <summary>
        /// Clears all data from connected data streams and updates the plot display
        /// Works regardless of whether the update timer is running or stopped
        /// </summary>
        public void Clear()
        {
            if (_channels == null)
                return;

            // Get unique data streams from channels
            var uniqueStreams = new HashSet<IDataStream>();
            foreach (Channel channel in _channels)
            {
                uniqueStreams.Add(channel.OwnerStream);
            }

            // Clear data from all streams
            foreach (IDataStream stream in uniqueStreams)
            {
                try
                {
                    stream.clearData();
                }
                catch (Exception ex)
                {
                    // Log error but don't crash the application
                    System.Diagnostics.Debug.WriteLine($"Failed to clear data for {stream.StreamType}: {ex.Message}");
                }
            }

            // Clear plot data arrays and update display immediately
            for (int i = 0; i < _maxChannels; i++)
            {
                if (_data[i] != null)
                {
                    Array.Clear(_data[i], 0, _data[i].Length);
                }
            }

            // Force plot refresh to show cleared data immediately, regardless of timer state
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

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                StopUpdates();
                
                // Unsubscribe from channels
                if (_channels != null)
                {
                    _channels.CollectionChanged -= OnChannelsCollectionChanged;
                    foreach (Channel channel in _channels)
                    {
                        channel.PropertyChanged -= OnChannelPropertyChanged;
                    }
                }
                
                if (Settings != null)
                {
                    Settings.PropertyChanged -= OnSettingsChanged;
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
