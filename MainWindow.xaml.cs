using System.ComponentModel;
using System.IO;
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
        
        // Configurable display settings - use DataStream's ring buffer capacity
        public int DisplayElements { get; set; } = 10000; // Number of elements to display
        private readonly int _channelCount = 8;

        //Debug just data parsing and plotting
        private DataStream dataStream;

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
            InitTimer();
            InitializeChannelControlBar();

            readSettingsXML(); // Load settings at startup
        }

        private void InitializePlot()
        {
            WpfPlot1.Plot.Clear();
            WpfPlot1.Plot.Add.Palette = new ScottPlot.Palettes.Category10();

            

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
            WpfPlot1.Plot.Axes.SetLimitsY(-200, 4000); // Adjust Y range based on your data
            WpfPlot1.Plot.Axes.Bottom.IsVisible = false;
        }

        private void InitializeChannelControlBar()
        {
            // Clear any existing channels
            ChannelControlBar.ChannelControls.Clear();

            // Create 8 channels with different colors
            for (int i = 0; i < 8; i++)
            {

                // Ensure palette is initialized
                ChannelControl  channel = new ChannelControl();

                
                // Set channel color using ScottPlot's default palette (same as plot)
                var channelColor = _signals[i].Color;

                var wpfColor = Color.FromArgb(channelColor.A,
                                            channelColor.R,
                                            channelColor.G,
                                            channelColor.B);

                // Configure the channel
                channel.Color = wpfColor;
                channel.SetChannelLabel($"CH{i + 1}");
                channel.Gain = 1.0; // Default gain
                channel.Offset = 0.0; // Default offset

                // Add to the control bar
                ChannelControlBar.AddChannel(channel);
            }
        }

        private void InitializeDataStream()
        {
            SourceSetting sourceSetting = new SourceSetting("COM22", 1000000, 9);
            byte[] startbytes = new byte[] { 0xAA, 0xAA };
            dataStream = new DataStream(sourceSetting, new DataParser(DataParser.BinaryFormat.uint16_t, _channelCount, startbytes));
            dataStream.Start();
        }

        private void InitTimer()
        {
            _updatePlotTimer.Elapsed += UpdatePlot;
            _updatePlotTimer.Start();
        }

        private void UpdatePlot(object source, ElapsedEventArgs e)
        {
            bool hasNewData = false;

            // Check if any channel has new data
            for (int channel = 0; channel < _channelCount; channel++)
            {
                var newData = dataStream.GetNewData(channel);
                if (newData.Any())
                {
                    hasNewData = true;
                    break; // No need to check other channels if we found new data
                }
            }

            // Only refresh if we have new data
            if (hasNewData)
            {
                // Use Dispatcher.BeginInvoke for better performance
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    UpdateSignalPlots();
                    WpfPlot1.Refresh();
                }, System.Windows.Threading.DispatcherPriority.Render);
            }
        }

        private void UpdateSignalPlots()
        {


            for (int channel = 0; channel < _channelCount; channel++)
            {
                // Efficiently copy data to pre-allocated array without memory allocation
                int actualDataCount = dataStream.CopyLatestDataTo(channel, _linearizedDataArrays[channel], DisplayElements);
                
                if (actualDataCount > 0)
                {
                    // Signal plots are already connected to the pre-allocated arrays
                    // Since the array data has been updated in-place, the Signal will 
                    // automatically use the updated data when rendered
                }
            }
        }

        /// <summary>
        /// Writes current plot settings to an XML file in the application directory.
        /// </summary>
        private void writeSettingToXML()
        {
            int plotUpdateRateFPS = (int)(1000.0 / _updatePlotTimer.Interval);
            int serialPortUpdateRateHz = dataStream?.SerialPortUpdateRateHz ?? 1000;
            int lineWidth = (int)(_signals[0]?.LineWidth ?? 1);
            bool antiAliasing = _signals[0]?.LineStyle.AntiAlias ?? false;
            bool showRenderTime = WpfPlot1.Plot.Benchmark.IsVisible;
            var settingsXml = new XElement("PlotSettings",
                new XElement("PlotUpdateRateFPS", plotUpdateRateFPS),
                new XElement("SerialPortUpdateRateHz", serialPortUpdateRateHz),
                new XElement("LineWidth", lineWidth),
                new XElement("AntiAliasing", antiAliasing),
                new XElement("ShowRenderTime", showRenderTime)
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
            }
            catch { /* Ignore errors and use defaults */ }
        }

        protected override void OnClosed(EventArgs e)
        {
            writeSettingToXML(); // Save settings on exit
            _updatePlotTimer?.Stop();
            dataStream?.Dispose();
            base.OnClosed(e);
        }

        // Event handlers for XAML events
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Window loaded logic can go here
        }

        private void closing(object sender, CancelEventArgs e)
        {
            _updatePlotTimer?.Stop();
            dataStream?.Dispose();
        }

        private void WpfPlot1_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Left click logic can go here
        }

        private void WpfPlot1_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Restore default axis limits as in initialization
            WpfPlot1.Plot.Axes.SetLimitsX(0, DisplayElements);
            WpfPlot1.Plot.Axes.SetLimitsY(-200, 4000);
        }

        private void Button_ConfigPlot_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new View.UserForms.PlotSettingsWindow();
            int currentFPS = (int)(1000.0 / _updatePlotTimer.Interval);
            int currentLineWidth = (int)(_signals[0]?.LineWidth ?? 1);
            bool currentAntiAliasing = _signals[0]?.LineStyle.AntiAlias ?? false;
            settingsWindow.InitializeFromMainWindow(currentFPS, dataStream.SerialPortUpdateRateHz, currentLineWidth, currentAntiAliasing, WpfPlot1.Plot.Benchmark.IsVisible);
            settingsWindow.OnSettingsApplied += (settings) => ApplyPlotSettings(settings);
            settingsWindow.Show();
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
    }
}