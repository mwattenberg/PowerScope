using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.IO.Ports;
using NAudio.Wave;
using NAudio.CoreAudioApi;
using System.Management;
using System.Collections.Generic;
using System.Windows.Input;
using SerialPlotDN_WPF.Model;

namespace SerialPlotDN_WPF.View.UserForms
{
    /// <summary>
    /// Interaction logic for SerialConfigWindow.xaml
    /// </summary>
    public partial class SerialConfigWindow : Window
    {
        // Common fallback sample rates if device-specific rates can't be determined
        private static readonly int[] FallbackSampleRates = new int[] { 8000, 16000, 22050, 44100, 48000, 96000 };
        
        readonly List<PortInfo> ports = new List<PortInfo>();
        private List<AudioDeviceInfo> audioDevices = new List<AudioDeviceInfo>();
        
        public StreamSettings ViewModel { get; }

        public SerialConfigWindow(StreamSettings viewModel)
        {
            InitializeComponent();
            ViewModel = viewModel;
            this.DataContext = ViewModel;
            
            // Apply the ViewModel values to the window controls
            ViewModel.ApplyToWindow(this);
            
            Loaded += SerialConfigWindow_Loaded;
            Loaded += SerialConfigWindow_Loaded_AudioDevices;
            ComboBox_DataFormat.SelectionChanged += DataFormatCombo_SelectionChanged;
            ComboBox_AudioDevices.SelectionChanged += ComboBox_AudioDevices_SelectionChanged;
            SetRawBinaryPanelVisibility();
            SetASCIIPanelVisibility();
        }

        public SerialConfigWindow() : this(new StreamSettings()) { }

        // Custom window event handlers
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void SerialConfigWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Scan and populate available serial ports with description
            ports.Clear();
            using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'") )
            {
                foreach (var device in searcher.Get())
                {
                    string name = device["Name"]?.ToString();
                    if (name != null)
                    {
                        int start = name.LastIndexOf("(COM");
                        if (start >= 0)
                        {
                            int end = name.IndexOf(")", start);
                            if (end > start)
                            {
                                string port = name.Substring(start + 1, end - start - 1); // e.g., COM3
                                ports.Add(new PortInfo { Port = port, Description = name });
                            }
                        }
                    }
                }
            }
            ComboBox_Port.ItemsSource = ports;
            ComboBox_Port.DisplayMemberPath = "Description";
            ComboBox_Port.SelectedValuePath = "Port";

            // Pre-populate baud rates, allow user to edit
            ComboBox_Baud.ItemsSource = new string[]
            {
                "9600", "19200", "57600", "115200", "256000"
            };
            ComboBox_Baud.IsEditable = true;
        }

        private void SerialConfigWindow_Loaded_AudioDevices(object sender, RoutedEventArgs e)
        {
            LoadAudioDevices();
            UpdateSampleRatesForCurrentDevice();
        }

        private void LoadAudioDevices()
        {
            audioDevices.Clear();
            ComboBox_AudioDevices.Items.Clear();
            
            try
            {
                // Use both WaveIn and WASAPI to get comprehensive device list
                for (int i = 0; i < WaveIn.DeviceCount; i++)
                {
                    var deviceInfo = WaveIn.GetCapabilities(i);
                    var audioDeviceInfo = new AudioDeviceInfo
                    {
                        Index = i,
                        Name = deviceInfo.ProductName,
                        WaveInCapabilities = deviceInfo
                    };
                    
                    // Try to get WASAPI device for more detailed capabilities
                    try
                    {
                        using (var deviceEnumerator = new MMDeviceEnumerator())
                        {
                            var devices = deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
                            var wasapiDevice = devices.FirstOrDefault(d => d.FriendlyName.Contains(deviceInfo.ProductName) || 
                                                                          deviceInfo.ProductName.Contains(d.FriendlyName));
                            if (wasapiDevice != null)
                            {
                                audioDeviceInfo.WasapiDevice = wasapiDevice;
                            }
                        }
                    }
                    catch
                    {
                        // WASAPI device enumeration failed, continue with WaveIn only
                    }
                    
                    audioDevices.Add(audioDeviceInfo);
                    ComboBox_AudioDevices.Items.Add(audioDeviceInfo.Name);
                }
                
                if (ComboBox_AudioDevices.Items.Count > 0)
                    ComboBox_AudioDevices.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading audio devices: {ex.Message}", "Audio Device Error", 
                              MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ComboBox_AudioDevices_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateSampleRatesForCurrentDevice();
        }

        private void UpdateSampleRatesForCurrentDevice()
        {
            ComboBox_SampleRates.Items.Clear();
            
            if (ComboBox_AudioDevices.SelectedIndex < 0 || ComboBox_AudioDevices.SelectedIndex >= audioDevices.Count)
            {
                // No device selected, use fallback rates
                PopulateFallbackSampleRates();
                return;
            }

            var selectedDevice = audioDevices[ComboBox_AudioDevices.SelectedIndex];
            var supportedRates = GetSupportedSampleRates(selectedDevice);
            
            if (supportedRates.Any())
            {
                foreach (var rate in supportedRates.OrderBy(r => r))
                {
                    ComboBox_SampleRates.Items.Add(rate.ToString());
                }
                
                // Select a reasonable default (prefer 44100 or 48000)
                var preferredRates = new[] { 44100, 48000, 22050, 16000 };
                string selectedRate = null;
                foreach (var preferred in preferredRates)
                {
                    if (supportedRates.Contains(preferred))
                    {
                        selectedRate = preferred.ToString();
                        break;
                    }
                }
                
                // If no preferred rate found, select the middle one
                if (selectedRate == null && supportedRates.Any())
                {
                    var sortedRates = supportedRates.OrderBy(r => r).ToList();
                    selectedRate = sortedRates[sortedRates.Count / 2].ToString();
                }
                
                ComboBox_SampleRates.SelectedItem = selectedRate ?? "44100";
            }
            else
            {
                // Couldn't determine supported rates, use fallback
                PopulateFallbackSampleRates();
            }
        }

        private void PopulateFallbackSampleRates()
        {
            foreach (var rate in FallbackSampleRates)
            {
                ComboBox_SampleRates.Items.Add(rate.ToString());
            }
            ComboBox_SampleRates.SelectedItem = "44100";
        }

        private HashSet<int> GetSupportedSampleRates(AudioDeviceInfo deviceInfo)
        {
            var supportedRates = new HashSet<int>();
            
            try
            {
                // Test common sample rates to see what the device supports
                var testRates = new int[] { 8000, 11025, 16000, 22050, 32000, 44100, 48000, 88200, 96000, 176400, 192000 };
                
                if (deviceInfo.WasapiDevice != null)
                {
                    // Use WASAPI to test supported formats
                    try
                    {
                        using (var audioClient = deviceInfo.WasapiDevice.AudioClient)
                        {
                            var mixFormat = audioClient.MixFormat;
                            
                            foreach (var rate in testRates)
                            {
                                try
                                {
                                    var testFormat = new WaveFormat(rate, mixFormat.BitsPerSample, mixFormat.Channels);
                                    
                                    // Test if this format is supported
                                    if (audioClient.IsFormatSupported(AudioClientShareMode.Shared, testFormat))
                                    {
                                        supportedRates.Add(rate);
                                    }
                                }
                                catch
                                {
                                    // Format not supported, continue
                                }
                            }
                        }
                    }
                    catch
                    {
                        // WASAPI testing failed, continue with fallback
                    }
                }
                
                // If WASAPI didn't yield results or we don't have WASAPI device, add common rates
                if (!supportedRates.Any())
                {
                    // Add standard rates that most audio devices support
                    supportedRates.UnionWith(new[] { 8000, 16000, 22050, 44100, 48000 });
                    
                    // If we have WaveIn capabilities, we can at least verify the device exists
                    if (deviceInfo.WaveInCapabilities.HasValue)
                    {
                        // Device exists via WaveIn, so these rates are likely supported
                        supportedRates.UnionWith(new[] { 11025, 32000, 96000 });
                    }
                }
                
                // If still no rates found, assume device supports common rates
                if (!supportedRates.Any())
                {
                    supportedRates.UnionWith(FallbackSampleRates);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting supported sample rates: {ex.Message}");
                // Return common rates as fallback
                supportedRates.UnionWith(FallbackSampleRates);
            }
            
            return supportedRates;
        }

        public string SelectedPort
        {
            get => ComboBox_Port.SelectedValue as string;
            set { ComboBox_Port.SelectedValue = value; }
        }

        public int SelectedBaud
        {
            get
            {
                int.TryParse(ComboBox_Baud.Text, out int baud);
                return baud;
            }
            set { ComboBox_Baud.Text = value.ToString(); }
        }

        public int SelectedDataBits
        {
            get
            {
                if (ComboBox_Databits.SelectedItem is ComboBoxItem item && int.TryParse(item.Content.ToString(), out int bits))
                    return bits;
                if (ComboBox_Databits.SelectedItem is string str && int.TryParse(str, out bits))
                    return bits;
                return 8; // Default
            }
            set
            {
                foreach (var obj in ComboBox_Databits.Items)
                {
                    if (obj is ComboBoxItem item && int.TryParse(item.Content.ToString(), out int bits) && bits == value)
                    {
                        ComboBox_Databits.SelectedItem = item;
                        return;
                    }
                    if (obj is string str && int.TryParse(str, out bits) && bits == value)
                    {
                        ComboBox_Databits.SelectedItem = obj;
                        return;
                    }
                }
            }
        }

        public int SelectedStopBits
        {
            get
            {
                if (ComboBox_Stopbits.SelectedItem is ComboBoxItem item && int.TryParse(item.Content.ToString(), out int bits))
                    return bits;
                if (ComboBox_Stopbits.SelectedItem is string str && int.TryParse(str, out bits))
                    return bits;
                return 1; // Default
            }
            set
            {
                foreach (var obj in ComboBox_Stopbits.Items)
                {
                    if (obj is ComboBoxItem item && int.TryParse(item.Content.ToString(), out int bits) && bits == value)
                    {
                        ComboBox_Stopbits.SelectedItem = item;
                        return;
                    }
                    if (obj is string str && int.TryParse(str, out bits) && bits == value)
                    {
                        ComboBox_Stopbits.SelectedItem = obj;
                        return;
                    }
                }
            }
        }

        public Parity SelectedParity
        {
            get
            {
                string parityStr = null;
                if (ComboBox_Parity.SelectedItem is ComboBoxItem item)
                    parityStr = item.Content.ToString();
                else if (ComboBox_Parity.SelectedItem is string str)
                    parityStr = str;
                    
                if (parityStr == "None")
                    return Parity.None;
                else if (parityStr == "Odd")
                    return Parity.Odd;
                else if (parityStr == "Even")
                    return Parity.Even;
                else
                    return Parity.None;
            }
            set
            {
                string parityStr;
                if (value == Parity.None)
                    parityStr = "None";
                else if (value == Parity.Odd)
                    parityStr = "Odd";
                else if (value == Parity.Even)
                    parityStr = "Even";
                else
                    parityStr = "None";
                    
                foreach (object obj in ComboBox_Parity.Items)
                {
                    if (obj is ComboBoxItem item && item.Content.ToString() == parityStr)
                    {
                        ComboBox_Parity.SelectedItem = item;
                        return;
                    }
                    if (obj is string str && str == parityStr)
                    {
                        ComboBox_Parity.SelectedItem = obj;
                        return;
                    }
                }
            }
        }

        public string SelectedAudioDevice
        {
            get => ComboBox_AudioDevices.SelectedItem as string;
            set { ComboBox_AudioDevices.SelectedItem = value; }
        }

        public int SelectedAudioDeviceIndex
        {
            get => ComboBox_AudioDevices.SelectedIndex;
            set { ComboBox_AudioDevices.SelectedIndex = value; }
        }

        public int SelectedSampleRate
        {
            get
            {
                if (ComboBox_SampleRates.SelectedItem is string selected &&
                    int.TryParse(selected, out int rate))
                {
                    return rate;
                }
                return 44100; // Default fallback
            }
            set { ComboBox_SampleRates.SelectedItem = value.ToString(); }
        }

        private void Button_Connect_Click(object sender, RoutedEventArgs e)
        {
            // Validate based on the selected stream source
            switch (ViewModel.StreamSource)
            {
                case StreamSource.SerialPort:
                    // Only validate serial port settings for serial streams
                    if (string.IsNullOrWhiteSpace(SelectedPort) || SelectedBaud == 0)
                    {
                        MessageBox.Show("Please select a valid port and baud rate.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    break;
                    
                case StreamSource.AudioInput:
                    // Validate audio settings
                    if (string.IsNullOrWhiteSpace(SelectedAudioDevice))
                    {
                        MessageBox.Show("Please select a valid audio device.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    break;
                    
                case StreamSource.Demo:
                    // Demo mode requires minimal validation
                    if (ViewModel.NumberOfChannels <= 0)
                    {
                        MessageBox.Show("Please specify a valid number of channels.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    if (ViewModel.DemoSampleRate <= 0)
                    {
                        MessageBox.Show("Please specify a valid sample rate.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    break;
                    
                case StreamSource.USB:
                    // USB validation would go here when implemented
                    MessageBox.Show("USB streams are not yet implemented.", "Not Implemented", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                    
                default:
                    MessageBox.Show("Please select a valid stream type.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
            }
            
            DialogResult = true;
            Close();
        }

        private void Button_Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void DataFormatCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SetRawBinaryPanelVisibility();
            SetASCIIPanelVisibility();
        }

        private void SetRawBinaryPanelVisibility()
        {
            if (Panel_RawBinary != null)
                Panel_RawBinary.Visibility = ViewModel.DataFormat == DataFormatType.RawBinary ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SetASCIIPanelVisibility()
        {
            if (Panel_ASCII != null)
                Panel_ASCII.Visibility = ViewModel.DataFormat == DataFormatType.ASCII ? Visibility.Visible : Visibility.Collapsed;
        }

        private void PopulateDemoControls()
        {
            // Demo controls are already defined in XAML, just ensure they're bound properly
            // The binding is handled through the DataContext (ViewModel)
        }

        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ViewModel == null || !(sender is TabControl tabControl))
                return;

            if (tabControl.SelectedItem is TabItem selectedTab)
            {
                // Update StreamSource based on selected tab
                if (selectedTab.Name == "TabItem_Serial")
                {
                    ViewModel.StreamSource = StreamSource.SerialPort;
                }
                else if (selectedTab.Name == "TabItem_Audio")
                {
                    ViewModel.StreamSource = StreamSource.AudioInput;
                }
                else if (selectedTab.Name == "TabItem_Demo")
                {
                    ViewModel.StreamSource = StreamSource.Demo;
                }
                // USB tab would set StreamSource.USB when implemented
            }
        }
    }

    public class PortInfo
    {
        public string Port { get; set; }
        public string Description { get; set; }
        public override string ToString() => Description; // For display in ComboBox
    }

    public class AudioDeviceInfo
    {
        public int Index { get; set; }
        public string Name { get; set; }
        public WaveInCapabilities? WaveInCapabilities { get; set; }
        public MMDevice WasapiDevice { get; set; }
    }
}
