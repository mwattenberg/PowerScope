﻿using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ScottPlot;
using ScottPlot.WPF;
using ScottPlot.Plottables;
using PowerScope.Model;
using Color = System.Windows.Media.Color;
using System.IO;
using System.Collections.Generic;

namespace PowerScope.Model
{
    /// <summary>
    /// Manager class for ScottPlot integration in WPF
    /// Migrated to Channel-centric architecture - works directly with Channel objects
    /// that encapsulate both data streams and settings
    /// Now owns and manages cursor functionality for proper separation of concerns
    /// Also handles data recording functionality
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

        // Recording functionality
        private bool _isRecording = false;
        private string _recordingFilePath = "";
        private StreamWriter _recordingWriter = null;
        private readonly object _recordingLock = new object();
        private long _recordingSampleCount = 0;

        // Cursor management
        private readonly Cursor _cursor;
        private HorizontalLine _horizontalCursorA;
        private HorizontalLine _horizontalCursorB;
        private VerticalLine _verticalCursorA;
        private VerticalLine _verticalCursorB;
        private AxisLine _plottableBeingDragged;
        private bool _cursorMouseHandlingEnabled;

        public PlotSettings Settings { get; private set; }
        
        public WpfPlotGL Plot 
        { 
            get { return _plot; } 
        }

        public int NumberOfChannels { get; private set; }

        /// <summary>
        /// The cursor instance owned by this PlotManager
        /// </summary>
        public Cursor Cursor => _cursor;

        /// <summary>
        /// Whether cursors are currently active
        /// </summary>
        public bool HasActiveCursors { get; private set; }

        /// <summary>
        /// Current active cursor mode
        /// </summary>
        public CursorMode ActiveCursorMode { get; private set; }

        /// <summary>
        /// Gets a color from the ScottPlot Tsitsulin palette
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

        /// <summary>
        /// Whether recording is currently active
        /// </summary>
        public bool IsRecording 
        { 
            get => _isRecording;
            private set
            {
                if (_isRecording != value)
                {
                    _isRecording = value;
                    OnPropertyChanged(nameof(IsRecording));
                }
            }
        }

        /// <summary>
        /// Current recording file path
        /// </summary>
        public string RecordingFilePath => _recordingFilePath;

        /// <summary>
        /// Number of samples recorded so far
        /// </summary>
        public long RecordingSampleCount => _recordingSampleCount;

        public PlotManager(WpfPlotGL wpfPlot, int maxChannels = 16)
        {                
            _plot = wpfPlot;
            Settings = new PlotSettings();
            _channels = new ObservableCollection<Channel>();

            // Initialize cursor - PlotManager owns it
            _cursor = new Cursor();

            // Initialize update timer with correct interval from settings
            _updateTimer = new DispatcherTimer(DispatcherPriority.Render);
            _updateTimer.Interval = TimeSpan.FromMilliseconds(Settings.TimerInterval);
            _updateTimer.Tick += UpdatePlot;

            Settings.PropertyChanged += OnSettingsChanged;

            _maxChannels = maxChannels;
            _signals = new Signal[_maxChannels];
            _data = new double[_maxChannels][];

            // Initialize cursor state
            ActiveCursorMode = CursorMode.None;
            HasActiveCursors = false;
        }

        /// <summary>
        /// Enables vertical cursors - no parameter needed since PlotManager owns the cursor
        /// </summary>
        public void EnableVerticalCursors()
        {
            DisableCursors(); // Remove any existing cursors
            CreateVerticalCursors();
            SetupCursorMouseHandling();
            ActiveCursorMode = CursorMode.Vertical;
            HasActiveCursors = true;
            _cursor.ActiveMode = CursorMode.Vertical;
            UpdateVerticalCursorData();
            _plot.Refresh();
            OnPropertyChanged(nameof(HasActiveCursors));
            OnPropertyChanged(nameof(ActiveCursorMode));
        }

        /// <summary>
        /// Enables horizontal cursors - no parameter needed since PlotManager owns the cursor
        /// </summary>
        public void EnableHorizontalCursors()
        {
            DisableCursors(); // Remove any existing cursors
            CreateHorizontalCursors();
            SetupCursorMouseHandling();
            ActiveCursorMode = CursorMode.Horizontal;
            HasActiveCursors = true;
            _cursor.ActiveMode = CursorMode.Horizontal;
            UpdateHorizontalCursorData();
            _plot.Refresh();
            OnPropertyChanged(nameof(HasActiveCursors));
            OnPropertyChanged(nameof(ActiveCursorMode));
        }

        /// <summary>
        /// Disables all cursors and cleans up cursor state
        /// </summary>
        public void DisableCursors()
        {
            RemoveAllCursor();
            RemoveCursorMouseHandling();
            ActiveCursorMode = CursorMode.None;
            HasActiveCursors = false;
            _cursor.ActiveMode = CursorMode.None;
            _plot.Refresh();
            OnPropertyChanged(nameof(HasActiveCursors));
            OnPropertyChanged(nameof(ActiveCursorMode));
        }

        private void CreateVerticalCursors()
        {
            // Place lines at 25% and 75% of current x-axis span
            var xAxisRange = _plot.Plot.Axes.GetXAxes().First().Range;
            double x1 = xAxisRange.Min + (xAxisRange.Max - xAxisRange.Min) * 0.25;
            double x2 = xAxisRange.Min + (xAxisRange.Max - xAxisRange.Min) * 0.75;

            // Get the highlight colors from App.xaml resources
            var highlightNormal = (Color)Application.Current.Resources["Highlight_Normal"];
            var highlightComplementary = (Color)Application.Current.Resources["Highlight_Complementary"];

            _verticalCursorA = _plot.Plot.Add.VerticalLine(x1);
            _verticalCursorA.IsDraggable = true;
            _verticalCursorA.Text = "A";
            _verticalCursorA.Color = new ScottPlot.Color(highlightNormal.R, highlightNormal.G, highlightNormal.B);

            _verticalCursorB = _plot.Plot.Add.VerticalLine(x2);
            _verticalCursorB.IsDraggable = true;
            _verticalCursorB.Text = "B";
            _verticalCursorB.Color = new ScottPlot.Color(highlightComplementary.R, highlightComplementary.G, highlightComplementary.B);
        }

        private void CreateHorizontalCursors()
        {
            // Place lines at 25% and 75% of current y-axis span
            var limits = _plot.Plot.Axes.GetYAxes().First().Range;
            double y1 = limits.Min + (limits.Max - limits.Min) * 0.25;
            double y2 = limits.Min + (limits.Max - limits.Min) * 0.75;

            // Get the highlight colors from App.xaml resources
            var highlightNormal = (Color)Application.Current.Resources["Highlight_Normal"];
            var highlightComplementary = (Color)Application.Current.Resources["Highlight_Complementary"];

            _horizontalCursorA = _plot.Plot.Add.HorizontalLine(y1);
            _horizontalCursorA.IsDraggable = true;
            _horizontalCursorA.Text = "A";
            _horizontalCursorA.Color = new ScottPlot.Color(highlightNormal.R, highlightNormal.G, highlightNormal.B);

            _horizontalCursorB = _plot.Plot.Add.HorizontalLine(y2);
            _horizontalCursorB.IsDraggable = true;
            _horizontalCursorB.Text = "B";
            _horizontalCursorB.Color = new ScottPlot.Color(highlightComplementary.R, highlightComplementary.G, highlightComplementary.B);
        }

        private void RemoveAllCursor()
        {
            RemoveVerticalCursor();
            RemoveHorizontalCursor();
        }

        private void RemoveVerticalCursor()
        {
            if (_verticalCursorA != null)
            {
                _plot.Plot.Remove(_verticalCursorA);
                _verticalCursorA = null;
            }
            if (_verticalCursorB != null)
            {
                _plot.Plot.Remove(_verticalCursorB);
                _verticalCursorB = null;
            }
        }

        private void RemoveHorizontalCursor()
        {
            if (_horizontalCursorA != null)
            {
                _plot.Plot.Remove(_horizontalCursorA);
                _horizontalCursorA = null;
            }
            if (_horizontalCursorB != null)
            {
                _plot.Plot.Remove(_horizontalCursorB);
                _horizontalCursorB = null;
            }
        }

        private void SetupCursorMouseHandling()
        {
            if (!_cursorMouseHandlingEnabled)
            {
                _plot.MouseDown += Plot_MouseDown;
                _plot.MouseUp += Plot_MouseUp;
                _plot.MouseMove += Plot_MouseMove;
                _cursorMouseHandlingEnabled = true;
            }
        }

        private void RemoveCursorMouseHandling()
        {
            if (_cursorMouseHandlingEnabled)
            {
                _plot.MouseDown -= Plot_MouseDown;
                _plot.MouseUp -= Plot_MouseUp;
                _plot.MouseMove -= Plot_MouseMove;
                _cursorMouseHandlingEnabled = false;
            }
        }

        private void Plot_MouseDown(object sender, MouseButtonEventArgs e)
        {
            Point pos = e.GetPosition(_plot);
            AxisLine line = GetLineUnderMouse((float)pos.X, (float)pos.Y);
            if (line != null)
            {
                _plottableBeingDragged = line;
                _plot.UserInputProcessor.Disable();
                e.Handled = true;
            }
        }

        private void Plot_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _plottableBeingDragged = null;
            _plot.UserInputProcessor.Enable();
            _plot.Refresh();
            Mouse.OverrideCursor = null;
        }

        private void Plot_MouseMove(object sender, MouseEventArgs e)
        {
            Point pos = e.GetPosition(_plot);
            var rect = _plot.Plot.GetCoordinateRect((float)pos.X, (float)pos.Y, radius: 10);

            if (_plottableBeingDragged == null)
            {
                AxisLine lineUnderMouse = GetLineUnderMouse((float)pos.X, (float)pos.Y);
                if (lineUnderMouse == null)
                    Mouse.OverrideCursor = null;
                else if (lineUnderMouse.IsDraggable && lineUnderMouse is VerticalLine)
                    Mouse.OverrideCursor = Cursors.SizeWE;
                else if (lineUnderMouse.IsDraggable && lineUnderMouse is HorizontalLine)
                    Mouse.OverrideCursor = Cursors.SizeNS;
            }
            else
            {
                if (_plottableBeingDragged is HorizontalLine horizontalLine)
                {
                    horizontalLine.Y = rect.VerticalCenter;
                    horizontalLine.Text = $"{horizontalLine.Y:0.0}";
                    UpdateHorizontalCursorData();
                }
                else if (_plottableBeingDragged is VerticalLine verticalLine)
                {
                    verticalLine.X = rect.HorizontalCenter;
                    verticalLine.Text = $"{verticalLine.X:0}";
                    UpdateVerticalCursorData();
                }
                _plot.Refresh();
                e.Handled = true;
            }
        }

        private AxisLine GetLineUnderMouse(float x, float y)
        {
            var rect = _plot.Plot.GetCoordinateRect(x, y, radius: 10);
            foreach (AxisLine axLine in _plot.Plot.GetPlottables<AxisLine>().Reverse())
            {
                if (axLine.IsUnderMouse(rect))
                    return axLine;
            }
            return null;
        }

        /// <summary>
        /// Updates the cursor model with current vertical cursor positions
        /// </summary>
        private void UpdateVerticalCursorData()
        {
            if (_verticalCursorA == null || _verticalCursorB == null)
                return;

            double cursorASample = _verticalCursorA.X;
            double cursorBSample = _verticalCursorB.X;
            double sampleRate = GetCurrentSampleRate();

            _cursor.UpdateVerticalCursors(cursorASample, cursorBSample, sampleRate);
            
            // Update channel values directly - much simpler since we have direct access
            _cursor.UpdateChannelValues(this);
        }

        /// <summary>
        /// Updates the cursor model with current horizontal cursor positions
        /// </summary>
        private void UpdateHorizontalCursorData()
        {
            if (_horizontalCursorA == null || _horizontalCursorB == null)
                return;

            double cursorAYValue = _horizontalCursorA.Y;
            double cursorBYValue = _horizontalCursorB.Y;

            _cursor.UpdateHorizontalCursors(cursorAYValue, cursorBYValue);
        }

        /// <summary>
        /// Sets the channels collection for the plot manager
        /// Also updates the cursor channel data automatically
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

            // Update cursor channel data automatically
            _cursor.UpdateChannelData(_channels);

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

        /// <summary>
        /// Starts recording data to the specified file
        /// </summary>
        /// <param name="filePath">Path to the output CSV file</param>
        /// <returns>True if recording started successfully</returns>
        public bool StartRecording(string filePath)
        {
            if (IsRecording)
                return false;

            try
            {
                lock (_recordingLock)
                {
                    _recordingFilePath = filePath;
                    _recordingWriter = new StreamWriter(filePath);
                    
                    // Write CSV header with channel labels and sample rate info
                    WriteRecordingHeader();
                    
                    _recordingSampleCount = 0;
                    IsRecording = true;
                }
                
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to start recording: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Stops recording and closes the file
        /// </summary>
        public void StopRecording()
        {
            if (!IsRecording)
                return;
                
            lock (_recordingLock)
            {
                try
                {
                    _recordingWriter?.Flush();
                    _recordingWriter?.Close();
                    _recordingWriter?.Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error stopping recording: {ex.Message}");
                }
                finally
                {
                    _recordingWriter = null;
                    IsRecording = false;
                    _recordingFilePath = "";
                }
            }
        }

        private void WriteRecordingHeader()
        {
            if (_recordingWriter == null || _channels == null)
                return;

            var headerParts = new List<string>();
            
            // Add metadata as comments with version information
            _recordingWriter.WriteLine($"# PowerScope Version: {StreamSettings.CURRENT_VERSION}");
            double sampleRate = GetCurrentSampleRate();
            _recordingWriter.WriteLine($"# Sample Rate (Hz): {sampleRate.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            _recordingWriter.WriteLine($"# Recording Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            _recordingWriter.WriteLine($"# Number of Channels: {_channels.Count(c => c.IsEnabled)}");
            
            // Write channel information
            _recordingWriter.WriteLine("# Channel Information:");
            foreach (var channel in _channels.Where(c => c.IsEnabled))
            {
                _recordingWriter.WriteLine($"# {channel.Label}: Stream={channel.StreamType}, Index={channel.LocalChannelIndex}");
            }
            _recordingWriter.WriteLine("#");
            
            // Write CSV header
            headerParts.Add("Sample");
            headerParts.Add("Time_s");
            
            foreach (var channel in _channels.Where(c => c.IsEnabled))
            {
                headerParts.Add(channel.Label);
            }
            
            _recordingWriter.WriteLine(string.Join(",", headerParts));
        }

        private void WriteRecordingData()
        {
            if (!IsRecording || _recordingWriter == null || _channels == null)
                return;
                
            lock (_recordingLock)
            {
                try
                {
                    var dataParts = new List<string>();
                    
                    // Add sample number and time
                    dataParts.Add(_recordingSampleCount.ToString());
                    
                    double sampleRate = GetCurrentSampleRate();
                    double timeSeconds = sampleRate > 0 ? _recordingSampleCount / sampleRate : 0;
                    dataParts.Add(timeSeconds.ToString("F6", System.Globalization.CultureInfo.InvariantCulture));
                    
                    // Add channel data
                    foreach (var channel in _channels.Where(c => c.IsEnabled))
                    {
                        // Get the most recent sample from the channel
                        double[] tempBuffer = new double[1];
                        int samplesRead = channel.CopyLatestDataTo(tempBuffer, 1);
                        
                        if (samplesRead > 0)
                            dataParts.Add(tempBuffer[0].ToString("F6", System.Globalization.CultureInfo.InvariantCulture));
                        else
                            dataParts.Add("0");
                    }
                    
                    _recordingWriter.WriteLine(string.Join(",", dataParts));
                    _recordingSampleCount++;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error writing recording data: {ex.Message}");
                }
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

            // Update cursor channel data automatically when channels change
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
                        
                    // Other channel setting changes are handled by the Channel itself
                    // since it applies settings directly to its owner stream
                }
            });
        }

        public void InitializePlot()
        {
            _plot.Plot.Clear();
            _plot.Plot.Axes.Hairline(true);
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
            
            // Record data if recording is active
            if (IsRecording)
            {
                WriteRecordingData();
            }

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
                
                // Stop recording if active
                StopRecording();
                
                // Disable cursors and clean up
                DisableCursors();
                
                // Dispose cursor
                _cursor?.Dispose();
                
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

        /// <summary>
        /// Gets the plot data for a specific channel at a given sample index
        /// This provides access to the exact data being displayed on the plot
        /// </summary>
        /// <param name="channel">The channel to get data for</param>
        /// <param name="sampleIndex">The sample index (0 = leftmost on plot)</param>
        /// <returns>The displayed value at that sample, or null if not available</returns>
        public double? GetPlotDataAt(Channel channel, int sampleIndex)
        {
            if (channel == null || _channels == null)
                return null;

            // Find the channel index in our collection
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
                return null; // Channel not found in plot

            // Ensure the channel is enabled and has a signal
            if (!channel.IsEnabled || _data[channelIndex] == null)
                return null;

            // Get the data from the plot's data array (what's actually displayed)
            if (sampleIndex >= 0 && sampleIndex < _data[channelIndex].Length)
                return _data[channelIndex][sampleIndex];
            
            return null;
        }

        /// <summary>
        /// Gets the current sample rate from the first available channel
        /// </summary>
        /// <returns>Sample rate in Hz, or 0 if no channels available</returns>
        /// This could be problematic if channels have different sample rates but we assume they are the same for now
        public double GetCurrentSampleRate()
        {
            if (_channels == null || _channels.Count == 0)
                return 0;

            Channel firstChannel = _channels[0];
            if (firstChannel?.OwnerStream == null)
                return 0;

            return firstChannel.OwnerStream.SampleRate;
        }

        /// <summary>
        /// Updates cursor channel values using the unified cursor view model
        /// This centralizes cursor data retrieval and reduces coupling
        /// </summary>
        /// <param name="cursor">The cursor model to update</param>
        public void UpdateCursorValues(Cursor cursor)
        {
            if (cursor == null)
                return;

            cursor.UpdateChannelValues(this);
        }
    }
}
