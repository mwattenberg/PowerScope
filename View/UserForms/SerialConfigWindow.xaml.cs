using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.IO.Ports;
using NAudio.Wave;
using System.Management;
using System.Collections.Generic;
using System.Windows.Input;

namespace SerialPlotDN_WPF.View.UserForms
{
    /// <summary>
    /// Interaction logic for SerialConfigWindow.xaml
    /// </summary>
    public partial class SerialConfigWindow : Window
    {
        private readonly int[] CommonSampleRates = new int[] { 8000, 16000, 22050, 44100, 48000, 96000 };
        readonly List<PortInfo> ports = new List<PortInfo>();

        public SerialConfigWindow()
        {
            InitializeComponent();
            Loaded += SerialConfigWindow_Loaded;
            Loaded += SerialConfigWindow_Loaded_AudioDevices;
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

            // Pre-populate baud rates, allow user to edit
            ComboBox_Baud.ItemsSource = new string[]
            {
                "9600", "19200", "57600", "115200", "256000"
            };
            ComboBox_Baud.IsEditable = true;
            ComboBox_Baud.Text = "115200"; // Default value
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
            foreach (var rate in CommonSampleRates)
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
                if (Radio_Databits8.IsChecked == true) return 8;
                if (Radio_Databits7.IsChecked == true) return 7;
                if (Radio_Databits6.IsChecked == true) return 6;
                if (Radio_Databits5.IsChecked == true) return 5;
                return 8; // Default
            }
            set
            {
                Radio_Databits8.IsChecked = value == 8;
                Radio_Databits7.IsChecked = value == 7;
                Radio_Databits6.IsChecked = value == 6;
                Radio_Databits5.IsChecked = value == 5;
            }
        }

        public int SelectedStopBits
        {
            get
            {
                if (Radio_Stopbits1.IsChecked == true) return 1;
                if (Radio_Stopbits2.IsChecked == true) return 2;
                return 1; // Default
            }
            set
            {
                Radio_Stopbits1.IsChecked = value == 1;
                Radio_Stopbits2.IsChecked = value == 2;
            }
        }

        public Parity SelectedParity
        {
            get
            {
                if (Radio_ParityNone.IsChecked == true) return Parity.None;
                if (Radio_ParityOdd.IsChecked == true) return Parity.Odd;
                if (Radio_ParityEven.IsChecked == true) return Parity.Even;
                return Parity.None; // Default
            }
            set
            {
                Radio_ParityNone.IsChecked = value == Parity.None;
                Radio_ParityOdd.IsChecked = value == Parity.Odd;
                Radio_ParityEven.IsChecked = value == Parity.Even;
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

        private void Button_OK_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SelectedPort) || SelectedBaud == 0)
            {
                MessageBox.Show("Please select a valid port and baud rate.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
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

        private void DataFormatRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (Panel_SimpleBinary == null || Panel_ASCII == null || Panel_CustomFrame == null)
                return;

            Panel_SimpleBinary.Visibility = Radio_SimpleBinary.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            Panel_ASCII.Visibility = Radio_ASCII.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            Panel_CustomFrame.Visibility = Radio_CustomFrame.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    public class PortInfo
    {
        public string Port { get; set; }
        public string Description { get; set; }
        public override string ToString() => Description; // For display in ComboBox
    }
}
