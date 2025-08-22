using System.ComponentModel;
using System.IO.Ports; // Add for Parity enum
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
        AudioInput
    }

    public class DataStreamViewModel : INotifyPropertyChanged, IDisposable
    {
        private string _port;
        private int _baud;
        private bool _isConnected;
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
        private string numberType; // "Uint8", "Uint16", etc.
        private string _endianness; // "LittleEndian", "BigEndian"
        private string _delimiter; // "Comma", "Space", "Tab", or custom
        private string _frameStart; // for CustomFrame

        // SerialDataStream instance
        private SerialDataStream _serialDataStream;

        public DataStreamViewModel()
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
        public SerialDataStream SerialDataStream => _serialDataStream;

        /// <summary>
        /// Connects to the serial port using the current configuration
        /// </summary>
        /// <returns>True if connection was successful, false otherwise</returns>
        public bool Connect()
        {
            if (_isConnected)
            {
                return true; // Already connected
            }

            // Validate port name before attempting connection
            if (string.IsNullOrWhiteSpace(Port))
            {
                StatusMessage = "Please select a valid port";
                return false;
            }

            try
            {
                // Create SourceSetting based on current properties
                var sourceSetting = new SourceSetting(Port, Baud, DataBits, Parity);

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
                _serialDataStream = new SerialDataStream(sourceSetting, dataParser);
                
                // Start the data stream
                _serialDataStream.Start();

                // Update connection state
                IsConnected = true;
                StatusMessage = "Connected";

                return true;
            }
            catch (PortNotFoundException ex)
            {
                StatusMessage = $"Could not find port: {ex.PortName}";
                _serialDataStream?.Dispose();
                _serialDataStream = null;
                IsConnected = false;
                return false;
            }
            catch (PortAlreadyInUseException ex)
            {
                StatusMessage = $"Port already in use: {ex.PortName}";
                _serialDataStream?.Dispose();
                _serialDataStream = null;
                IsConnected = false;
                return false;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Connection failed: {ex.Message}";
                _serialDataStream?.Dispose();
                _serialDataStream = null;
                IsConnected = false;
                return false;
            }
        }

        /// <summary>
        /// Disconnects from the serial port and disposes resources
        /// </summary>
        public void Disconnect()
        {
            if (!_isConnected)
            {
                return; // Already disconnected
            }

            try
            {
                // Stop and dispose the SerialDataStream
                _serialDataStream?.Stop();
                _serialDataStream?.Dispose();
                _serialDataStream = null;

                // Update connection state
                IsConnected = false;
                StatusMessage = "Disconnected";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Disconnect error: {ex.Message}";
                // Ensure cleanup even if there's an error
                _serialDataStream = null;
                IsConnected = false;
            }
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
                var parts = frameStart.Split(new char[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                var bytes = new List<byte>();
                
                foreach (var part in parts)
                {
                    string cleanPart = part.Trim().Replace("0x", "").Replace("0X", "");
                    if (byte.TryParse(cleanPart, System.Globalization.NumberStyles.HexNumber, null, out byte b))
                    {
                        bytes.Add(b);
                    }
                }

                return bytes.Count > 0 ? bytes.ToArray() : new byte[] { 0xAA, 0xAA };
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

            return delimiter.ToLower() switch
            {
                "comma" or "," => ',',
                "space" or " " => ' ',
                "tab" or "\t" => '\t',
                "semicolon" or ";" => ';',
                _ => delimiter[0] // Use first character if not recognized
            };
        }

        // Dataformat Tab properties
        public DataFormatType DataFormat
        {
            get => _dataFormat;
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
                OnPropertyChanged(nameof(PortAndBaudDisplay));
            }
        }

        public int Baud
        {
            get { return _baud; }
            set
            {
                _baud = value;
                OnPropertyChanged(nameof(Baud));
                OnPropertyChanged(nameof(PortAndBaudDisplay));
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

        public int SampleRate
        {
            get { return _sampleRate; }
            set
            {
                _sampleRate = value;
                OnPropertyChanged(nameof(SampleRate));
            }
        }

        public bool EnableChecksum
        {
            get => _enableChecksum;
            set { _enableChecksum = value; OnPropertyChanged(nameof(EnableChecksum)); }
        }

        public bool IsConnected
        {
            get { return _isConnected; }
            set
            {
                _isConnected = value;
                OnPropertyChanged(nameof(IsConnected));
                OnPropertyChanged(nameof(ConnectButtonText));
                OnPropertyChanged(nameof(ConnectButtonBackground));
                UpdateStatusMessage();
            }
        }

        public string ConnectButtonText => IsConnected ? "Disconnect" : "Connect";

        public SolidColorBrush ConnectButtonBackground => 
            IsConnected ? new SolidColorBrush(Colors.OrangeRed) : new SolidColorBrush(Colors.LimeGreen);

        public string StatusMessage
        {
            get { return _statusMessage; }
            set
            {
                _statusMessage = value;
                OnPropertyChanged(nameof(StatusMessage));
            }
        }

        public string PortAndBaudDisplay => $"Port: {Port}\nBaud: {Baud}";

        public int NumberOfChannels
        {
            get => _numberOfChannels;
            set { _numberOfChannels = value; OnPropertyChanged(nameof(NumberOfChannels)); }
        }
        public string NumberType
        {
            get => numberType;
            set { numberType = value; OnPropertyChanged(nameof(NumberType)); }
        }
        public string Endianness
        {
            get => _endianness;
            set { _endianness = value; OnPropertyChanged(nameof(Endianness)); }
        }
        public string Delimiter
        {
            get => _delimiter;
            set { _delimiter = value; OnPropertyChanged(nameof(Delimiter)); }
        }
        public string FrameStart
        {
            get => _frameStart;
            set { _frameStart = value; OnPropertyChanged(nameof(FrameStart)); }
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

        /// <summary>
        /// Disposes of the SerialDataStream resources
        /// </summary>
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
