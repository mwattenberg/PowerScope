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
            // Set PlotSettings as DataContext for HorizontalControl
            HorizontalControl.Settings = _plotManager.Settings;
            
            // Initialize default values through PlotSettings
            _plotManager.Settings.Xmax = DisplayElements; // Set initial window size
            HorizontalControl.BufferSize = 5000000; // Set initial buffer size
        }

        void InitializeVerticalControl()
        {
            // Set PlotSettings as DataContext for VerticalControl
            VerticalControl.Settings = _plotManager.Settings;
            
            // Initialize default values through PlotSettings
            _plotManager.Settings.Ymax = 4000;
            _plotManager.Settings.Ymin = 0;
        }

        private void InitializeEventHandlers()
        {
            HorizontalControl.WindowSizeChanged += _plotManager.updateHorizontalScale;
            
            // Note: VerticalControl now updates PlotSettings directly via data binding
            // The PlotManager automatically handles Y-axis limit updates via its PropertyChanged subscription
            // Legacy events are still fired for backward compatibility if needed
            
            RunControl.RunStateChanged += RunControl_RunStateChanged;

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
            Serializer.WriteSettingsToXML(filePath, _plotManager, DataStreamBar, ChannelControlBar);
        }

        /// <summary>
        /// Reads plot settings from an XML file in the application directory and applies them.
        /// </summary>
        private void readSettingsXML()
        {
            string filePath = Path.Combine(Directory.GetCurrentDirectory(), "Settings.xml");
            Serializer.ReadSettingsFromXML(filePath, _plotManager, DataStreamBar, ChannelControlBar);
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
            _plotManager.Plot.Plot.Axes.SetLimitsY(_plotManager.Settings.Ymin, _plotManager.Settings.Ymax);
            _plotManager.Plot.Refresh();
        }

        private void WpfPlot1_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Restore default axis limits as in initialization
            _plotManager.Plot.Plot.Axes.SetLimitsY(_plotManager.Settings.Ymin, _plotManager.Settings.Ymax);
            _plotManager.Plot.Refresh();
        }

        private void Button_ConfigPlot_Click(object sender, RoutedEventArgs e)
        {
            // Pass current settings to the window - changes are applied immediately via data binding
            View.UserForms.PlotSettingsWindow settingsWindow = new View.UserForms.PlotSettingsWindow(_plotManager.Settings);
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



        private void Scrollbar_ValueChanged(object sender, System.Windows.Controls.Primitives.ScrollEventArgs e)
        {
            double test = e.NewValue;
        }

    }
}