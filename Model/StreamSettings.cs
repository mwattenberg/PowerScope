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
    /// Supported stream source types
    /// </summary>
    public enum StreamSource
    {
        SerialPort,
        AudioInput,
        Demo,
        File,
        USB,
        FTDI
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

        // FTDI-related properties
        private uint _ftdiDeviceIndex;
        private uint _ftdiClockFrequency;
        private string _ftdiSelectedDevice;

        // SPI-specific configuration properties
        private uint _spiClockFrequency;
        private byte _spiLatencyTimer;
        private int _spiTransferInterval;
        private int _spiTransferSize;
        private byte _spiChipSelectPolarity;
        private byte _spiMode;
        private byte _spiDataOrder;

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
            
            // FTDI defaults
            FtdiDeviceIndex = 0; // Default to first device
            FtdiClockFrequency = 1000000; // Default to 1MHz
            FtdiSelectedDevice = null;
            
            // SPI defaults
            SpiClockFrequency = 15000000; // Default to 15MHz for SPI
            SpiLatencyTimer = 2; // Default latency timer
            SpiTransferInterval = 10; // Default transfer interval
            SpiTransferSize = 256; // Default transfer size
            SpiChipSelectPolarity = 0; // Default active-low CS
            SpiMode = 0; // Default SPI mode 0
            SpiDataOrder = 0; // Default MSB first
            
            // StreamSource will be set by the configuration dialog based on selected tab
            StreamSource = StreamSource.SerialPort; // Default to serial port;
            
            // Leave other values as null/zero to be set by user input
            Port = null;
            Baud = 0;
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

        // FTDI properties
        public uint FtdiDeviceIndex
        {
            get { return _ftdiDeviceIndex; }
            set
            {
                _ftdiDeviceIndex = value;
                OnPropertyChanged(nameof(FtdiDeviceIndex));
            }
        }

        public uint FtdiClockFrequency
        {
            get { return _ftdiClockFrequency; }
            set
            {
                _ftdiClockFrequency = value;
                OnPropertyChanged(nameof(FtdiClockFrequency));
            }
        }

        public string FtdiSelectedDevice
        {
            get { return _ftdiSelectedDevice; }
            set
            {
                _ftdiSelectedDevice = value;
                OnPropertyChanged(nameof(FtdiSelectedDevice));
            }
        }

        // SPI Configuration Properties
        public uint SpiClockFrequency
        {
   get { return _spiClockFrequency; }
 set
    {
     if (_spiClockFrequency != value)
    {
               _spiClockFrequency = value;
    OnPropertyChanged(nameof(SpiClockFrequency));
        }
 }
        }

        public byte SpiLatencyTimer
        {
       get { return _spiLatencyTimer; }
         set { _spiLatencyTimer = 2; } // Always 2ms - fixed
 }

        public int SpiTransferInterval
        {
            get { return _spiTransferInterval; }
            set { _spiTransferInterval = 10; } // Always 10ms - fixed
      }

        public int SpiTransferSize
        {
            get { return _spiTransferSize; }
            set { _spiTransferSize = 256; } // Always 256 bytes - fixed
        }

     public byte SpiChipSelectPolarity
        {
  get { return _spiChipSelectPolarity; }
            set
{
    if (_spiChipSelectPolarity != value && (value == 0 || value == 1))
             {
         _spiChipSelectPolarity = value;
 OnPropertyChanged(nameof(SpiChipSelectPolarity));
         }
   }
        }

        public byte SpiMode
        {
       get { return _spiMode; }
     set
      {
         if (_spiMode != value && value >= 0 && value <= 3)
        {
 _spiMode = value;
 OnPropertyChanged(nameof(SpiMode));
    }
      }
        }

        public byte SpiDataOrder
{
  get { return _spiDataOrder; }
            set { _spiDataOrder = 0; } // Always MSB First (0) - fixed
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
