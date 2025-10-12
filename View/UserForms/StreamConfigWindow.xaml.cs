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
using System.Text.RegularExpressions;
using PowerScope.Model;
using Microsoft.Win32;

namespace PowerScope.View.UserForms
{
    /// <summary>
    /// Interaction logic for SerialConfigWindow.xaml
    /// </summary>
    public partial class SerialConfigWindow : Window
    {
        // Common fallback sample rates if device-specific rates can't be determined
        private static readonly int[] FallbackAudioSampleRates = new int[] { 8000, 16000, 22050, 44100, 48000, 96000 };

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
            
            // Wire up demo signal type selection change to update the info text
            ComboBox_DemoSignalType.SelectionChanged += ComboBox_DemoSignalType_SelectionChanged;

            // Initialize demo info text based on the currently selected demo signal type
            UpdateDemoInfoText();
            
            // Automatically select the appropriate tab based on the configured StreamSource
            SelectTabBasedOnStreamSource();
        }

        public SerialConfigWindow() : this(new StreamSettings()) { }

        /// <summary>
        /// Automatically selects the appropriate tab based on the StreamSource in the ViewModel
        /// This allows users to directly edit their previously configured stream type
        /// </summary>
        private void SelectTabBasedOnStreamSource()
        {
            if (TabControl_StreamTypes == null || ViewModel == null)
                return;

            // Map StreamSource enum to the corresponding tab
            TabItem targetTab = ViewModel.StreamSource switch
            {
                StreamSource.SerialPort => TabItem_Serial,
                StreamSource.AudioInput => TabItem_Audio,
                StreamSource.Demo => TabItem_Demo,
                StreamSource.File => TabItem_File,
                StreamSource.USB => null, // USB tab exists but is not implemented yet
                _ => TabItem_Serial // Default fallback to Serial tab
            };

            // Select the target tab if it exists
            if (targetTab != null)
            {
                TabControl_StreamTypes.SelectedItem = targetTab;
            }
        }

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
            foreach (var rate in FallbackAudioSampleRates)
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
                    supportedRates.UnionWith(FallbackAudioSampleRates);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting supported sample rates: {ex.Message}");
                // Return common rates as fallback
                supportedRates.UnionWith(FallbackAudioSampleRates);
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
                var textBox = this.FindName("TextBox_Baud") as TextBox;
                if (textBox != null && int.TryParse(textBox.Text, out int baud))
                    return baud;
                return 0;
            }
            set 
            { 
                var textBox = this.FindName("TextBox_Baud") as TextBox;
                if (textBox != null)
                    textBox.Text = value.ToString(); 
            }
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
                    
                case StreamSource.File:
                    // Validate file settings
                    if (string.IsNullOrWhiteSpace(ViewModel.FilePath))
                    {
                        MessageBox.Show("Please select a data file.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    if (!System.IO.File.Exists(ViewModel.FilePath))
                    {
                        MessageBox.Show("The selected file does not exist.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    if (ViewModel.NumberOfChannels <= 0)
                    {
                        MessageBox.Show("The file appears to have no valid channels. Please check the file format.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
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

        private void Button_BrowseFile_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openDialog = new OpenFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|Text files (*.txt)|*.txt|All files (*.*)|*.*",
                DefaultExt = "csv",
                Title = "Select Data File"
            };

            if (openDialog.ShowDialog() == true)
            {
                ViewModel.FilePath = openDialog.FileName;
                
                // Parse the file header automatically
                if (ViewModel.ParseFileHeader(openDialog.FileName))
                {
                    // File parsed successfully - UI will update automatically via data binding
                }
                else
                {
                    MessageBox.Show($"Failed to parse file: {ViewModel.FileParseStatus}", 
                        "File Parse Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
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
                else if (selectedTab.Name == "TabItem_File")
                {
                    ViewModel.StreamSource = StreamSource.File;
                }
                // USB tab would set StreamSource.USB when implemented
            }
        }

        // Input validation methods
        private void NumbersOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // Allow only digits
            e.Handled = !IsNumeric(e.Text);
        }

        private void BaudOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            var textBox = sender as TextBox;
            var newText = textBox.Text.Insert(textBox.SelectionStart, e.Text);
            // Allow numbers and 'e' for scientific notation (e.g., 1e6 for 1000000)
            e.Handled = !IsBaudRate(newText);
        }

        private static bool IsNumeric(string text)
        {
            return Regex.IsMatch(text, @"^[0-9]+$");
        }

        private static bool IsBaudRate(string text)
        {
            // Allow empty string (for typing)
            if (string.IsNullOrEmpty(text))
                return true;
                
            // Allow numbers and one 'e' or 'E' for scientific notation
            // Examples: 115200, 1e6, 2e5, 1E6
            
            // Basic pattern: digits, optional decimal point, more digits, optional e/E, optional digits
            if (!Regex.IsMatch(text, @"^[0-9]*\.?[0-9]*[eE]?[0-9]*$"))
                return false;
                
            // Additional validations
            if (text == "." || text == "e" || text == "E" || text.EndsWith(".."))
                return false;
                
            // Check for multiple 'e' or 'E'
            var lowerText = text.ToLower();
            if (lowerText.Count(c => c == 'e') > 1)
                return false;
                
            // Don't allow 'e' at the beginning
            if (text.StartsWith("e") || text.StartsWith("E"))
                return false;
                
            return true;
        }

        // New: update demo info text based on selected demo signal type
        private void ComboBox_DemoSignalType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateDemoInfoText();
        }

        private void UpdateDemoInfoText()
        {
            if (InfoTextBlock == null || ComboBox_DemoSignalType == null)
                return;

            string selected = null;
            // SelectedValuePath is set to "Content" in XAML, so SelectedValue should be the display string
            if (ComboBox_DemoSignalType.SelectedValue != null)
                selected = ComboBox_DemoSignalType.SelectedValue.ToString();
            else if (ComboBox_DemoSignalType.SelectedItem is ComboBoxItem cbi && cbi.Content != null)
                selected = cbi.Content.ToString();

            if (string.IsNullOrEmpty(selected))
            {
                InfoTextBlock.Text = string.Empty;
                return;
            }

            switch (selected.Trim().ToLowerInvariant())
            {
                case "sine wave":
                    InfoTextBlock.Text = "Generates a 1Hz sine wave for the first channel.\nFrequency will increase successively on other channels.";
                    break;
                case "square wave":
                    InfoTextBlock.Text = "Generates a 1Hz square wave on CH1.\nFrequency will increase successively on other channels.";
                    break;
                case "triangle wave":
                    InfoTextBlock.Text = "Generates a 1Hz triangle wave on CH1.\nFrequency will increase successively on other channels.";
                    break;
                case "random noise":
                    InfoTextBlock.Text = "Generates white noise.";
                    break;
                case "mixed signals":
                    InfoTextBlock.Text = "Generates a combination of sine, square, triangle nosie.";
                    break;
                case "chirp signal":
                case "chirp":
                    InfoTextBlock.Text = "Sweeps a sine wave from 100Hz to 10kHz.\nA sample rate of 20kHz or higher is recommended.";
                    break;
                case "tones":
                    InfoTextBlock.Text = "Generates a 1kHz main sine wave with secondary sine waves at 750 and 1250Hz.\nA sample rate of 10kHz or higher is recommended.\nSecondary waves will successively move closer to 1kHz.";
                    break;
                case "sin(x)/x":
                    InfoTextBlock.Text = "Generates a 400Hz sine wave for testing sin(x)/x interpolation.\nFrequency is just below Nyquist for 1000Hz sampling.\nEach channel gets a slight frequency offset (+5Hz per channel).";
                    break;
                default:
                    InfoTextBlock.Text = string.Empty;
                    break;
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

        // Sampling factor validation and event handlers
        private void SamplingFactor_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            var textBox = sender as TextBox;
            var newText = textBox.Text.Insert(textBox.SelectionStart, e.Text);
            
            // Allow empty string, minus sign at start, and numbers
            if (string.IsNullOrEmpty(newText) || newText == "-")
            {
                e.Handled = false;
                return;
            }
            
            // Check if it's a valid integer between -9 and 9
            if (int.TryParse(newText, out int value))
            {
                e.Handled = value < -9 || value > 9;
            }
            else
            {
                e.Handled = true;
            }
        }

        private void SamplingFactor_TextChanged(object sender, TextChangedEventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox == null) return;
            
            // Validate and clamp the value when text changes
            if (int.TryParse(textBox.Text, out int value))
            {
                var clampedValue = Math.Max(-9, Math.Min(9, value));
                if (value != clampedValue)
                {
                    textBox.Text = clampedValue.ToString();
                    textBox.SelectionStart = textBox.Text.Length; // Move cursor to end
                }
            }
            else if (!string.IsNullOrEmpty(textBox.Text) && textBox.Text != "-")
            {
                // Invalid input, reset to 0
                textBox.Text = "0";
                textBox.SelectionStart = textBox.Text.Length;
            }
        }

        private void Button_SamplingUp_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null && ViewModel.UpDownSampling < 9)
            {
                ViewModel.UpDownSampling++;
            }
        }

        private void Button_SamplingDown_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null && ViewModel.UpDownSampling > -9)
            {
                ViewModel.UpDownSampling--;
            }
        }
    }
}
