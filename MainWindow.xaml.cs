using System.ComponentModel;
using System.Windows.Media;
using System.Timers;
using System.Windows;
using System.Windows.Input;
using SerialPlotDN_WPF.Model;

namespace SerialPlotDN_WPF
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary> 　
    public partial class MainWindow : Window
    {
        private readonly ScottPlot.Plottables.DataStreamer[] StreamerPlottables = new ScottPlot.Plottables.DataStreamer[8]; // 8 channels   
        readonly private System.Timers.Timer UpdatePlotTimer = new() { Interval = 33, Enabled = true, AutoReset = true }; // ~30 FPS
        private readonly int streamerBufferSize = 5000; // Buffer size for DataStreamer

        // Data management for high-performance plotting
        private readonly int channelCount = 8;

        //Debug just data parsing and plotting
        private DataStream dataStream;

        public MainWindow()
        {
            InitializeComponent();
            InitializePlot();
            InitializeDataStream();
            InitTimer();
            InitializeChannelControlBar();
        }

        private void InitializePlot()
        {
            WpfPlot1.Plot.Clear();

            // Initialize DataStreamer plottables for real-time performance
            for (int i = 0; i < channelCount; i++)
            {
                // Create DataStreamer plottables for maximum real-time performance
                StreamerPlottables[i] = WpfPlot1.Plot.Add.DataStreamer(streamerBufferSize);
                StreamerPlottables[i].LineWidth = 1; // Thinner lines = better performance

                // Use ScottPlot's default palette for automatic color selection
                StreamerPlottables[i].Color = ScottPlot.Palette.Default.GetColor(i);

                // REMOVED: StreamerPlottables[i].ViewScrollLeft(); - This causes auto X-axis scrolling

                // Disable anti-aliasing for better performance
                StreamerPlottables[i].LineStyle.AntiAlias = false;
            }

            // Optimize plot settings for performance
            WpfPlot1.Plot.Axes.ContinuouslyAutoscale = false; // Manual scaling is faster
            WpfPlot1.Plot.RenderManager.ClearCanvasBeforeEachRender = true;

            // Set fixed axis limits to prevent auto scaling
            WpfPlot1.Plot.Axes.SetLimitsX(0, streamerBufferSize);
            WpfPlot1.Plot.Axes.SetLimitsY(-200, 4000); // Adjust Y range based on your data

            // Style settings
            WpfPlot1.Plot.Grid.MajorLineColor = ScottPlot.Color.FromHex("#0e3d54");
            WpfPlot1.Plot.FigureBackground.Color = ScottPlot.Color.FromHex("#07263b");
            WpfPlot1.Plot.DataBackground.Color = ScottPlot.Color.FromHex("#0b3049");
            WpfPlot1.Plot.Axes.Color(ScottPlot.Color.FromHex("#a0acb5"));
        }
        private void InitializeChannelControlBar()
        {
            // Clear any existing channels
            ChannelControlBar.ChannelControls.Clear();

            // Create 8 channels with different colors
            for (int i = 0; i < 8; i++)
            {
                var channelControl = new View.UserControls.ChannelControl();

                // Set channel color using ScottPlot's default palette (same as plot)
                var channelColor = ScottPlot.Palette.Default.GetColor(i);
                var wpfColor = Color.FromRgb((byte)(channelColor.R * 255),
                                            (byte)(channelColor.G * 255),
                                            (byte)(channelColor.B * 255));

                // Configure the channel
                channelControl.SetChannelColorAndLabel(wpfColor, $"CH{i + 1}");
                channelControl.SetGainOffset(1.0, 0.0);
                channelControl.MathFunction = "Raw";

                //// Subscribe to button events (optional)
                //channelControl.ButtonUpClicked += (sender, e) => {
                //    // Handle channel up button click
                //    System.Diagnostics.Debug.WriteLine($"Channel {i + 1} Up clicked");
                //};

                //channelControl.ButtonDownClicked += (sender, e) => {
                //    // Handle channel down button click
                //    System.Diagnostics.Debug.WriteLine($"Channel {i + 1} Down clicked");
                //};

                // Add to the control bar
                ChannelControlBar.AddChannel(channelControl);
            }
        }

        private void InitializeDataStream()
        {
            SourceSetting sourceSetting = new SourceSetting("COM22", 1000000, 9);
            byte[] startbytes = new byte[] { 0xAA, 0xAA };
            dataStream = new DataStream(sourceSetting, new DataParser(DataParser.BinaryFormat.uint16_t, channelCount, startbytes));
            dataStream.Start();
        }

        private void InitTimer()
        {
            UpdatePlotTimer.Elapsed += UpdatePlot;
            UpdatePlotTimer.Start();
        }

        private void UpdatePlot(object source, ElapsedEventArgs e)
        {
            bool hasNewData = false;

            // Get new data for all channels
            for (int channel = 0; channel < channelCount; channel++)
            {
                var newData = dataStream.GetNewData(channel);
                if (newData.Any())
                {
                    hasNewData = true;
                    AddDataToBuffer(channel, newData);
                }
            }

            // Only refresh if we have new data
            if (hasNewData)
            {
                // Use Dispatcher.BeginInvoke for better performance
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    // Update axis limits manually for better control
                    
                    WpfPlot1.Refresh();
                    UpdateAxisLimits();
                }, System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private void AddDataToBuffer(int channel, IEnumerable<double> newData)
        {
            foreach (double value in newData)
            {
                StreamerPlottables[channel].Add(value);
            }
        }

        private void UpdateAxisLimits()
        {
            WpfPlot1.Plot.Axes.SetLimitsY(-200, 200);

            // Set X limits to show the streamer buffer size
            WpfPlot1.Plot.Axes.SetLimitsX(0, streamerBufferSize);
        }

        protected override void OnClosed(EventArgs e)
        {
            UpdatePlotTimer?.Stop();
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
            UpdatePlotTimer?.Stop();
            dataStream?.Dispose();
        }

        private void WpfPlot1_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Left click logic can go here
        }

        private void WpfPlot1_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Toggle benchmark visibility
            if (WpfPlot1.Plot.Benchmark.IsVisible)
            {
                WpfPlot1.Plot.Benchmark.IsVisible = false;
            }
            else
            {
                WpfPlot1.Plot.Benchmark.IsVisible = true;
            }
        }
    }
}