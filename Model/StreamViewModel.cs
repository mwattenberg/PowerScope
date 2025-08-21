using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using System.ComponentModel;
using System.IO.Ports; // Add for Parity enum

namespace SerialPlotDN_WPF.Model
{
    public class DataStreamViewModel : INotifyPropertyChanged
    {
        private string port;
        private int baud;
        private bool isConnected;
        private string statusMessage;
        private int dataBits;
        private int stopBits;
        private Parity parity;
        private string audioDevice;
        private int audioDeviceIndex;
        private int sampleRate;
        private bool enableChecksum;

        // Dataformat Tab properties
        private bool _isRawBinary;
        public bool IsRawBinary
        {
            get => _isRawBinary;
            set
            {
                if (_isRawBinary != value)
                {
                    _isRawBinary = value;
                    OnPropertyChanged(nameof(IsRawBinary));
                    OnPropertyChanged(nameof(IsASCII));
                }
            }
        }
        public bool IsASCII
        {
            get => !_isRawBinary;
            set
            {
                IsRawBinary = !value;
            }
        }
        private int numberOfChannels;
        private string numberType; // "Uint8", "Uint16", etc.
        private string endianness; // "LittleEndian", "BigEndian"
        private string delimiter; // "Comma", "Space", "Tab", or custom
        private string frameStart; // for CustomFrame

        public string Port
        {
            get { return port; }
            set
            {
                port = value;
                OnPropertyChanged(nameof(Port));
                OnPropertyChanged(nameof(PortAndBaudDisplay));
            }
        }

        public int Baud
        {
            get { return baud; }
            set
            {
                baud = value;
                OnPropertyChanged(nameof(Baud));
                OnPropertyChanged(nameof(PortAndBaudDisplay));
            }
        }

        public int DataBits
        {
            get { return dataBits; }
            set
            {
                dataBits = value;
                OnPropertyChanged(nameof(DataBits));
            }
        }

        public int StopBits
        {
            get { return stopBits; }
            set
            {
                stopBits = value;
                OnPropertyChanged(nameof(StopBits));
            }
        }

        public Parity Parity
        {
            get { return parity; }
            set
            {
                parity = value;
                OnPropertyChanged(nameof(Parity));
            }
        }

        public string AudioDevice
        {
            get { return audioDevice; }
            set
            {
                audioDevice = value;
                OnPropertyChanged(nameof(AudioDevice));
            }
        }

        public int AudioDeviceIndex
        {
            get { return audioDeviceIndex; }
            set
            {
                audioDeviceIndex = value;
                OnPropertyChanged(nameof(AudioDeviceIndex));
            }
        }

        public int SampleRate
        {
            get { return sampleRate; }
            set
            {
                sampleRate = value;
                OnPropertyChanged(nameof(SampleRate));
            }
        }

        public bool EnableChecksum
        {
            get => enableChecksum;
            set { enableChecksum = value; OnPropertyChanged(nameof(EnableChecksum)); }
        }

        public bool IsConnected
        {
            get { return isConnected; }
            set
            {
                isConnected = value;
                OnPropertyChanged(nameof(IsConnected));
                UpdateStatusMessage();
            }
        }

        public string StatusMessage
        {
            get { return statusMessage; }
            set
            {
                statusMessage = value;
                OnPropertyChanged(nameof(StatusMessage));
            }
        }

        public string PortAndBaudDisplay => $"Port: {Port}\nBaud: {Baud}";

        public int NumberOfChannels
        {
            get => numberOfChannels;
            set { numberOfChannels = value; OnPropertyChanged(nameof(NumberOfChannels)); }
        }
        public string NumberType
        {
            get => numberType;
            set { numberType = value; OnPropertyChanged(nameof(NumberType)); }
        }
        public string Endianness
        {
            get => endianness;
            set { endianness = value; OnPropertyChanged(nameof(Endianness)); }
        }
        public string Delimiter
        {
            get => delimiter;
            set { delimiter = value; OnPropertyChanged(nameof(Delimiter)); }
        }
        public string FrameStart
        {
            get => frameStart;
            set { frameStart = value; OnPropertyChanged(nameof(FrameStart)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void UpdateStatusMessage()
        {
            StatusMessage = IsConnected ? "Connected" : "Disconnected";
        }

        public void ApplyToWindow(View.UserForms.SerialConfigWindow window)
        {
            window.SelectedPort = this.Port;
            window.SelectedBaud = this.Baud;
            window.SelectedDataBits = this.DataBits;
            window.SelectedStopBits = this.StopBits;
            window.SelectedParity = this.Parity;
            window.SelectedAudioDevice = this.AudioDevice;
            window.SelectedAudioDeviceIndex = this.AudioDeviceIndex;
            window.SelectedSampleRate = this.SampleRate;
        }

        public void UpdateFromWindow(View.UserForms.SerialConfigWindow window)
        {
            this.Port = window.SelectedPort;
            this.Baud = window.SelectedBaud;
            this.DataBits = window.SelectedDataBits;
            this.StopBits = window.SelectedStopBits;
            this.Parity = window.SelectedParity;
            this.AudioDevice = window.SelectedAudioDevice;
            this.AudioDeviceIndex = window.SelectedAudioDeviceIndex;
            this.SampleRate = window.SelectedSampleRate;
        }
    }
}
