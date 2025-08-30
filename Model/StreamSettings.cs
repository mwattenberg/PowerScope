using System.ComponentModel;
using RJCP.IO.Ports; // Changed from System.IO.Ports to RJCP.IO.Ports for Parity enum
using System.Windows.Media; // Add for SolidColorBrush and Colors
using System; // Add for IDisposable
using System.Collections.Generic; // Add for List<T>
using System.Windows.Controls; // Add for ComboBoxItem

namespace SerialPlotDN_WPF.Model
{
    public enum DataFormatType
    {
        RawBinary,
        ASCII
    }

    public enum StreamSource
    {
        SerialPort,
        AudioInput,
        USB,
        Demo
    }

    public enum NumberTypeEnum
    {
        Uint8,
        Int8,
        Uint16,
        Int16,
        Uint32,
        Int32,
        Float
    }

    public class StreamSettings : INotifyPropertyChanged
    {
        private string _port;
        private int _baud;
        private int _dataBits;
        private int _stopBits;
        private Parity _parity;
        private string _audioDevice;
        private int _audioDeviceIndex;
        private int _sampleRate;
        private bool _enableChecksum;
        private DataFormatType _dataFormat;
        private StreamSource _streamSource;
        private int _numberOfChannels;
        private NumberTypeEnum _numberType; // enum property
        private string _endianness; // "LittleEndian", "BigEndian"
        private string _delimiter; // "Comma", "Space", "Tab", or custom
        private string _frameStart; // for CustomFrame
        private int _demoSampleRate; // for Demo mode
        private string _demoSignalType; // for Demo mode

        public StreamSettings()
        {
            DataBits = 8;
            StopBits = 1;
            Parity = Parity.None;
            NumberOfChannels = 8;
            DataFormat = DataFormatType.RawBinary;
            FrameStart = "AA AA"; // Default frame start bytes as string representation
            DemoSampleRate = 1000; // Default demo sample rate
            DemoSignalType = "Sine Wave"; // Default demo signal type
            StreamSource = StreamSource.SerialPort; // Default to serial port
        }

        // Removed Connect/Disconnect methods
        // Only keep configuration properties and INotifyPropertyChanged

        // Dataformat Tab properties
        public DataFormatType DataFormat
        {
            get 
            { 
                return _dataFormat; 
            }
            set
            {
                if (_dataFormat != value)
                {
                    _dataFormat = value;
                    OnPropertyChanged(nameof(DataFormat));
                }
            }
        }

        public string Port
        {
            get { return _port; }
            set
            {
                _port = value;
                OnPropertyChanged(nameof(Port));
                // Removed PortAndBaudDisplay notification
            }
        }

        public int Baud
        {
            get { return _baud; }
            set
            {
                _baud = value;
                OnPropertyChanged(nameof(Baud));
                // Removed PortAndBaudDisplay notification
            }
        }

        public int DataBits
        {
            get { return _dataBits; }
            set
            {
                _dataBits = value;
                OnPropertyChanged(nameof(DataBits));
            }
        }

        public int StopBits
        {
            get { return _stopBits; }
            set
            {
                _stopBits = value;
                OnPropertyChanged(nameof(StopBits));
            }
        }

        public Parity Parity
        {
            get { return _parity; }
            set
            {
                _parity = value;
                OnPropertyChanged(nameof(Parity));
            }
        }

        public string AudioDevice
        {
            get { return _audioDevice; }
            set
            {
                _audioDevice = value;
                OnPropertyChanged(nameof(AudioDevice));
            }
        }

        public int AudioDeviceIndex
        {
            get { return _audioDeviceIndex; }
            set
            {
                _audioDeviceIndex = value;
                OnPropertyChanged(nameof(AudioDeviceIndex));
            }
        }

        public int AudioSampleRate
        {
            get { return _sampleRate; }
            set
            {
                _sampleRate = value;
                OnPropertyChanged(nameof(AudioSampleRate));
            }
        }

        public bool EnableChecksum
        {
            get 
            { 
                return _enableChecksum; 
            }
            set 
            { 
                _enableChecksum = value; 
                OnPropertyChanged(nameof(EnableChecksum)); 
            }
        }

        public int NumberOfChannels
        {
            get
            {
                return _numberOfChannels;
            }
            set
            {
                _numberOfChannels = value;
                OnPropertyChanged(nameof(NumberOfChannels));
            }
        }
        
        public NumberTypeEnum NumberType
        {
            get 
            { 
                return _numberType; 
            }
            set 
            { 
                _numberType = value; 
                OnPropertyChanged(nameof(NumberType)); 
            }
        }
        
        public string Endianness
        {
            get 
            { 
                return _endianness; 
            }
            set 
            { 
                _endianness = value; 
                OnPropertyChanged(nameof(Endianness)); 
            }
        }
        
        public string Delimiter
        {
            get 
            { 
                return _delimiter; 
            }
            set 
            { 
                _delimiter = value; 
                OnPropertyChanged(nameof(Delimiter)); 
            }
        }
        
        public string FrameStart
        {
            get 
            { 
                return _frameStart; 
            }
            set 
            { 
                _frameStart = value; 
                OnPropertyChanged(nameof(FrameStart)); 
            }
        }

        public int DemoSampleRate
        {
            get { return _demoSampleRate; }
            set
            {
                _demoSampleRate = value;
                OnPropertyChanged(nameof(DemoSampleRate));
            }
        }

        public string DemoSignalType
        {
            get { return _demoSignalType; }
            set
            {
                _demoSignalType = value;
                OnPropertyChanged(nameof(DemoSignalType));
            }
        }

        public StreamSource StreamSource
        {
            get { return _streamSource; }
            set
            {
                _streamSource = value;
                OnPropertyChanged(nameof(StreamSource));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
                PropertyChanged.Invoke(this, new PropertyChangedEventArgs(propertyName));
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
            window.SelectedSampleRate = this.AudioSampleRate;
            // Note: SelectedNumberType exists in the code-behind, keeping it
            if (window.ComboBox_NumberType != null)
            {
                foreach (var obj in window.ComboBox_NumberType.Items)
                {
                    if (obj is ComboBoxItem item && item.Tag?.ToString() == this.NumberType.ToString())
                    {
                        window.ComboBox_NumberType.SelectedItem = item;
                        break;
                    }
                }
            }
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
            this.AudioSampleRate = window.SelectedSampleRate;
            // Get NumberType from the ComboBox
            if (window.ComboBox_NumberType?.SelectedItem is ComboBoxItem selectedItem && 
                Enum.TryParse<NumberTypeEnum>(selectedItem.Tag?.ToString(), out var numberType))
            {
                this.NumberType = numberType;
            }
            // Demo properties are handled by data binding automatically
        }
    }
}
