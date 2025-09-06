using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.IO.Ports;
using NAudio.Wave;
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
        private readonly int[] CommonAudioSampleRates = new int[] { 8000, 16000, 22050, 44100, 48000, 96000 };
        readonly List<PortInfo> ports = new List<PortInfo>();
        public StreamSettings ViewModel { get; }

        public SerialConfigWindow(StreamSettings viewModel)
        {
            InitializeComponent();
            ViewModel = viewModel;
            this.DataContext = ViewModel;
            Loaded += SerialConfigWindow_Loaded;
            Loaded += SerialConfigWindow_Loaded_AudioDevices;
            ComboBox_DataFormat.SelectionChanged += DataFormatCombo_SelectionChanged;
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
            //ComboBox_Baud.Text = "115200"; // Default value
        }

        private void SerialConfigWindow_Loaded_AudioDevices(object sender, RoutedEventArgs e)
        {
            ComboBox_AudioDevices.Items.Clear();
            for (int i = 0; i < WaveIn.DeviceCount; i++)
            {
                var deviceInfo = WaveIn.GetCapabilities(i);
                ComboBox_AudioDevices.Items.Add(deviceInfo.ProductName);
            }
            if (ComboBox_AudioDevices.Items.Count > 0)
                ComboBox_AudioDevices.SelectedIndex = 0;

            // Populate sample rates
            ComboBox_SampleRates.Items.Clear();
            foreach (var rate in CommonAudioSampleRates)
            {
                ComboBox_SampleRates.Items.Add(rate.ToString());
            }
            ComboBox_SampleRates.SelectedItem = "44100";
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
}
