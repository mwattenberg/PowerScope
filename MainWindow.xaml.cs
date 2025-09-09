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
using SerialPlotDN_WPF.View.UserForms;



namespace SerialPlotDN_WPF
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary> 　
    public partial class MainWindow : Window
    {
        private PlotManager _plotManager;
        private SystemManager _systemManager;

        // Configurable display settings - use DataStream's ring buffer capacity
        public int DisplayElements { get; set; } = 3000; // Number of elements to display

        

        public MainWindow()
        {
            InitializeComponent();

            _plotManager = new PlotManager(WpfPlot1);
            _systemManager = new SystemManager();
            
            _plotManager.InitializePlot();
            _plotManager.SetupPlotUserInput();
            
            InitializeControls();
            InitializeEventHandlers();

            readSettingsXML();
            
            // Initialize channel display based on current streams
            int totalChannels = DataStreamBar.GetTotalChannelCount();
            _plotManager.SetDataStreams(DataStreamBar.ConnectedDataStreams);
            _plotManager.SetChannelSettings(ChannelControlBar.ChannelSettings);
            ChannelControlBar.UpdateChannels(totalChannels);
            _plotManager.UpdateChannelDisplay(totalChannels);
        }

        void InitializeControls()
        {
            // Set PlotSettings as DataContext for controls
            HorizontalControl.Settings = _plotManager.Settings;
            VerticalControl.Settings = _plotManager.Settings;
            
            // Set dependencies for MeasurementBar
            MeasurementBar.DataStreamBar = DataStreamBar;
            MeasurementBar.ChannelSettings = ChannelControlBar.ChannelSettings;
            MeasurementBar.SystemManager = _systemManager;
            
            // Set SystemManager dependencies
            _systemManager.SetPlotManager(_plotManager);
            
            // Set SystemManager as DataContext for RunControl
            RunControl.DataContext = _systemManager;
            
            // Initialize default values through PlotSettings
            _plotManager.Settings.Xmax = DisplayElements; // Set initial window size
            _plotManager.Settings.Ymax = 4000;
            _plotManager.Settings.Ymin = 0;
            
            HorizontalControl.BufferSize = 5000000; // Set initial buffer size
        }

        private void InitializeEventHandlers()
        {
            RunControl.RunStateChanged += RunControl_RunStateChanged;

            DataStreamBar.ChannelsChanged += (totalChannels) => 
            {
                _plotManager.SetDataStreams(DataStreamBar.ConnectedDataStreams);
                _plotManager.SetChannelSettings(ChannelControlBar.ChannelSettings);
                // Simplified: no need for GetSignalColors, ChannelSettings handle colors directly
                ChannelControlBar.UpdateChannels(totalChannels);
                _plotManager.UpdateChannelDisplay(totalChannels);
            };
            
            DataStreamBar.StreamsChanged += () =>
            {
                // Update data streams when streams are added or removed
                _plotManager.SetDataStreams(DataStreamBar.ConnectedDataStreams);
            };
            
            // Handle measurement requests from individual channel controls
            ChannelControlBar.ChannelMeasurementRequested += ChannelControlBar_MeasurementRequested;
        }

        /// <summary>
        /// Handle measurement request from ChannelControlBar
        /// </summary>
        private void ChannelControlBar_MeasurementRequested(object sender, MeasurementRequestEventArgs e)
        {
            // Show simplified measurement selection dialog (no channel selection needed)
            var measurementSelection = new MeasurementSelection();
            
            if (measurementSelection.ShowDialog() == true && 
                measurementSelection.SelectedMeasurementType.HasValue)
            {
                // Create measurement using the requesting channel's index and the selected measurement type
                MeasurementBar.AddMeasurement(measurementSelection.SelectedMeasurementType.Value, e.ChannelIndex);
            }
        }

        private void RunControl_RunStateChanged(object? sender, RunControl.RunStates newState)
        {
            if (newState == RunControl.RunStates.Running)
                _systemManager.StartUpdates();
            else
                _systemManager.StopUpdates();
        }

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
            
            // Stop SystemManager
            _systemManager?.StopUpdates();
            _systemManager?.Dispose();
            
            // Dispose MeasurementBar
            MeasurementBar.Dispose();
            
            // Dispose DataStreamBar which will handle all stream disposal
            DataStreamBar.Dispose();
            base.OnClosed(e);
        }

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