using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Timers;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Linq;
using SerialPlotDN_WPF.Model;
using SerialPlotDN_WPF.View.UserControls;



namespace SerialPlotDN_WPF
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary> 　
    public partial class MainWindow : Window
    {
        private readonly ScottPlot.Plottables.Signal[] _signals = new ScottPlot.Plottables.Signal[8]; // 8 channels   
        // Pre-allocated arrays for linearized data - avoid memory allocations during plotting
        private readonly double[][] _linearizedDataArrays = new double[8][];
        // Timer for updating the plot at a fixed rate
        readonly private System.Timers.Timer _updatePlotTimer = new() { Interval = 33, Enabled = true, AutoReset = true }; // ~30 FPS
        private readonly System.Timers.Timer _updateAquisitionTimer = new() { Interval = 1000, Enabled = true, AutoReset = true }; // 500 ms
        
        private TimeSpan _prevCpuTime = TimeSpan.Zero;
        private readonly Stopwatch _cpuStopwatch = Stopwatch.StartNew();
        private long _prevCpuStopwatchMs = 0;
        private long _prevSampleCount = 0;
        private long _totalBits = 0;
           

        // Configurable display settings - use DataStream's ring buffer capacity
        public int DisplayElements { get; set; } = 3000; // Number of elements to display
        private readonly int _channelCount = 8;

        //Debug just data parsing and plotting
        private DataStream dataStream;

        // Y-axis range properties for plot
        public int Ymin { get; set; } = -200;
        public int Ymax { get; set; } = 4000;

        public MainWindow()
        {
            InitializeComponent();

            // Initialize pre-allocated arrays for efficient data copying
            for (int i = 0; i < _channelCount; i++)
            {
                _linearizedDataArrays[i] = new double[DisplayElements];
            }
            
            InitializePlot();
            InitializeDataStream();
            InitializeTimer();
            InitializeChannelControlBar();
            InitializeHorizontalControl();
            InitializeVerticalControl();
            readSettingsXML();
            InitializeEventHandlers();
            SetupPlotUserInput();
        }

        void InitializeHorizontalControl()
        {
            HorizontalControl.WindowSize = DisplayElements; // Set initial window size
            HorizontalControl.BufferSize = 5000000; // Set initial buffer size
        }

        void InitializeVerticalControl()
        {
            VerticalControl.Max = Ymax; // Set initial max value
            VerticalControl.Min = Ymin; // Set initial min value
                                        // 

        }

        private void InitializeEventHandlers()
        {
            HorizontalControl.WindowSizeChanged += HorizontalControl_WindowSizeChanged;
            RunControl.RunStateChanged += RunControl_RunStateChanged;
            VerticalControl.MinValueChanged += VerticalControl_MinValueChanged;
            VerticalControl.MaxValueChanged += VerticalControl_MaxValueChanged;
            VerticalControl.AutoScaleChanged += VerticalControl_AutoScaleChanged;
        }

        private void HorizontalControl_WindowSizeChanged(object? sender, int newSize)
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
                WpfPlot1.Plot.Remove(_signals[i]); // Remove old signals
                _linearizedDataArrays[i] = new double[DisplayElements];
                _signals[i] = WpfPlot1.Plot.Add.Signal(_linearizedDataArrays[i]);
                _signals[i].Color = colorOld;
                _signals[i].LineWidth = linewidthOld; // Reapply old line width
                _signals[i].LineStyle.AntiAlias = antiAliasingOld; // Reapply old anti-aliasing setting
            }
            WpfPlot1.Plot.Axes.SetLimitsX(0, DisplayElements);
            
            if(isRunning ) 
                _updatePlotTimer.Start();
            else
            {
                UpdatePlot(null, null);
            }
                
        }

        private void RunControl_RunStateChanged(object? sender, RunControl.RunStates newState)
        {
            if (newState == RunControl.RunStates.Running)
            {
                // Start the data stream and timers
                dataStream.Start();
                _updatePlotTimer.Start();
                _updateAquisitionTimer.Start();
            }
            else
            {
                // Stop the data stream and timers
                dataStream.Stop();
                _updatePlotTimer.Stop();
                _updateAquisitionTimer.Stop();
            }
        }

        private void VerticalControl_MinValueChanged(object? sender, int newMin)
        {
            Ymin = newMin;
            WpfPlot1.Plot.Axes.SetLimitsY(Ymin, Ymax);
            WpfPlot1.Refresh();
        }

        private void VerticalControl_MaxValueChanged(object? sender, int newMax)
        {
            Ymax = newMax;
            WpfPlot1.Plot.Axes.SetLimitsY(Ymin, Ymax);
            WpfPlot1.Refresh();
        }

        private void VerticalControl_AutoScaleChanged(object? sender, bool isAutoScale)
        {
            if (!isAutoScale)
            {
                WpfPlot1.Plot.Axes.SetLimitsY(Ymin, Ymax);
            }
            WpfPlot1.Refresh();
        }

        private void InitializePlot()
        {
            WpfPlot1.Plot.Clear();
            WpfPlot1.Plot.Add.Palette = new ScottPlot.Palettes.Category10();

            // Double the axis font size for all axes (ScottPlot v5+)
            foreach (var axis in WpfPlot1.Plot.Axes.GetAxes())
            {
                axis.TickLabelStyle.FontSize *= 2;
                axis.Label.FontSize *= 2;
            }

            // Initialize Signal plottables with pre-allocated arrays
            for (int i = 0; i < _channelCount; i++)
            {
                // Create Signal plottables with pre-allocated arrays - these will be reused
                _signals[i] = WpfPlot1.Plot.Add.Signal(_linearizedDataArrays[i]);
                _signals[i].Color = _signals[i].Color.Lighten(0.2); // Lighten the color for better 
                _signals[i].Color = _signals[i].Color.WithOpacity(1.0); // Ensure full opacity
                _signals[i].LineWidth = 1; // Thinner lines = better performance
                
                // Disable anti-aliasing for better performance
                _signals[i].LineStyle.AntiAlias = false;
            }

            WpfPlot1.Plot.ShowLegend();

            // change figure colors
            WpfPlot1.Plot.FigureBackground.Color = ScottPlot.Color.FromHex("#181818");
            WpfPlot1.Plot.DataBackground.Color = ScottPlot.Color.FromHex("#1f1f1f");

            // change axis and grid colors
            WpfPlot1.Plot.Axes.Color(ScottPlot.Color.FromHex("#d7d7d7"));
            WpfPlot1.Plot.Grid.LineWidth = 1.0f; // Thinner grid lines for better 
            WpfPlot1.Plot.Grid.MajorLineColor = ScottPlot.Color.FromHex("#404040");

            // change legend colors
            WpfPlot1.Plot.Legend.BackgroundColor = ScottPlot.Color.FromHex("#404040");
            WpfPlot1.Plot.Legend.FontColor = ScottPlot.Color.FromHex("#d7d7d7");
            WpfPlot1.Plot.Legend.OutlineColor = ScottPlot.Color.FromHex("#d7d7d7");

            // Optimize plot settings for performance
            WpfPlot1.Plot.Axes.ContinuouslyAutoscale = false; // Manual scaling is faster
            WpfPlot1.Plot.RenderManager.ClearCanvasBeforeEachRender = true;

            // Set fixed axis limits to prevent auto scaling - show display window
            WpfPlot1.Plot.Axes.SetLimitsX(0, DisplayElements);
            WpfPlot1.Plot.Axes.SetLimitsY(Ymin, Ymax); // Adjust Y range based on your data
            WpfPlot1.Plot.Axes.Bottom.IsVisible = true;
        }

        private void InitializeChannelControlBar()
        {
            // Create 8 channels with different colors
            for (int i = 0; i < 8; i++)
            {
                ChannelControl channel = new ChannelControl();
                var channelColor = _signals[i].Color;
                var wpfColor = Color.FromArgb(channelColor.A,
                                      channelColor.R,
                                      channelColor.G,
                                      channelColor.B);
                channel.Color = wpfColor;
                channel.Label = $"CH{i + 1}";
                channel.Gain = 1.0;
                channel.Offset = 0.0;
                ChannelControlBar.AddChannel(channel); // Direct access to ChannelControlBar
            }
        }

        private void InitializeDataStream()
        {
            SourceSetting sourceSetting = new SourceSetting("COM22", 1000000, 9);
            byte[] startbytes = new byte[] { 0xAA, 0xAA };
            dataStream = new DataStream(sourceSetting, new DataParser(DataParser.BinaryFormat.uint16_t, _channelCount, startbytes));
            AquisitionControl.Baudrate = sourceSetting.BaudRate;
            
        }

        private void InitializeTimer()
        {
            _updatePlotTimer.Elapsed += UpdatePlot;
            _updateAquisitionTimer.Elapsed += UpdateAquisition;
        }

        private void UpdatePlot(object? source, ElapsedEventArgs? e)
        {
            // Use Dispatcher.BeginInvoke for better performance
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                for (int channel = 0; channel < _channelCount; channel++)
                {
                    // Efficiently copy data to pre-allocated array without memory allocation
                   dataStream.CopyLatestDataTo(channel, _linearizedDataArrays[channel], DisplayElements);
                }
                if(VerticalControl.IsAutoScale)
                    WpfPlot1.Plot.Axes.AutoScaleY();

                WpfPlot1.Refresh();
                
            }, System.Windows.Threading.DispatcherPriority.Render);
        }


        private void UpdateAquisition(object sender, ElapsedEventArgs e)
        {
            // Ensure up-to-date data
            var process = Process.GetCurrentProcess();
            process.Refresh();
            
            //Update CPU usage
            TimeSpan cpuTime = process.TotalProcessorTime;
            long currentMs = _cpuStopwatch.ElapsedMilliseconds;           

            double cpuUsedMs = (cpuTime - _prevCpuTime).TotalMilliseconds;
            long elapsedMs = currentMs - _prevCpuStopwatchMs;

            _prevCpuStopwatchMs = currentMs;
            _prevCpuTime = cpuTime;

            if (elapsedMs < _updateAquisitionTimer.Interval)
                return; // Avoid division by zero if timer interval is too short

            double cpuUsagePercent = (cpuUsedMs / (elapsedMs * Environment.ProcessorCount)) * 1000;

            //Update memory usage
            long memoryBytes = Process.GetCurrentProcess().WorkingSet64;
            double memoryMB = memoryBytes / (1024.0 * 1024.0);

            //Serial port samples
            long samplesPerSecond = (dataStream.TotalSamples - _prevSampleCount) / (elapsedMs / 1000); 
            _prevSampleCount = dataStream.TotalSamples;

            //Bits per second
            long bitsPerSecond = (dataStream.TotalBits - _totalBits) / (elapsedMs / 1000);
            _totalBits = dataStream.TotalBits;

            Application.Current.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal, new Action(() =>
            {
                if (AquisitionControl != null && AquisitionControl.IsLoaded)
                {
                    // Update AquisitionControl
                    AquisitionControl.TotalMemorySize = (long)memoryMB;
                    AquisitionControl.CPULoad = cpuUsagePercent;
                    AquisitionControl.SamplesPerSecond = samplesPerSecond;
                    AquisitionControl.BitsPerSecond = bitsPerSecond;
                }
            }));
        }

        /// <summary>
        /// Writes current plot settings to an XML file in the application directory.
        /// </summary>
        private void writeSettingsToXML()
        {
            var channelLabels = new XElement("ChannelLabels");
            foreach (ChannelControl channel in ChannelControlBar.Channels)
            {

                channelLabels.Add(new XElement("Label", channel.Label));
            }

            int plotUpdateRateFPS = (int)(1000.0 / _updatePlotTimer.Interval);
            int serialPortUpdateRateHz = dataStream?.SerialPortUpdateRateHz ?? 1000;
            int lineWidth = (int)(_signals[0]?.LineWidth ?? 1);
            bool antiAliasing = _signals[0]?.LineStyle.AntiAlias ?? false;
            bool showRenderTime = WpfPlot1.Plot.Benchmark.IsVisible;
            bool autoScale = VerticalControl.IsAutoScale;
            var settingsXml = new XElement("PlotSettings",
                new XElement("PlotUpdateRateFPS", plotUpdateRateFPS),
                new XElement("SerialPortUpdateRateHz", serialPortUpdateRateHz),
                new XElement("LineWidth", lineWidth),
                new XElement("AntiAliasing", antiAliasing),
                new XElement("ShowRenderTime", showRenderTime),
                new XElement("Ymin", Ymin),
                new XElement("Ymax", Ymax),
                new XElement("AutoScale", autoScale),
                channelLabels
            );
            string filePath = Path.Combine(Directory.GetCurrentDirectory(), "Settings.xml");
            settingsXml.Save(filePath);
        }

        /// <summary>
        /// Reads plot settings from an XML file in the application directory and applies them.
        /// </summary>
        private void readSettingsXML()
        {
            string filePath = Path.Combine(Directory.GetCurrentDirectory(), "Settings.xml");
            if (!File.Exists(filePath)) return;
            try
            {


                var settingsXml = XElement.Load(filePath);
                int plotUpdateRateFPS = int.Parse(settingsXml.Element("PlotUpdateRateFPS")?.Value ?? "30");
                int serialPortUpdateRateHz = int.Parse(settingsXml.Element("SerialPortUpdateRateHz")?.Value ?? "1000");
                int lineWidth = int.Parse(settingsXml.Element("LineWidth")?.Value ?? "1");
                bool antiAliasing = bool.Parse(settingsXml.Element("AntiAliasing")?.Value ?? "false");
                bool showRenderTime = bool.Parse(settingsXml.Element("ShowRenderTime")?.Value ?? "false");
                int yMin = int.Parse(settingsXml.Element("Ymin")?.Value ?? "-200");
                int yMax = int.Parse(settingsXml.Element("Ymax")?.Value ?? "4000");
                bool autoScale = bool.Parse(settingsXml.Element("AutoScale")?.Value ?? "true");
                Ymin = yMin;
                Ymax = yMax;
                VerticalControl.Min = Ymin;
                VerticalControl.Max = Ymax;
                VerticalControl.IsAutoScale = autoScale;
                _updatePlotTimer.Interval = 1000.0 / plotUpdateRateFPS;
                if (dataStream != null)
                    dataStream.SerialPortUpdateRateHz = serialPortUpdateRateHz;
                for (int i = 0; i < _signals.Length; i++)
                {
                    if (_signals[i] != null)
                    {
                        _signals[i].LineWidth = lineWidth;
                        _signals[i].LineStyle.AntiAlias = antiAliasing;
                    }
                }
                WpfPlot1.Plot.Benchmark.IsVisible = showRenderTime;
                WpfPlot1.Plot.Axes.SetLimitsY(Ymin, Ymax);

                var channelLabelElement = settingsXml.Element("ChannelLabels");
                if (channelLabelElement != null)
                {
                    var labelElements = channelLabelElement.Elements("Label").ToList();
                    int i = 0;
                    foreach (ChannelControl channel in ChannelControlBar.Channels)
                    {
                        if (i < labelElements.Count)
                        {
                            channel.Label = labelElements[i].Value;
                        }
                        i++;
                    }
                }
            }
            catch { /* Ignore errors and use defaults */ }
        }

        protected override void OnClosed(EventArgs e)
        {
            writeSettingsToXML(); // Save settings on exit
            _updatePlotTimer?.Stop();
            _updateAquisitionTimer?.Stop();
            dataStream?.Dispose();
            base.OnClosed(e);
        }

        // Event handlers for XAML events
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Window loaded logic can go here if needed in the future
        }

        private void closing(object sender, CancelEventArgs e)
        {
            _updateAquisitionTimer?.Stop();
            _updatePlotTimer?.Stop();
            dataStream?.Dispose();
        }

        private void WpfPlot1_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Restore default axis limits as in initialization
            WpfPlot1.Plot.Axes.SetLimitsX(0, DisplayElements);
            WpfPlot1.Plot.Axes.SetLimitsY(Ymin, Ymax);
            WpfPlot1.Refresh();
        }

        private void WpfPlot1_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Restore default axis limits as in initialization
            WpfPlot1.Plot.Axes.SetLimitsX(0, DisplayElements);
            WpfPlot1.Plot.Axes.SetLimitsY(Ymin, Ymax);
            WpfPlot1.Refresh();
        }

        private void Button_ConfigPlot_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new View.UserForms.PlotSettingsWindow();
            int currentFPS = (int)(1000.0 / _updatePlotTimer.Interval);
            int currentLineWidth = (int)(_signals[0]?.LineWidth ?? 1);
            bool currentAntiAliasing = _signals[0]?.LineStyle.AntiAlias ?? false; // Fixed property name
            settingsWindow.InitializeFromMainWindow(currentFPS, dataStream.SerialPortUpdateRateHz, currentLineWidth, currentAntiAliasing, WpfPlot1.Plot.Benchmark.IsVisible);
            settingsWindow.OnSettingsApplied += (settings) => ApplyPlotSettings(settings);
            settingsWindow.Show();
        }

        private void SetupPlotUserInput()
        {  
            WpfPlot1.UserInputProcessor.Reset();
            WpfPlot1.UserInputProcessor.IsEnabled = false;

            // right-click-drag zoom rectangle
            var zoomRectangleButton = ScottPlot.Interactivity.StandardMouseButtons.Right;
            var zoomRectangleResponse = new ScottPlot.Interactivity.UserActionResponses.MouseDragZoomRectangle(zoomRectangleButton);
            WpfPlot1.UserInputProcessor.UserActionResponses.Add(zoomRectangleResponse);
        }




        private void ApplyPlotSettings(View.UserForms.PlotSettingsWindow settings)
        {
            _updatePlotTimer.Interval = 1000.0 / settings.PlotUpdateRateFPS;
            dataStream.SerialPortUpdateRateHz = settings.SerialPortUpdateRateHz;
            WpfPlot1.Plot.Grid.LineWidth = (float)settings.LineWidth / 2;
            for (int i = 0; i < _signals.Length; i++)
            {
                if (_signals[i] != null)
                {
                    _signals[i].LineWidth = (float)settings.LineWidth;
                    _signals[i].LineStyle.AntiAlias = settings.AntiAliasing;
                }
            }
            WpfPlot1.Plot.Benchmark.IsVisible = settings.ShowRenderTime;
            WpfPlot1.Refresh();
        }

        private void Scrollbar_ValueChanged(object sender, System.Windows.Controls.Primitives.ScrollEventArgs e)
        {
            double test = e.NewValue;
        }

        private void WpfPlot1_MouseDoubleCLick(object sender, MouseButtonEventArgs e)
        {

        }
    }
}