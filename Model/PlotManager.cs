using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using ScottPlot;
using ScottPlot.WPF;
using ScottPlot.Plottables;
using Color = System.Windows.Media.Color;

namespace PowerScope.Model
{
    /// <summary>
    /// Manager class for ScottPlot integration in WPF
    /// Migrated to Channel-centric architecture - works directly with Channel objects
    /// that encapsulate both data streams and settings
    /// Split into partial classes for better organization:
    /// - PlotManager.cs: Core functionality (this file)
    /// - PlotManager.Cursors.cs: Cursor management
    /// - PlotManager.Triggers.cs: Trigger functionality
    /// </summary>
    public partial class PlotManager : INotifyPropertyChanged, IDisposable
    {
        // Core fields
        private readonly WpfPlotGL _plot;
        private readonly DispatcherTimer _updateTimer;
        private bool _disposed;
        private readonly Signal[] _signals;
        private readonly double[][] _data;
        private readonly int _maxChannels;
        private ObservableCollection<Channel> _channels;

        // Recording functionality
        private readonly PlotFileWriter _fileWriter;

        internal PlotFileWriter FileWriter
        {
            get { return _fileWriter; }
        }

        // DPI scaling for mouse handling
        private DpiScale _dpi;

        public event PropertyChangedEventHandler PropertyChanged;

        public PlotSettings Settings { get; private set; }

        public WpfPlotGL Plot
        {
            get { return _plot; }
        }

        public int NumberOfChannels { get; private set; }

        /// <summary>
        /// Global function to get color from the same palette
        /// </summary>
        public static Color GetColor(int index)
        {
            if (index == 3)
            {
                return System.Windows.Media.Colors.LimeGreen;
            }

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

            // Initialize cursor (field declared in Cursors partial)
            _cursor = new Cursor();

            // Initialize update timer
            _updateTimer = new DispatcherTimer(DispatcherPriority.Render);
            _updateTimer.Interval = TimeSpan.FromMilliseconds(Settings.TimerInterval);
            _updateTimer.Tick += UpdatePlot;

            Settings.PropertyChanged += OnSettingsChanged;

            _maxChannels = maxChannels;
            _signals = new Signal[_maxChannels];
            _data = new double[_maxChannels][];

            // Initialize cursor state (properties declared in Cursors partial)
            ActiveCursorMode = CursorMode.None;
            HasActiveCursors = false;

            _dpi = VisualTreeHelper.GetDpi(Plot);

            // Initialize file writer
            _fileWriter = new PlotFileWriter();
            _fileWriter.Channels = _channels;
        }

        /// <summary>
        /// Sets the channels collection for the plot manager
        /// Also updates the cursor channel data automatically
        /// </summary>
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

            // Update cursor channel data automatically
            _cursor.UpdateChannelData(_channels);

            // Update writer channels
            if (_fileWriter != null)
            {
                _fileWriter.Channels = _channels;
            }

            // Update the display
            UpdateChannelDisplay();
        }

        #region Recording
        public bool IsRecording
        {
            get { return _fileWriter.IsRecording; }
        }

        public bool StartRecording(string filePath)
        {
            if (IsRecording)
                return false;

            _fileWriter.SampleRate = _channels[0].OwnerStream.SampleRate;
            _fileWriter.Channels = _channels;

            if (_fileWriter.StartRecording(filePath))
            {
                _fileWriter.WritePendingSamples();
                return true;
            }

            return false;
        }

        public void StopRecording()
        {
            if (!IsRecording)
                return;

            _fileWriter.StopRecording();
        }

        /// <summary>
        /// Returns a snapshot of the data currently visible on the plot.
        /// Returns null if no channels are available.
        /// </summary>
        public PlotSnapshot GetSnapshot()
        {
            if (_channels == null || _channels.Count == 0)
                return null;

            int count = Math.Min(_channels.Count, _maxChannels);
            double[][] data = new double[count][];

            for (int i = 0; i < count; i++)
            {
                data[i] = new double[Settings.Xmax];
                if (_channels[i].IsEnabled && _data[i] != null)
                    Array.Copy(_data[i], data[i], Settings.Xmax);
            }

            double sampleRate = _channels[0].OwnerStream.SampleRate;
            return new PlotSnapshot(data, _channels, sampleRate, DateTime.Now);
        }
        #endregion

        #region Event Handlers

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
                        HandleXmaxChangeForTrigger();
                        RebuildSignalsForNewXRange();
                        UpdateMeasurementWindowLength();
                        break;

                    case nameof(PlotSettings.LineWidth):
                    case nameof(PlotSettings.AntiAliasing):
                    case nameof(PlotSettings.ShowRenderTime):
                        ApplyVisualSettings();
                        break;

                    case nameof(PlotSettings.PlotUpdateRateFPS):
                    case nameof(PlotSettings.PlotUpdateRateFpsOption):
                        if (IsRunning)
                        {
                            _updateTimer.Interval = TimeSpan.FromMilliseconds(Settings.TimerInterval);
                        }
                        break;

                    case nameof(PlotSettings.BufferSize):
                        UpdateDataStreamBufferSizes();
                        break;

                    case nameof(PlotSettings.EnableEdgeTrigger):
                        HandleTriggerModeChange();
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

            _cursor.UpdateChannelData(_channels);
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
    }
});
        }

        private void RebuildSignalsForNewXRange()
        {
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

            CopyChannelDataToPlot();
            _plot.Plot.Axes.SetLimitsX(0, Settings.Xmax);
            _plot.Refresh();
        }

        private void UpdateMeasurementWindowLength()
        {
            if (_channels == null)
                return;

            foreach (Channel channel in _channels)
            {
                if (channel.Measurements != null)
                {
                    foreach (Measurement measurement in channel.Measurements)
                    {
                        try
                        {
                            measurement.MeasurementWindowLength = Settings.Xmax;
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Failed to update buffer size for measurement {measurement.TypeDisplayName} on channel {channel.Label}: {ex.Message}");
                        }
                    }
                }
            }
        }

        #endregion

        #region Initialization and Visuals

        public void InitializePlot()
        {
            _plot.Plot.Clear();
            _plot.Plot.Axes.Hairline(true);
            _plot.Plot.Add.Palette = new ScottPlot.Palettes.Tsitsulin();

            for (int i = 0; i < _maxChannels; i++)
            {
                _data[i] = new double[Settings.Xmax];
            }

            ApplyDarkTheme();
        }

        private void ApplyDarkTheme()
        {
            _plot.Plot.FigureBackground.Color = ScottPlot.Color.FromHex("#1f1f1f");
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

                    Color channelColor = channel.Color;
                    _signals[i].Color = new ScottPlot.Color(channelColor.R, channelColor.G, channelColor.B);
                    _signals[i].MarkerShape = ScottPlot.MarkerShape.None;
                    _signals[i].LineWidth = (float)Settings.LineWidth;
                    _signals[i].LineStyle.AntiAlias = Settings.AntiAliasing;
                }
            }

            _plot.Refresh();
        }

        public void SetupPlotUserInput()
        {
            _plot.UserInputProcessor.UserActionResponses.Clear();
            _plot.UserInputProcessor.IsEnabled = true;

            var panButton = ScottPlot.Interactivity.StandardMouseButtons.Left;
            var panResponse = new ScottPlot.Interactivity.UserActionResponses.MouseDragPan(panButton);
            _plot.UserInputProcessor.UserActionResponses.Add(panResponse);

            var zoomRectangleButton = ScottPlot.Interactivity.StandardMouseButtons.Middle;
            var zoomRectangleResponse = new ScottPlot.Interactivity.UserActionResponses.MouseDragZoomRectangle(zoomRectangleButton);
            _plot.UserInputProcessor.UserActionResponses.Add(zoomRectangleResponse);

            var autoscaleButton = ScottPlot.Interactivity.StandardMouseButtons.Right;
            var autoscaleResponse = new ScottPlot.Interactivity.UserActionResponses.SingleClickAutoscale(autoscaleButton);
            _plot.UserInputProcessor.UserActionResponses.Add(autoscaleResponse);

            ScottPlot.Interactivity.UserActionResponses.MouseWheelZoom wheelZoomResponse = new ScottPlot.Interactivity.UserActionResponses.MouseWheelZoom(ScottPlot.Interactivity.StandardKeys.Shift, ScottPlot.Interactivity.StandardKeys.Control);
            _plot.UserInputProcessor.UserActionResponses.Add(wheelZoomResponse);
        }

        #endregion

        #region Core Update Loop

        public void StartUpdates()
        {
            if (!IsRunning && !_disposed)
            {
                _updateTimer.Interval = TimeSpan.FromMilliseconds(Settings.TimerInterval);
                _updateTimer.Start();
                IsRunning = true;
                OnPropertyChanged(nameof(IsRunning));
            }
        }

        public void StopUpdates()
        {
            if (IsRunning)
            {
                _updateTimer.Stop();
                IsRunning = false;
                OnPropertyChanged(nameof(IsRunning));
            }
        }

        private void UpdatePlot(object? sender, EventArgs e)
        {
            if (Application.Current == null)
                return;

            _plot.Plot.RenderManager.EnableRendering = false;

            bool shouldUpdatePlot;

            if (Settings.EnableEdgeTrigger)
            {
                shouldUpdatePlot = CheckTriggerCondition();
            }
            else
            {
                shouldUpdatePlot = true;
            }

            if (shouldUpdatePlot)
            {
                CopyChannelDataToPlot();

                if (_fileWriter != null && _fileWriter.IsRecording)
                {
                    _fileWriter.WritePendingSamples();
                }

                if (Settings.YAutoScale)
                    _plot.Plot.Axes.AutoScaleY();

                _plot.Plot.RenderManager.EnableRendering = true;
                _plot.Refresh();
            }
            else
            {
                _plot.Plot.RenderManager.EnableRendering = true;
            }
        }

        private void CopyChannelDataToPlot()
        {
            if (_channels == null)
                return;

            if (Settings.EnableEdgeTrigger && _triggerSampleIndex >= 0)
            {
                // Use the new over-fetch and shift approach for trigger alignment
                // This works with ALL stream types using only CopyLatestTo()
                CopyChannelDataWithTriggerAlignment();
            }
            else
            {
                // Normal mode: just copy latest data
                for (int i = 0; i < _channels.Count && i < _maxChannels; i++)
                {
                    Channel channel = _channels[i];

                    if (channel.IsEnabled)
                    {
                        channel.CopyLatestDataTo(_data[i], Settings.Xmax);
                    }
                }
            }
            //Not sure about this. I think Claude misunderstanded the logic here.
            //_triggerSampleIndex = -1;
        }

        #endregion

        #region Utilities

        private void UpdateDataStreamBufferSizes()
        {
            var uniqueStreams = new HashSet<IDataStream>();
            foreach (Channel channel in _channels)
            {
                uniqueStreams.Add(channel.OwnerStream);
            }

            foreach (IDataStream stream in uniqueStreams)
            {
                if (stream is IBufferResizable resizableStream)
                {
                    resizableStream.BufferSize = Settings.BufferSize;
                }
            }
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public double? GetPlotDataAt(Channel channel, int sampleIndex)
        {
            if (channel == null || _channels == null)
                return null;

            int channelIndex = -1;
            for (int i = 0; i < _channels.Count && i < _maxChannels; i++)
            {
                if (_channels[i] == channel)
                {
                    channelIndex = i;
                    break;
                }
            }

            if (channelIndex < 0)
                return null;

            if (!channel.IsEnabled || _data[channelIndex] == null)
                return null;

            if (sampleIndex >= 0 && sampleIndex < _data[channelIndex].Length)
                return _data[channelIndex][sampleIndex];

            return null;
        }

        #endregion

        #region Cleanup

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;

                StopRecording();
                DisableCursors();
                _cursor?.Dispose();
                StopUpdates();

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

                _fileWriter?.Dispose();
            }
        }

        public void Clear()
        {
            if (_channels == null)
                return;

            var uniqueStreams = new HashSet<IDataStream>();
            foreach (Channel channel in _channels)
            {
                uniqueStreams.Add(channel.OwnerStream);
            }

            foreach (IDataStream stream in uniqueStreams)
            {
                try
                {
                    stream.clearData();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to clear data for {stream.StreamType}: {ex.Message}");
                }
            }

            for (int i = 0; i < _maxChannels; i++)
            {
                if (_data[i] != null)
                {
                    Array.Clear(_data[i], 0, _data[i].Length);
                }
            }

            _plot.Refresh();
        }

        #endregion
    }
}
