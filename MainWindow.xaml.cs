using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using PowerScope.Model;
using PowerScope.Model.Mcp;
using PowerScope.View.UserControls;
using PowerScope.View.UserForms;
using Aelian.FFT;
using Microsoft.Win32;

namespace PowerScope
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary> 
    public partial class MainWindow : Window
    {
        private PlotManager _plotManager;
        private McpServer _mcpServer;

        // Configurable display settings - use DataStream's ring buffer capacity
        public int DisplayElements { get; set; } = 3000; // Number of elements to display

        // Commands for keyboard shortcuts
        public ICommand SaveSettingsCommand { get; private set; }
        public ICommand LoadSettingsCommand { get; private set; }
        public ICommand OpenWaveformCommand { get; private set; }
        public ICommand PreferencesCommand { get; private set; }

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

            // A corrupted or outdated session file shouldn't prevent the app from starting,
            // so only catch the exceptions an old/malformed XML file can actually cause.
            // Anything else (e.g. a NullReferenceException from a real bug) should surface.
            try
            {
                RestoreSessionFromXML();
            }
            catch (System.Xml.XmlException ex)
            {
                MessageBox.Show($"Could not restore previous settings: the session file is not valid XML.\n\n{ex.Message}",
                    "Restore Settings Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (FormatException ex)
            {
                MessageBox.Show($"Could not restore previous settings: the session file contains an invalid value.\n\n{ex.Message}",
                    "Restore Settings Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (IOException ex)
            {
                MessageBox.Show($"Could not restore previous settings: the session file could not be read.\n\n{ex.Message}",
                    "Restore Settings Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            }


            // Initialize channel display based on current streams - simplified with Channel-centric approach
            _plotManager.SetChannels(DataStreamBar.Channels);

            FastFourierTransform.Initialize();

            // Set DataContext for command bindings
            DataContext = this;

            // Start or stop the MCP server to match the loaded setting, and keep it
            // in sync as the user toggles it in the Plot Settings window.
            _plotManager.Settings.PropertyChanged += Settings_PropertyChanged;
            ApplyMcpServerState();
        }

        private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PlotSettings.McpServerEnabled))
                ApplyMcpServerState();
        }

        private void ApplyMcpServerState()
        {
            if (_plotManager.Settings.McpServerEnabled)
                StartMcpServer();
            else
                StopMcpServer();
        }

        private void StartMcpServer()
        {
            if (_mcpServer != null)
                return; // already running

            try
            {
                McpToolService toolService = new McpToolService(new McpWindowHost(this));
                _mcpServer = new McpServer(toolService);
                _mcpServer.Start();
                _plotManager.Settings.McpServerStatus = "Running";
            }
            catch (System.Net.Sockets.SocketException)
            {
                // Most commonly AddressAlreadyInUse: another process (e.g. a second
                // PowerScope instance) already holds the MCP port.
                _mcpServer?.Dispose();
                _mcpServer = null;
                _plotManager.Settings.McpServerStatus = "Port occupied";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Warning: MCP server failed to start: {ex.Message}");
                _mcpServer?.Dispose();
                _mcpServer = null;
                _plotManager.Settings.McpServerStatus = "Stopped";
            }
        }

        private void StopMcpServer()
        {
            _mcpServer?.Dispose();
            _mcpServer = null;
            _plotManager.Settings.McpServerStatus = "Stopped";
        }

        private void InitializeCommands()
        {
            SaveSettingsCommand = new RelayCommand(SaveSettings);
            LoadSettingsCommand = new RelayCommand(LoadSettings);
            OpenWaveformCommand = new RelayCommand(OpenWaveform);
            PreferencesCommand = new RelayCommand(OpenPreferences);
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
                    Serializer.SaveSessionToXML(saveFileDialog.FileName, _plotManager, DataStreamBar);

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
                    Serializer.LoadSessionFromXML(openFileDialog.FileName, _plotManager, DataStreamBar, ResolveMissingSerialPort);

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
                    Filter = "PNG files (*.png)|*.png|SVG files (*.svg)|*.svg|CSV files (*.csv)|*.csv",
                    DefaultExt = "png",
                    FileName = $"export_{DateTime.Now:yyyyMMdd_HHmmss}"
                };

                if (saveFileDialog.ShowDialog() != true)
                    return;

                string extension = Path.GetExtension(saveFileDialog.FileName).ToLower();

                switch (extension)
                {
                    case ".svg":
                        WpfPlot1.Plot.SaveSvg(saveFileDialog.FileName, 1920, 1080);
                        break;
                    case ".csv":
                        ExportPlotDataToCsv(saveFileDialog.FileName);
                        break;
                    case ".png":
                    default:
                        WpfPlot1.Plot.SavePng(saveFileDialog.FileName, 1920, 1080);
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportPlotDataToCsv(string filePath)
        {
            PlotSnapshot snapshot = _plotManager.GetSnapshot();

            if (snapshot == null)
            {
                MessageBox.Show("No data to export.", "Export Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _plotManager.FileWriter.ExportSnapshot(filePath, snapshot);
        }

        private void OpenWaveform()
        {
            StreamSettings streamSettings = new StreamSettings
            {
                StreamSource = StreamSource.File
            };

            SerialConfigWindow configWindow = new SerialConfigWindow(streamSettings);
            configWindow.Owner = this;

            if (configWindow.ShowDialog() != true)
                return;

            try
            {
                FileDataStream fileStream = new FileDataStream(streamSettings.FilePath, loopPlayback: streamSettings.FileLoopPlayback);
                fileStream.Connect();
                fileStream.StartStreaming();

                DataStreamBar.AddChannelsForStream(fileStream);
                DataStreamBar.AddStreamInfoPanel(streamSettings, fileStream);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading waveform: {ex.Message}", "Open Waveform Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        void InitializeControls()
        {
            // Set PlotSettings as DataContext for controls
            HorizontalControl.Settings = _plotManager.Settings;
            HorizontalControl.PlotManager = _plotManager;

            TriggerControl.Settings = _plotManager.Settings;
            TriggerControl.AvailableChannels = DataStreamBar.Channels;
            TriggerControl.PlotManager = _plotManager;

            VerticalControl.Settings = _plotManager.Settings;

            // Set dependencies for MeasurementBar - now gets channels from ChannelControlBar!
            MeasurementBar.ChannelControlBar = ChannelControlBar;
            // Pass PlotManager for plot data access and plot control
            MeasurementBar.PlotManager = _plotManager;

            // Set PlotManager for RunControl to monitor trigger state
            RunControl.PlotManager = _plotManager;

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
            RunControl.ExportClicked += (s, e) => ExportPlot();

            // Subscribe directly to ObservableCollection.CollectionChanged for automatic notifications
            DataStreamBar.Channels.CollectionChanged += OnChannelsCollectionChanged;

            // Subscribe to MenuBar events
            MainMenuBar.PreferencesClicked += MenuBar_PreferencesClicked;
            MainMenuBar.SaveSettingsClicked += (s, e) => SaveSettings();
            MainMenuBar.LoadSettingsClicked += (s, e) => LoadSettings();
            MainMenuBar.OpenWaveformClicked += (s, e) => OpenWaveform();
            MainMenuBar.AboutClicked += (s, e) => ShowAboutWindow();
        }

        private void ShowAboutWindow()
        {
            View.UserForms.AboutWindow aboutWindow = new View.UserForms.AboutWindow();
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

            // Update TriggerControl's available channels for trigger selection
            TriggerControl.AvailableChannels = DataStreamBar.Channels;

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
                SaveFileDialog saveDialog = new SaveFileDialog
                {
                    Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                    DefaultExt = "csv",
                    FileName = $"recording_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    if (_plotManager.StartRecording(saveDialog.FileName))
                    {
                        RunControl.IsRecording = true;
                    }
                    else
                    {
                        MessageBox.Show("Failed to start recording. Please check that you have enabled channels and they are streaming data.",
                            "Recording Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
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

        /// <summary>
        /// Writes current plot settings to an XML file in the application directory.
        /// </summary>
        private void writeSettingsToXML()
        {
            string filePath = Path.Combine(Directory.GetCurrentDirectory(), "Settings.xml");
            Serializer.SaveSessionToXML(filePath, _plotManager, DataStreamBar);
        }

        /// <summary>
        /// Reads plot settings from an XML file and applies them.
        /// Uses the file given via the --config command line argument when present,
        /// otherwise Settings.xml in the application directory.
        /// </summary>
        private void RestoreSessionFromXML()
        {
            string filePath;
            if (App.ConfigFilePath != null)
                filePath = App.ConfigFilePath;
            else
                filePath = Path.Combine(Directory.GetCurrentDirectory(), "Settings.xml");
            Serializer.LoadSessionFromXML(filePath, _plotManager, DataStreamBar, ResolveMissingSerialPort);
        }

        /// <summary>
        /// Prompts the user to pick a replacement COM port when a saved serial stream's port no
        /// longer exists. All other settings parsed from the session file are kept and the
        /// existing StreamConfigWindow is reused to let the user pick a new port.
        /// </summary>
        private StreamSettings ResolveMissingSerialPort(StreamSettings settings)
        {
            MessageBox.Show(
                $"The serial port '{settings.Port}' used by a saved stream was not found on this system.\n\nSelect a different port in the next dialog, or Cancel to skip restoring that stream.",
                "Serial Port Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);

            SerialConfigWindow window = new SerialConfigWindow(settings) { Owner = this };
            bool? dialogResult = window.ShowDialog();
            if (dialogResult == true)
                return settings;
            else
                return null;
        }

        protected override void OnClosed(EventArgs e)
        {
            // Stop accepting MCP requests before tearing anything down.
            // _mcpServer can genuinely be null here (MCP server disabled or never started).
            _mcpServer?.Dispose();

            writeSettingsToXML(); // Save settings on exit

            // Stop updates
            _plotManager.StopUpdates();
            MeasurementBar.StopUpdates();

            // Dispose components
            _plotManager.Dispose();
            MeasurementBar.Dispose();

            // Dispose DataStreamBar which will handle all stream disposal
            DataStreamBar.Dispose();
            base.OnClosed(e);
        }

        /// <summary>
        /// IMcpHost implementation backed by the live MainWindow.
        /// MCP tool calls arrive on thread pool threads, so every interaction
        /// with UI-owned state is marshalled through the dispatcher.
        /// </summary>
        private class McpWindowHost : IMcpHost
        {
            private readonly MainWindow _window;

            public McpWindowHost(MainWindow window)
            {
                _window = window;
            }

            public IReadOnlyList<Channel> GetChannels()
            {
                return _window.Dispatcher.Invoke(() => _window.DataStreamBar.Channels.ToList());
            }

            public int AddDemoStream(int numberOfChannels, int sampleRate, string signalType)
            {
                return _window.Dispatcher.Invoke(() =>
                {
                    StreamSettings settings = new StreamSettings
                    {
                        StreamSource = StreamSource.Demo,
                        NumberOfChannels = numberOfChannels,
                        DemoSampleRate = sampleRate,
                        DemoSignalType = signalType
                    };

                    IDataStream dataStream = settings.CreateDataStream();
                    dataStream.Connect();
                    dataStream.StartStreaming();

                    _window.DataStreamBar.AddChannelsForStream(dataStream);
                    _window.DataStreamBar.AddStreamInfoPanel(settings, dataStream);

                    return dataStream.ChannelCount;
                });
            }

            public IReadOnlyList<string> LoadConfiguration(string filePath)
            {
                return _window.Dispatcher.Invoke(() =>
                {
                    // No resolveMissingPort callback: this path is driven by an MCP tool call, so
                    // there may be no one watching a modal dialog. Streams with a missing COM port
                    // are skipped and reported back in the returned list instead.
                    return Serializer.LoadSessionFromXML(filePath, _window._plotManager, _window.DataStreamBar);
                });
            }

            public void RemoveAllStreams()
            {
                _window.Dispatcher.Invoke(() =>
                {
                    foreach (IDataStream stream in _window.DataStreamBar.ConnectedDataStreams)
                    {
                        _window.DataStreamBar.RemoveStreamByDataStream(stream);
                    }
                });
            }

            public void RemoveStream(IDataStream stream)
            {
                _window.Dispatcher.Invoke(() =>
                {
                    _window.DataStreamBar.RemoveStreamByDataStream(stream);
                });
            }

            public string ExportPlot(string filePath, int width, int height)
            {
                return _window.Dispatcher.Invoke(() =>
                {
                    if (string.IsNullOrWhiteSpace(filePath))
                        filePath = System.IO.Path.Combine(
                            System.IO.Path.GetTempPath(),
                            $"powerscope_plot_{DateTime.Now:yyyyMMdd_HHmmss}.png");

                    string ext = System.IO.Path.GetExtension(filePath).ToLowerInvariant();
                    if (ext == ".svg")
                        _window.WpfPlot1.Plot.SaveSvg(filePath, width, height);
                    else
                        _window.WpfPlot1.Plot.SavePng(filePath, width, height);

                    return filePath;
                });
            }
        }
    }

    // Simple RelayCommand implementation
    // Needed for keyboard shortcuts and menu commands in the Menubar.xaml file
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            if (execute == null)
                throw new ArgumentNullException(nameof(execute));

            _execute = execute;
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