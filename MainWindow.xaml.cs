using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Ports; // Add for Parity enum
using System.Management;
using System.Timers;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Linq;
using ScottPlot.Plottables;
using SerialPlotDN_WPF.Model;
using SerialPlotDN_WPF.View.UserControls;



namespace SerialPlotDN_WPF
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary> 　
    public partial class MainWindow : Window
    {
        private PlotManager _plotManager;

        // Configurable display settings - use DataStream's ring buffer capacity
        public int DisplayElements { get; set; } = 3000; // Number of elements to display

        

        public MainWindow()
        {
            InitializeComponent();
            //InitializeDataStream();

            _plotManager = new PlotManager(WpfPlot1, VerticalControl, HorizontalControl);
            _plotManager.InitializePlot();
            _plotManager.SetupPlotUserInput();
            
            InitializeHorizontalControl();
            InitializeVerticalControl();
            InitializeEventHandlers();

            readSettingsXML();
            
            
            // Initialize channel display based on current streams
            int totalChannels = DataStreamBar.GetTotalChannelCount();
            _plotManager.SetDataStreams(DataStreamBar.ConnectedDataStreams);
            ChannelControlBar.UpdateChannels(totalChannels);
            _plotManager.UpdateChannelDisplay(totalChannels);
        }

        void InitializeHorizontalControl()
        {
            HorizontalControl.WindowSize = DisplayElements; // Set initial window size
            HorizontalControl.BufferSize = 5000000; // Set initial buffer size
        }

        void InitializeVerticalControl()
        {
            VerticalControl.Max = 4000; // Set initial max value
            VerticalControl.Min = 0; // Set initial min value
        }

        private void InitializeEventHandlers()
        {
            HorizontalControl.WindowSizeChanged += _plotManager.updateHorizontalScale;
            VerticalControl.MinValueChanged += (s, v) => _plotManager.SetYLimits(v, _plotManager.Ymax);
            VerticalControl.MaxValueChanged += (s, v) => _plotManager.SetYLimits(_plotManager.Ymin, v);
            VerticalControl.AutoScaleChanged += (s, isAutoScale) => { if (!isAutoScale) _plotManager.SetYLimits(_plotManager.Ymin, _plotManager.Ymax); };
            RunControl.RunStateChanged += RunControl_RunStateChanged;
            // Remove ClearClicked event handler if not needed
            // RunControl.ClearClicked += (s, e) => _plotManager.Clear();

            DataStreamBar.ChannelsChanged += (totalChannels) => 
            {
                _plotManager.SetDataStreams(DataStreamBar.ConnectedDataStreams);
                Color[] colors = _plotManager.GetSignalColors(totalChannels);
                ChannelControlBar.UpdateChannels(totalChannels, colors);
                _plotManager.UpdateChannelDisplay(totalChannels, colors);
            };
        }

        private void RunControl_RunStateChanged(object? sender, RunControl.RunStates newState)
        {
            if (newState == RunControl.RunStates.Running)
                _plotManager.startAutoUpdate();
            else
                _plotManager.stopAutoUpdate();
        }

        //private void InitializeDataStream()
        //{
        //    SourceSetting sourceSetting = new SourceSetting("COM22", 1000000, 9, Parity.None);
        //    byte[] startbytes = new byte[] { 0xAA, 0xAA };
        //    _dataStream = new SerialDataStream(sourceSetting, new DataParser(DataParser.BinaryFormat.uint16_t, 8, startbytes));
        //    AquisitionControl.Baudrate = sourceSetting.BaudRate;
        //}



        /// <summary>
        /// Writes current plot settings to an XML file in the application directory.
        /// </summary>
        private void writeSettingsToXML()
        {
            string filePath = Path.Combine(Directory.GetCurrentDirectory(), "Settings.xml");
            Serializer.WriteSettingsToXML(filePath, _plotManager, DataStreamBar, ChannelControlBar, VerticalControl);
        }

        /// <summary>
        /// Reads plot settings from an XML file in the application directory and applies them.
        /// </summary>
        private void readSettingsXML()
        {
            string filePath = Path.Combine(Directory.GetCurrentDirectory(), "Settings.xml");
            Serializer.ReadSettingsFromXML(filePath, _plotManager, DataStreamBar, ChannelControlBar, VerticalControl);
        }

        protected override void OnClosed(EventArgs e)
        {
            writeSettingsToXML(); // Save settings on exit
            
            // Dispose DataStreamBar which will handle all stream disposal
            DataStreamBar.Dispose();
            base.OnClosed(e);
        }

        //private void closing(object sender, CancelEventArgs e)
        //{
        //    // Dispose DataStreamBar which will handle all stream disposal
        //    DataStreamBar.Dispose();
        //}

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
            View.UserForms.PlotSettingsWindow settingsWindow = new View.UserForms.PlotSettingsWindow();
            int currentFPS = _plotManager.CurrentPlotUpdateRateFPS;
            int currentLineWidth = _plotManager.CurrentLineWidth;
            bool currentAntiAliasing = _plotManager.CurrentAntiAliasing;
            
            // Use default serial port update rate since we now have multiple streams
            int defaultSerialUpdateRate = 1000;
            settingsWindow.InitializeFromMainWindow(currentFPS, defaultSerialUpdateRate, currentLineWidth, currentAntiAliasing, WpfPlot1.Plot.Benchmark.IsVisible);
            settingsWindow.OnSettingsApplied += (settings) => ApplyPlotSettings(settings);
            settingsWindow.Show();
        }


        private void SetupPlotUserInput()
        {
            WpfPlot1.UserInputProcessor.Reset();
            WpfPlot1.UserInputProcessor.IsEnabled = false;

            // right-click-drag zoom rectangle
            ScottPlot.Interactivity.MouseButton zoomRectangleButton = ScottPlot.Interactivity.StandardMouseButtons.Right;
            ScottPlot.Interactivity.UserActionResponses.MouseDragZoomRectangle zoomRectangleResponse = new ScottPlot.Interactivity.UserActionResponses.MouseDragZoomRectangle(zoomRectangleButton);
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

    }
}