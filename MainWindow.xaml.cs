using System;
using System.Collections.Specialized;
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
using PowerScope.Model;
using PowerScope.View.UserControls;
using PowerScope.View.UserForms;

namespace PowerScope
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

            _plotManager = new PlotManager(WpfPlot1);
            
            _plotManager.InitializePlot();
            _plotManager.SetupPlotUserInput();
            
            InitializeControls();
            InitializeEventHandlers();

            readSettingsXML();
            
            // Initialize channel display based on current streams - simplified with Channel-centric approach
            _plotManager.SetChannels(DataStreamBar.Channels);
        }

        void InitializeControls()
        {
            // Set PlotSettings as DataContext for controls
            HorizontalControl.Settings = _plotManager.Settings;
            VerticalControl.Settings = _plotManager.Settings;
            
            // Set dependencies for MeasurementBar - now gets channels from ChannelControlBar!
            MeasurementBar.ChannelControlBar = ChannelControlBar;
            
            // Set PlotManager as DataContext for RunControl (it now handles its own running state)
            RunControl.DataContext = _plotManager;
            
            // Initialize default values through PlotSettings
            _plotManager.Settings.Xmax = DisplayElements; // Set initial window size
            _plotManager.Settings.Ymax = 4000;
            _plotManager.Settings.Ymin = 0;
            
            // Set initial buffer size in PlotSettings (automatically propagates to UI via data binding)
            _plotManager.Settings.BufferSize = 5000000;
        }

        private void InitializeEventHandlers()
        {
            RunControl.RunStateChanged += RunControl_RunStateChanged;
            RunControl.ClearClicked += RunControl_ClearClicked;

            // Subscribe directly to ObservableCollection.CollectionChanged for automatic notifications
            DataStreamBar.Channels.CollectionChanged += OnChannelsCollectionChanged;
        }

        private void OnChannelsCollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            // Channel-centric approach: simply pass the channels collection to PlotManager
            _plotManager.SetChannels(DataStreamBar.Channels);
            
            // Update ChannelControlBar from DataStreamBar
            ChannelControlBar.UpdateFromDataStreamBar(DataStreamBar);
            
            // Refresh measurements when channels change
            MeasurementBar.RefreshMeasurements();
        }

        private void RunControl_RunStateChanged(object sender, RunControl.RunStates newState)
        {
            if (newState == RunControl.RunStates.Running)
            {
                _plotManager.StartUpdates();
                MeasurementBar.StartUpdates();
            }
            else
            {
                _plotManager.StopUpdates();
                MeasurementBar.StopUpdates();
            }
        }

        private void RunControl_ClearClicked(object sender, EventArgs e)
        {
            _plotManager.Clear();
        }

        /// <summary>
        /// Writes current plot settings to an XML file in the application directory.
        /// </summary>
        private void writeSettingsToXML()
        {
            string filePath = Path.Combine(Directory.GetCurrentDirectory(), "Settings.xml");
            Serializer.WriteSettingsToXML(filePath, _plotManager, DataStreamBar);
        }

        /// <summary>
        /// Reads plot settings from an XML file in the application directory and applies them.
        /// </summary>
        private void readSettingsXML()
        {
            string filePath = Path.Combine(Directory.GetCurrentDirectory(), "Settings.xml");
            Serializer.ReadSettingsFromXML(filePath, _plotManager, DataStreamBar);
        }

        protected override void OnClosed(EventArgs e)
        {
            writeSettingsToXML(); // Save settings on exit
            
            // Stop updates
            _plotManager?.StopUpdates();
            MeasurementBar?.StopUpdates();
            
            // Dispose components
            _plotManager?.Dispose();
            MeasurementBar?.Dispose();
            
            // Dispose DataStreamBar which will handle all stream disposal
            DataStreamBar?.Dispose();
            base.OnClosed(e);
        }


        private void Button_ConfigPlot_Click(object sender, RoutedEventArgs e)
        {
            // Pass current settings to the window - changes are applied immediately via data binding
            View.UserForms.PlotSettingsWindow settingsWindow = new View.UserForms.PlotSettingsWindow(_plotManager.Settings);
            settingsWindow.Show();
        }

    }
}