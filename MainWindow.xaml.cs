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
using Aelian.FFT;
using System.Windows.Media.Effects; // Add for VisualTreeHelper
using Microsoft.Win32;

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

        // Commands for keyboard shortcuts
        public ICommand SaveSettingsCommand { get; private set; }
        public ICommand LoadSettingsCommand { get; private set; }
        public ICommand PreferencesCommand { get; private set; }
        public ICommand ExportPlotCommand { get; private set; }

        public MainWindow()
        {
            InitializeComponent();
          
            

            // Initialize commands
            InitializeCommands();

            _plotManager = new PlotManager(WpfPlot1);
            
            _plotManager.InitializePlot();
            _plotManager.SetupPlotUserInput();
            
            InitializeControls();
            InitializeEventHandlers();

            readSettingsXML();
            
            // Initialize channel display based on current streams - simplified with Channel-centric approach
            _plotManager.SetChannels(DataStreamBar.Channels);

            FastFourierTransform.Initialize();

            // Set DataContext for command bindings
            DataContext = this;
        }

        private void InitializeCommands()
        {
            SaveSettingsCommand = new RelayCommand(SaveSettings);
            LoadSettingsCommand = new RelayCommand(LoadSettings);
            PreferencesCommand = new RelayCommand(OpenPreferences);
            ExportPlotCommand = new RelayCommand(ExportPlot);
        }

        private void SaveSettings()
        {
            try
            {
                SaveFileDialog saveFileDialog = new SaveFileDialog
                {
                    Filter = "XML files (*.xml)|*.xml|All files (*.*)|*.*",
                    DefaultExt = "xml",
                    FileName = "Settings.xml"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    Serializer.WriteSettingsToXML(saveFileDialog.FileName, _plotManager, DataStreamBar);
                    
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving settings: {ex.Message}", "Save Settings Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadSettings()
        {
            try
            {
                OpenFileDialog openFileDialog = new OpenFileDialog
                {
                    Filter = "XML files (*.xml)|*.xml|All files (*.*)|*.*",
                    DefaultExt = "xml"
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    Serializer.ReadSettingsFromXML(openFileDialog.FileName, _plotManager, DataStreamBar);
                    
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading settings: {ex.Message}", "Load Settings Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenPreferences()
        {
            View.UserForms.PlotSettingsWindow settingsWindow = new View.UserForms.PlotSettingsWindow(_plotManager.Settings);
            settingsWindow.Show();
        }

        private void ExportPlot()
        {
            try
            {
                SaveFileDialog saveFileDialog = new SaveFileDialog
                {
                    Filter = "PNG files (*.png)|*.png|SVG files (*.svg)|*.svg|All files (*.*)|*.*",
                    DefaultExt = "png",
                    FileName = "plot_export"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    string extension = Path.GetExtension(saveFileDialog.FileName).ToLower();
                    
                    if (extension == ".png")
                    {
                        WpfPlot1.Plot.SavePng(saveFileDialog.FileName, 1920, 1080);
                    }
                    else if (extension == ".svg")
                    {
                        WpfPlot1.Plot.SaveSvg(saveFileDialog.FileName, 1920, 1080);
                    }
                    else
                    {
                        // Default to PNG
                        WpfPlot1.Plot.SavePng(saveFileDialog.FileName, 1920, 1080);
                    }
                    

                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting plot: {ex.Message}", "Export Plot Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        void InitializeControls()
        {
            // Set PlotSettings as DataContext for controls
            HorizontalControl.Settings = _plotManager.Settings;
            VerticalControl.Settings = _plotManager.Settings;
            
            // Set dependencies for MeasurementBar - now gets channels from ChannelControlBar!
            MeasurementBar.ChannelControlBar = ChannelControlBar;
            // Pass PlotManager for plot data access and plot control
            MeasurementBar.PlotManager = _plotManager;
            
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
            RunControl.RecordStateChanged += RunControl_RecordStateChanged;
            RunControl.ClearClicked += RunControl_ClearClicked;
            RunControl.LoadClicked += RunControl_LoadClicked;

            // Subscribe directly to ObservableCollection.CollectionChanged for automatic notifications
            DataStreamBar.Channels.CollectionChanged += OnChannelsCollectionChanged;
            
            // Subscribe to MenuBar events
            MainMenuBar.PreferencesClicked += MenuBar_PreferencesClicked;
            MainMenuBar.SaveSettingsClicked += (s, e) => SaveSettings();
            MainMenuBar.LoadSettingsClicked += (s, e) => LoadSettings();
            MainMenuBar.ExportPlotClicked += (s, e) => ExportPlot();
            MainMenuBar.AboutClicked += (s, e) => ShowAboutWindow();
        }

        private void ShowAboutWindow()
        {
            var aboutWindow = new View.UserForms.AboutWindow();
            aboutWindow.Owner = this;
            aboutWindow.ShowDialog();
        }

        private void MenuBar_PreferencesClicked(object sender, EventArgs e)
        {
            OpenPreferences();
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

        private void RunControl_RecordStateChanged(object sender, RunControl.RecordStates newState)
        {
            if (newState == RunControl.RecordStates.Recording)
            {
                // Show save dialog to select file
                SaveFileDialog saveDialog = new SaveFileDialog
                {
                    Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                    DefaultExt = "csv",
                    FileName = $"recording_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
                };
                
                if (saveDialog.ShowDialog() == true)
                {
                    if (!_plotManager.StartRecording(saveDialog.FileName))
                    {
                        MessageBox.Show("Failed to start recording. Please check that you have enabled channels and they are streaming data.", 
                            "Recording Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        // Reset RunControl record state if recording failed - this would require adding a method to RunControl
                    }
                }
                else
                {
                    // User cancelled, reset the button state - this would require adding a method to RunControl
                }
            }
            else
            {
                _plotManager.StopRecording();
            }
        }

        private void RunControl_ClearClicked(object sender, EventArgs e)
        {
            _plotManager.Clear();
        }

        private void RunControl_LoadClicked(object sender, EventArgs e)
        {
            try
            {
                // Show file dialog immediately
                OpenFileDialog openFileDialog = new OpenFileDialog
                {
                    Filter = "CSV files (*.csv)|*.csv|Text files (*.txt)|*.txt|All files (*.*)|*.*",
                    DefaultExt = "csv",
                    Title = "Select Data File to Load"
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    // Create FileDataStream directly
                    var fileStream = new FileDataStream(openFileDialog.FileName, loopPlayback: true);
                    
                    // Connect and start streaming
                    fileStream.Connect();
                    fileStream.StartStreaming();
                    
                    // Add channels for the file stream
                    DataStreamBar.AddChannelsForStream(fileStream);
                    
                    // Create StreamSettings for the UI panel
                    var settings = new StreamSettings
                    {
                        StreamSource = StreamSource.File,
                        FilePath = openFileDialog.FileName,
                        FileLoopPlayback = true
                    };
                    
                    // Add UI panel for the stream
                    DataStreamBar.AddStreamInfoPanel(settings, fileStream);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading file: {ex.Message}", "Load File Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
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

    }

    // Simple RelayCommand implementation
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameter)
        {
            return _canExecute == null || _canExecute();
        }

        public void Execute(object parameter)
        {
            _execute();
        }
    }
}