using System.ComponentModel;
using RJCP.IO.Ports; // Changed from System.IO.Ports to RJCP.IO.Ports for Parity enum
using System.Windows.Media; // Add for SolidColorBrush and Colors
using System; // Add for IDisposable
using System.Collections.Generic; // Add for List<T>

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
        USB
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

    public class StreamViewModel : INotifyPropertyChanged, IDisposable
    {
        private string _port;
        private int _baud;
        //private bool _isConnected;
        private string _statusMessage;
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

        // SerialDataStream instance
        private IDataStream _dataStream;

        public StreamViewModel()
        {
            StatusMessage = "Disconnected";
            DataBits = 8;
            StopBits = 1;
            Parity = Parity.None;
            NumberOfChannels = 8;
            DataFormat = DataFormatType.RawBinary;
            FrameStart = "AA AA"; // Default frame start bytes as string representation
        }

        /// <summary>
        /// Gets the current SerialDataStream instance (read-only)
        /// </summary>
        public IDataStream SerialDataStream 
        { 
            get 
            { 
                return _dataStream; 
            } 
        }

        /// <summary>
        /// Connects to the serial port using the current configuration
        /// /// <returns>True if connection was successful, false otherwise</returns>
        public bool Connect()
        {
            if (_dataStream != null && _dataStream.IsConnected)
                return true; // Already connected

            // Validate port name before attempting connection
            if (string.IsNullOrWhiteSpace(Port))
            {
                StatusMessage = "Please select a valid port";
                return false;
            }

            // Create SourceSetting based on current properties
            SourceSetting sourceSetting = new SourceSetting(Port, Baud, DataBits, StopBits, Parity);

            // Create DataParser based on current configuration
            DataParser dataParser;
            if (DataFormat == DataFormatType.RawBinary)
            {
                // Create binary parser - using uint16_t as default, with frame start bytes
                byte[] frameStartBytes = ParseFrameStartBytes(FrameStart);
                if (frameStartBytes != null && frameStartBytes.Length > 0)
                {
                    dataParser = new DataParser(DataParser.BinaryFormat.uint16_t, NumberOfChannels, frameStartBytes);
                }
                else
                {
                    dataParser = new DataParser(DataParser.BinaryFormat.uint16_t, NumberOfChannels);
                }
            }
            else
            {
                // Create ASCII parser
                char frameEnd = '\n'; // Default line ending
                char separator = ParseDelimiter(Delimiter);
                dataParser = new DataParser(NumberOfChannels, frameEnd, separator);
            }

            // Create and configure SerialDataStream
            _dataStream = new SerialDataStream(sourceSetting, dataParser);

            _dataStream.Connect();
            
            // Start the data stream
            _dataStream.StartStreaming();

            // Update connection state
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(StatusMessage));

            return _dataStream.IsConnected;
        }

        /// <summary>
        /// Disconnects from the serial port and disposes resources
        /// /// </summary>
        public void Disconnect()
        {
            if (_dataStream == null || !_dataStream.IsConnected)
                return; // Already disconnected

            
            _dataStream.StopStreaming();
            _dataStream.Disconnect();
            _dataStream.Dispose();
            
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(StatusMessage));
        }

        /// <summary>
        /// Parses frame start string into byte array
        /// </summary>
        private byte[] ParseFrameStartBytes(string frameStart)
        {
            if (string.IsNullOrEmpty(frameStart))
            {
                return new byte[] { 0xAA, 0xAA }; // Default frame start
            }

            try
            {
                // Try to parse as hex values (e.g., "AA AA" or "0xAA 0xAA")
                string[] parts = frameStart.Split(new char[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                List<byte> bytes = new List<byte>();
                
                foreach (string part in parts)
                {
                    string cleanPart = part.Trim().Replace("0x", "").Replace("0X", "");
                    if (byte.TryParse(cleanPart, System.Globalization.NumberStyles.HexNumber, null, out byte b))
                    {
                        bytes.Add(b);
                    }
                }

                if (bytes.Count > 0)
                    return bytes.ToArray();
                else
                    return new byte[] { 0xAA, 0xAA };
            }
            catch
            {
                return new byte[] { 0xAA, 0xAA }; // Default on parse error
            }
        }

        /// <summary>
        /// Parses delimiter string into character
        /// </summary>
        private char ParseDelimiter(string delimiter)
        {
            if (string.IsNullOrEmpty(delimiter))
            {
                return ','; // Default comma
            }

            string lowerDelimiter = delimiter.ToLower();
            if (lowerDelimiter == "comma" || lowerDelimiter == ",")
                return ',';
            else if (lowerDelimiter == "space" || lowerDelimiter == " ")
                return ' ';
            else if (lowerDelimiter == "tab" || lowerDelimiter == "\t")
                return '\t';
            else if (lowerDelimiter == "semicolon" || lowerDelimiter == ";")
                return ';';
            else
                return delimiter[0]; // Use first character if not recognized
        }

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

        public bool IsConnected
        {
            get
            {
                if (_dataStream != null)
                    return _dataStream.IsConnected;
                else
                    return false;
            }

        }

        public string StatusMessage
        {
            get
            {
                if (_dataStream != null)
                    return _dataStream.StatusMessage;
                else
                    return _statusMessage;
            }
            set
            {
                if (_dataStream == null)
                    _statusMessage = value;

                OnPropertyChanged(nameof(StatusMessage));
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

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
                PropertyChanged.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Disposes of the SerialDataStream resources
        /// /// </summary>
        public void Dispose()
        {
            Disconnect();
            GC.SuppressFinalize(this);
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
            window.SelectedNumberType = this.NumberType;
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
            this.NumberType = window.SelectedNumberType;
        }
    }
}
