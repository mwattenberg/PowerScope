using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Ports;
using System.Windows.Controls;

namespace PowerScope.Model
{
    /// <summary>
    /// Supported data format types for serial data parsing
    /// </summary>
    public enum DataFormatType
    {
        RawBinary,
        ASCII,
        CustomFrame
    }

    /// <summary>
    /// Number types for binary data parsing
    /// </summary>
    public enum NumberTypeEnum
    {
        Uint8,
        Int8,
        Uint16,
        Int16,
        Uint32,
        Int32,
        Float32,
        Float64
    }

    /// <summary>
    /// Physical interface used by the FX2G3 USB bridge to talk to the target device.
    /// </summary>
    public enum UsbInterfaceType
    {
        SPI,
        UART,
        I2C
    }

    /// <summary>
    /// Supported stream source types
    /// </summary>
    public enum StreamSource
    {
        SerialPort,
        AudioInput,
        Demo,
        File,
        USB
    }

    public class StreamSettings : INotifyPropertyChanged
    {
        /// <summary>
        /// Current PowerScope file format version
        /// </summary>
        public const string CURRENT_VERSION = FileIOManager.CURRENT_VERSION;

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
        private string _delimiter; // "Comma", "Space", "Tab", or custom
        private string _frameStart; // for CustomFrame
        private int _demoSampleRate; // for Demo mode
        private string _demoSignalType; // for Demo mode
        private int _upDownSampling; // for UP/down sampling factor
        
        // File-related properties
        private string _filePath;
        private double _fileSampleRate;
        private bool _fileLoopPlayback;
        private bool _fileHasHeader;
        private string _fileDelimiter;
        private List<string> _fileChannelLabels;
        private string _fileParseStatus;
        private long _fileTotalSamples;

        // USB-related properties
        private string _usbSelectedDevice;
        private string _usbSelectedDevicePath;
        private UsbInterfaceType _usbInterface;
        private int _usbBufThreshold;

        // Callback for applying settings to data streams
        public Action<IDataStream> DataStreamConfigurationCallback { get; set; }

        public StreamSettings()
        {
            // Set minimal defaults - let the UI configuration window set the actual values
            DataBits = 8; // Keep reasonable default for serial
            StopBits = 1; // Keep reasonable default for serial
            Parity = Parity.None; // Keep reasonable default for serial
            NumberOfChannels = 1; // Start with minimal default
            DataFormat = DataFormatType.RawBinary; // Keep as default format
            NumberType = NumberTypeEnum.Uint16; // Set reasonable default
            Delimiter = "Comma"; // Set reasonable default for ASCII
            FrameStart = "AA AA"; // Keep default frame start bytes
            
            // Demo defaults
            DemoSampleRate = 1000; // Keep for demo mode
            DemoSignalType = "Sine Wave"; // Keep for demo mode
            
            // Sampling defaults
            UpDownSampling = 0; // Default to no sampling change
            
            // File defaults
            FileSampleRate = 1000.0;
            FileLoopPlayback = true; // Always loop as requested
            FileHasHeader = true; // Assume header by default
            FileDelimiter = ","; // Default to CSV
            FileChannelLabels = new List<string>();
            FileParseStatus = "No file selected";
            FileTotalSamples = 0;
            
            // USB defaults
            UsbSelectedDevice = null;
            UsbSelectedDevicePath = null;
            UsbInterface = UsbInterfaceType.UART;
            UsbBufThreshold = 128;

            // StreamSource will be set by the configuration dialog based on selected tab
            StreamSource = StreamSource.SerialPort; // Default to serial port;
            
            // Leave other values as null/zero to be set by user input
            Port = null;
            Baud = 3000000;
            AudioDevice = null;
            AudioDeviceIndex = 0;
            AudioSampleRate = 44100; // Set reasonable default for audio
            EnableChecksum = false;
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
            }
        }

        public int Baud
        {
            get { return _baud; }
            set
            {
                _baud = value;
                OnPropertyChanged(nameof(Baud));
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

        public int UpDownSampling
        {
            get { return _upDownSampling; }
            set
            {
                // Clamp value between -9 and 9
                var clampedValue = Math.Max(-9, Math.Min(9, value));
                if (_upDownSampling != clampedValue)
                {
                    _upDownSampling = clampedValue;
                    OnPropertyChanged(nameof(UpDownSampling));
                }
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

        // File properties
        public string FilePath
        {
            get { return _filePath; }
            set
            {
                _filePath = value;
                OnPropertyChanged(nameof(FilePath));
            }
        }

        public double FileSampleRate
        {
            get { return _fileSampleRate; }
            set
            {
                _fileSampleRate = value;
                OnPropertyChanged(nameof(FileSampleRate));
            }
        }

        public bool FileLoopPlayback
        {
            get { return _fileLoopPlayback; }
            set
            {
                _fileLoopPlayback = value;
                OnPropertyChanged(nameof(FileLoopPlayback));
            }
        }

        public bool FileHasHeader
        {
            get { return _fileHasHeader; }
            set
            {
                _fileHasHeader = value;
                OnPropertyChanged(nameof(FileHasHeader));
            }
        }

        public string FileDelimiter
        {
            get { return _fileDelimiter; }
            set
            {
                _fileDelimiter = value;
                OnPropertyChanged(nameof(FileDelimiter));
            }
        }

        public List<string> FileChannelLabels
        {
            get { return _fileChannelLabels; }
            set
            {
                _fileChannelLabels = value;
                OnPropertyChanged(nameof(FileChannelLabels));
            }
        }

        public string FileParseStatus
        {
            get { return _fileParseStatus; }
            set
            {
                _fileParseStatus = value;
                OnPropertyChanged(nameof(FileParseStatus));
            }
        }

        public long FileTotalSamples
        {
            get { return _fileTotalSamples; }
            set
            {
                _fileTotalSamples = value;
                OnPropertyChanged(nameof(FileTotalSamples));
            }
        }

        // USB properties
        public string UsbSelectedDevice
        {
            get { return _usbSelectedDevice; }
            set
            {
                if (_usbSelectedDevice != value)
                {
                    _usbSelectedDevice = value;
                    OnPropertyChanged(nameof(UsbSelectedDevice));
                }
            }
        }

        /// <summary>
        /// The WinUSB device-interface path of the selected FX2G3 unit, e.g.
        /// \\?\USB#VID_04B4&amp;PID_0081#&lt;instance&gt;#{guid}. Unlike the shared interface GUID,
        /// the &lt;instance&gt; segment uniquely identifies one physical board, so this is the key
        /// used to open the exact device the user picked (and to restore it across sessions).
        /// Null/empty = open the first available PowerScope device.
        /// </summary>
        public string UsbSelectedDevicePath
        {
            get { return _usbSelectedDevicePath; }
            set
            {
                if (_usbSelectedDevicePath != value)
                {
                    _usbSelectedDevicePath = value;
                    OnPropertyChanged(nameof(UsbSelectedDevicePath));
                }
            }
        }

        /// <summary>
        /// Physical interface the FX2G3 uses to talk to the attached MCU/device (SPI, UART, I2C).
        /// </summary>
        public UsbInterfaceType UsbInterface
        {
            get { return _usbInterface; }
            set
            {
                if (_usbInterface != value)
                {
                    _usbInterface = value;
                    OnPropertyChanged(nameof(UsbInterface));
                }
            }
        }

        /// <summary>
        /// Number of UART/SPI bytes the FX2G3 accumulates before firing a USB packet.
        /// Smaller = lower latency, more USB overhead. Range: 1–512.
        /// </summary>
        public int UsbBufThreshold
        {
            get { return _usbBufThreshold; }
            set
            {
                int clamped = Math.Max(1, Math.Min(512, value));
                if (_usbBufThreshold != clamped)
                {
                    _usbBufThreshold = clamped;
                    OnPropertyChanged(nameof(UsbBufThreshold));
                }
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
            // Only handle properties that require special logic or don't have reliable data binding
            
            // Get NumberType from ComboBox (requires special Tag-based logic)
            if (window.ComboBox_NumberType?.SelectedItem is ComboBoxItem selectedItem && 
                Enum.TryParse<NumberTypeEnum>(selectedItem.Tag?.ToString(), out var numberType))
            {
                this.NumberType = numberType;
            }

            // Get DataFormat from ComboBox (requires special Tag-based logic)
            if (window.ComboBox_DataFormat?.SelectedItem is ComboBoxItem dataFormatItem &&
                Enum.TryParse<DataFormatType>(dataFormatItem.Tag?.ToString(), out var dataFormat))
            {
                this.DataFormat = dataFormat;
            }
            
            // All other properties are handled by two-way data binding automatically:
            // - Demo properties (NumberOfChannels, DemoSampleRate, DemoSignalType)
            // - Serial properties (Port, Baud, DataBits, StopBits, Parity)
            // - Audio properties (AudioDevice, AudioDeviceIndex, AudioSampleRate)
            // - Other properties (Delimiter, FrameStart, EnableChecksum, UpDownSampling)
        }

        /// <summary>
        /// Apply up/down sampling settings to a data stream if it supports the feature
        /// </summary>
        /// <param name="dataStream">Data stream to configure</param>
        public void ApplyUpDownSamplingToDataStream(IDataStream dataStream)
        {
            if (dataStream is IUpDownSampling upDownSamplingStream)
            {
                upDownSamplingStream.UpDownSamplingFactor = this.UpDownSampling;
            }

            // Call the configured callback if available
            DataStreamConfigurationCallback?.Invoke(dataStream);
        }

        /// <summary>
        /// Apply USB-specific settings to a USBDataStream: device path, physical interface
        /// (UART/SPI), UART baud rate, and buffer threshold.
        /// Must be called before Connect() so the values are in place when StartStreaming()
        /// sends the control transfers to the FX2G3.
        /// </summary>
        /// <param name="dataStream">Data stream to configure — no-op for non-USB streams</param>
        public void ApplyUsbSettingsToDataStream(IDataStream dataStream)
        {
            if (dataStream is USBDataStream usbStream)
            {
                usbStream.SelectedDevicePath  = this.UsbSelectedDevicePath;
                usbStream.Interface           = this.UsbInterface;
                usbStream.UartBaudRate        = (this.UsbInterface == UsbInterfaceType.UART && this.Baud > 0) ? this.Baud : 0;
                usbStream.UartBufferThreshold = this.UsbBufThreshold > 0 ? this.UsbBufThreshold : 64;
            }
        }

        /// <summary>
        /// Parses the FrameStart hex string (e.g. "AA AA") into a byte array.
        /// Returns { 0xAA, 0xAA } as a safe default when the string is absent or invalid.
        /// </summary>
        public byte[] ParseFrameStartBytes()
        {
            string frameStart = this.FrameStart;
            if (string.IsNullOrEmpty(frameStart))
                return new byte[] { 0xAA, 0xAA };
            try
            {
                string[] parts = frameStart.Split(new char[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                List<byte> bytes = new List<byte>();
                foreach (string part in parts)
                {
                    string cleanPart = part.Trim().Replace("0x", "").Replace("0X", "");
                    if (byte.TryParse(cleanPart, System.Globalization.NumberStyles.HexNumber, null, out byte b))
                        bytes.Add(b);
                }
                return bytes.Count > 0 ? bytes.ToArray() : new byte[] { 0xAA, 0xAA };
            }
            catch
            {
                return new byte[] { 0xAA, 0xAA };
            }
        }

        /// <summary>
        /// Resolves the Delimiter string ("Comma", "Space", "Tab", "Semicolon", or a literal
        /// character) to its char equivalent.
        /// </summary>
        public char ParseDelimiterChar()
        {
            string delimiter = this.Delimiter;
            if (string.IsNullOrEmpty(delimiter))
                return ',';
            switch (delimiter.ToLower())
            {
                case "comma":
                case ",":
                    return ',';
                case "space":
                case " ":
                    return ' ';
                case "tab":
                case "\t":
                    return '\t';
                case "semicolon":
                case ";":
                    return ';';
                default:
                    return delimiter[0];
            }
        }

        /// <summary>
        /// Maps the NumberType enum to the DataParser binary format constant.
        /// </summary>
        public DataParser.BinaryFormat GetBinaryFormat()
        {
            switch (this.NumberType)
            {
                case NumberTypeEnum.Int16:   return DataParser.BinaryFormat.int16_t;
                case NumberTypeEnum.Uint16:  return DataParser.BinaryFormat.uint16_t;
                case NumberTypeEnum.Int32:   return DataParser.BinaryFormat.int32_t;
                case NumberTypeEnum.Uint32:  return DataParser.BinaryFormat.uint32_t;
                case NumberTypeEnum.Float32: return DataParser.BinaryFormat.float_t;
                default:                     return DataParser.BinaryFormat.uint16_t;
            }
        }

        /// <summary>
        /// Creates a DataParser fully configured from this StreamSettings instance.
        /// ASCII mode uses Delimiter and a newline frame terminator.
        /// Binary modes use NumberType and FrameStart bytes.
        /// </summary>
        public DataParser CreateDataParser()
        {
            if (this.DataFormat == DataFormatType.ASCII)
            {
                char separator = this.ParseDelimiterChar();
                return new DataParser(this.NumberOfChannels, '\n', separator);
            }
            else
            {
                DataParser.BinaryFormat binaryFormat = this.GetBinaryFormat();
                byte[] frameStartBytes = this.ParseFrameStartBytes();
                if (frameStartBytes != null && frameStartBytes.Length > 0)
                    return new DataParser(binaryFormat, this.NumberOfChannels, frameStartBytes);
                else
                    return new DataParser(binaryFormat, this.NumberOfChannels);
            }
        }

        /// <summary>
        /// Creates and fully configures an IDataStream from this StreamSettings instance.
        /// This is the single factory method for all stream types; it also applies
        /// up/down sampling so the returned stream is ready for Connect() + StartStreaming().
        /// </summary>
        public IDataStream CreateDataStream()
        {
            IDataStream dataStream;

            switch (this.StreamSource)
            {
                case StreamSource.USB:
                    UsbSourceSetting usbSourceSetting = new UsbSourceSetting(USBDataStream.DeviceInterfaceGuid, "FX2G3 PowerScope");
                    DataParser usbDataParser = this.CreateDataParser();
                    USBDataStream usbDs = new USBDataStream(usbSourceSetting, usbDataParser);
                    this.ApplyUsbSettingsToDataStream(usbDs);
                    dataStream = usbDs;
                    break;

                case StreamSource.Demo:
                    DemoSettings demoSettings = new DemoSettings(this.NumberOfChannels, this.DemoSampleRate, this.DemoSignalType);
                    dataStream = new DemoDataStream(demoSettings);
                    break;

                case StreamSource.AudioInput:
                    dataStream = new AudioDataStream(this.AudioDevice, this.AudioSampleRate);
                    break;

                case StreamSource.File:
                    dataStream = new FileDataStream(this.FilePath, this.FileLoopPlayback);
                    break;

                case StreamSource.SerialPort:
                default:
                    SourceSetting sourceSetting = new SourceSetting(this.Port, this.Baud, this.DataBits, this.StopBits, this.Parity);
                    DataParser serialParser = this.CreateDataParser();
                    dataStream = new SerialDataStream(sourceSetting, serialParser);
                    break;
            }

            this.ApplyUpDownSamplingToDataStream(dataStream);
            return dataStream;
        }

        /// <summary>
        /// Simplified file header parsing using FileIOManager
        /// </summary>
        /// <param name="filePath">Path to the file to parse</param>
        /// <returns>True if parsing was successful</returns>
        public bool ParseFileHeader(string filePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    FileParseStatus = "No file path specified";
                    return false;
                }

                // Use FileIOManager for parsing
                var header = FileIOManager.ReadFileHeader(filePath);
                
                if (header == null)
                {
                    FileParseStatus = "Failed to parse file";
                    return false;
                }

                // Update properties from header
                FileSampleRate = header.SampleRate;
                FileDelimiter = header.Delimiter.ToString();
                FileChannelLabels = header.ChannelLabels;
                NumberOfChannels = header.ChannelCount;
                FileHasHeader = header.HasHeader;
                FileTotalSamples = header.TotalSamples;
                FileParseStatus = header.ParseStatus;

                return !string.IsNullOrEmpty(header.ParseStatus) && header.ChannelCount > 0;
            }
            catch (Exception ex)
            {
                FileParseStatus = $"Error parsing file: {ex.Message}";
                return false;
            }
        }
    }
}
