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
        private DataAndPlotManager _plotManager;
        private readonly System.Timers.Timer _updateAquisitionTimer = new() { Interval = 1000, Enabled = true, AutoReset = true }; // 500 ms
        
        private TimeSpan _prevCpuTime = TimeSpan.Zero;
        private readonly Stopwatch _cpuStopwatch = Stopwatch.StartNew();
        private long _prevCpuStopwatchMs = 0;
        private long _prevSampleCount = 0;
        private long _totalBits = 0;
           

        // Configurable display settings - use DataStream's ring buffer capacity
        public int DisplayElements { get; set; } = 3000; // Number of elements to display

        //Debug just data parsing and plotting
        private DataStream dataStream;

        // Y-axis range properties for plot
        public int Ymin { get; set; } = -200;
        public int Ymax { get; set; } = 4000;

        public MainWindow()
        {
            InitializeComponent();
            _plotManager = new DataAndPlotManager(WpfPlot1, ChannelControlBar, VerticalControl, HorizontalControl);
            _plotManager.InitializePlot();
            _plotManager.InitializeChannelControlBar();
            _plotManager.SetupPlotUserInput();
            InitializeDataStream();
            InitializeTimer();
            InitializeHorizontalControl();
            InitializeVerticalControl();
            readSettingsXML();
            InitializeEventHandlers();
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
        }

        private void InitializeEventHandlers()
        {
            HorizontalControl.WindowSizeChanged += _plotManager.OnWindowSizeChanged;
            VerticalControl.MinValueChanged += (s, v) => _plotManager.SetYLimits(v, _plotManager.Ymax);
            VerticalControl.MaxValueChanged += (s, v) => _plotManager.SetYLimits(_plotManager.Ymin, v);
            VerticalControl.AutoScaleChanged += (s, isAutoScale) => { if (!isAutoScale) _plotManager.SetYLimits(_plotManager.Ymin, _plotManager.Ymax); };
            RunControl.RunStateChanged += RunControl_RunStateChanged;
        }

        private void RunControl_RunStateChanged(object? sender, RunControl.RunStates newState)
        {
            if (newState == RunControl.RunStates.Running)
            {
                dataStream.Start();
                _plotManager.StartPlotTimer();
            }
            else
            {
                dataStream.Stop();
                _plotManager.StopPlotTimer();
            }
        }

        private void InitializeDataStream()
        {
            SourceSetting sourceSetting = new SourceSetting("COM22", 1000000, 9);
            byte[] startbytes = new byte[] { 0xAA, 0xAA };
            dataStream = new DataStream(sourceSetting, new DataParser(DataParser.BinaryFormat.uint16_t, 8, startbytes));
            AquisitionControl.Baudrate = sourceSetting.BaudRate;
            
        }

        private void InitializeTimer()
        {
            _updateAquisitionTimer.Elapsed += UpdateAquisition;
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

            int plotUpdateRateFPS = _plotManager.CurrentPlotUpdateRateFPS;
            int serialPortUpdateRateHz = dataStream?.SerialPortUpdateRateHz ?? 1000;
            int lineWidth = _plotManager.CurrentLineWidth;
            bool antiAliasing = _plotManager.CurrentAntiAliasing;
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
                _plotManager.ApplyPlotSettings(plotUpdateRateFPS, lineWidth, antiAliasing, showRenderTime);
                if (dataStream != null)
                    dataStream.SerialPortUpdateRateHz = serialPortUpdateRateHz;
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
            _updateAquisitionTimer?.Stop();
            dataStream?.Dispose();
            base.OnClosed(e);
        }

        private void closing(object sender, CancelEventArgs e)
        {
            _updateAquisitionTimer?.Stop();
            dataStream?.Dispose();
        }

        private void WpfPlot1_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Restore default axis limits as in initialization
            _plotManager.SetYLimits(_plotManager.Ymin, _plotManager.Ymax);
        }

        private void WpfPlot1_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Restore default axis limits as in initialization
            _plotManager.SetYLimits(_plotManager.Ymin, _plotManager.Ymax);
        }

        private void Button_ConfigPlot_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new View.UserForms.PlotSettingsWindow();
            int currentFPS = _plotManager.CurrentPlotUpdateRateFPS;
            int currentLineWidth = _plotManager.CurrentLineWidth;
            bool currentAntiAliasing = _plotManager.CurrentAntiAliasing;
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
            _plotManager.ApplyPlotSettings(settings.PlotUpdateRateFPS, settings.LineWidth, settings.AntiAliasing, settings.ShowRenderTime);
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