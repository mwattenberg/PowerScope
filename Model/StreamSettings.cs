using System.ComponentModel;
using System.IO.Ports; // Use System.IO.Ports for Parity enum
using System.Windows.Media; // Add for SolidColorBrush and Colors
using System; // Add for IDisposable
using System.Collections.Generic; // Add for List<T>
using System.Windows.Controls; // Add for ComboBoxItem
using System.IO; // Add for File operations

namespace PowerScope.Model
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
        Demo,
        File
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
        /// <summary>
        /// Current PowerScope file format version
        /// </summary>
        public const string CURRENT_VERSION = "V1.0";

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
        
        // File-related properties
        private string _filePath;
        private double _fileSampleRate;
        private bool _fileLoopPlayback;
        private bool _fileHasHeader;
        private string _fileDelimiter;
        private List<string> _fileChannelLabels;
        private string _fileParseStatus;
        private long _fileTotalSamples;

        public StreamSettings()
        {
            // Set minimal defaults - let the UI configuration window set the actual values
            DataBits = 8; // Keep reasonable default for serial
            StopBits = 1; // Keep reasonable default for serial
            Parity = Parity.None; // Keep reasonable default for serial
            NumberOfChannels = 1; // Start with minimal default
            DataFormat = DataFormatType.RawBinary; // Keep as default format
            NumberType = NumberTypeEnum.Uint16; // Set reasonable default
            Endianness = "LittleEndian"; // Set reasonable default
            Delimiter = "Comma"; // Set reasonable default for ASCII
            FrameStart = "AA AA"; // Keep default frame start bytes
            
            // Demo defaults
            DemoSampleRate = 1000; // Keep for demo mode
            DemoSignalType = "Sine Wave"; // Keep for demo mode
            
            // File defaults
            FileSampleRate = 1000.0;
            FileLoopPlayback = true; // Always loop as requested
            FileHasHeader = true; // Assume header by default
            FileDelimiter = ","; // Default to CSV
            FileChannelLabels = new List<string>();
            FileParseStatus = "No file selected";
            FileTotalSamples = 0;
            
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
            // Only handle properties that require special logic or don't have reliable binding
            
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
            // - Other properties (Endianness, Delimiter, FrameStart, EnableChecksum)
        }

        /// <summary>
        /// Parses the header of a file to extract metadata and channel information
        /// </summary>
        /// <param name="filePath">Path to the file to parse</param>
        /// <returns>True if parsing was successful</returns>
        public bool ParseFileHeader(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    FileParseStatus = "File not found";
                    return false;
                }

                string[] lines = File.ReadAllLines(filePath);
                if (lines.Length == 0)
                {
                    FileParseStatus = "File is empty";
                    return false;
                }

                // Check if this is a PowerScope file with version header
                string fileVersion = DetectFileVersion(lines);
                
                if (fileVersion == "V1.0")
                {
                    return ParseV1FileHeader(lines);
                }
                else if (fileVersion == "Unknown")
                {
                    // Try to parse as generic CSV file
                    return ParseGenericCSVFile(lines);
                }
                else
                {
                    FileParseStatus = $"Unsupported file version: {fileVersion}. Please use PowerScope V1.0 format or a standard CSV file.";
                    return false;
                }
            }
            catch (Exception ex)
            {
                FileParseStatus = $"Error parsing file: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Detects the PowerScope file version from header comments
        /// </summary>
        /// <param name="lines">All lines from the file</param>
        /// <returns>Version string or "Unknown" for files without version info</returns>
        private string DetectFileVersion(string[] lines)
        {
            foreach (string line in lines)
            {
                if (line.StartsWith("#") && line.Contains("PowerScope Version") && line.Contains(":"))
                {
                    string versionStr = line.Split(':')[1].Trim();
                    return versionStr;
                }
                
                // Stop looking at first non-comment line
                if (!line.StartsWith("#") && !string.IsNullOrWhiteSpace(line))
                    break;
            }
            
            return "Unknown";
        }

        /// <summary>
        /// Parses V1.0 PowerScope file format
        /// </summary>
        private bool ParseV1FileHeader(string[] lines)
        {
            bool foundMetadata = false;
            double sampleRate = 1000.0; // Default
            List<string> channelLabels = new List<string>();
            string delimiter = ",";
            long totalSamples = 0;

            foreach (string line in lines)
            {
                if (line.StartsWith("#"))
                {
                    foundMetadata = true;
                    
                    // Parse sample rate using invariant culture to always expect "." as decimal separator
                    if (line.Contains("Sample Rate") && line.Contains(":"))
                    {
                        string rateStr = line.Split(':')[1].Trim().Split(' ')[0];
                        if (double.TryParse(rateStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double rate) && rate > 0)
                        {
                            sampleRate = rate;
                        }
                    }
                    // Parse channel information from channel info lines
                    else if (line.Contains(":") && 
                            !line.Contains("Sample Rate") && !line.Contains("Recording Started") && 
                            !line.Contains("Total Channels") && !line.Contains("PowerScope Version") &&
                            !line.Contains("Data Format") && !line.Contains("Channel Information"))
                    {
                        // Channel line: "# CH1: Stream=Serial, Index=0"
                        string[] parts = line.Split(':');
                        if (parts.Length >= 2)
                        {
                            string channelName = parts[0].Replace("#", "").Trim();
                            if (!string.IsNullOrEmpty(channelName) && !channelLabels.Contains(channelName))
                            {
                                channelLabels.Add(channelName);
                            }
                        }
                    }
                }
                else if (!string.IsNullOrWhiteSpace(line))
                {
                    // First non-comment line should be the CSV header
                    if (channelLabels.Count == 0)
                    {
                        // Try to detect delimiter
                        if (line.Contains(",")) delimiter = ",";
                        else if (line.Contains("\t")) delimiter = "\t";
                        else if (line.Contains(";")) delimiter = ";";
                        else if (line.Contains(" ")) delimiter = " ";
                        
                        // Parse header columns - all columns are channels in V1.0 format
                        string[] columns = line.Split(delimiter.ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
                        foreach (string col in columns)
                        {
                            string cleanCol = col.Trim();
                            if (!string.IsNullOrEmpty(cleanCol))
                            {
                                channelLabels.Add(cleanCol);
                            }
                        }
                    }
                    else
                    {
                        // Count data lines
                        totalSamples++;
                    }
                }
            }

            // Count remaining data lines (skip header if present)
            if (foundMetadata || channelLabels.Count > 0)
            {
                // Count all non-comment, non-header lines
                bool headerFound = false;
                foreach (string line in lines)
                {
                    if (line.StartsWith("#") || string.IsNullOrWhiteSpace(line))
                        continue;
                        
                    if (!headerFound && (foundMetadata || channelLabels.Count > 0))
                    {
                        headerFound = true; // Skip the first data line if it's a header
                        continue;
                    }
                    
                    totalSamples++;
                }
            }

            // Update properties
            FileSampleRate = sampleRate;
            FileDelimiter = delimiter;
            FileChannelLabels = channelLabels;
            NumberOfChannels = channelLabels.Count;
            FileHasHeader = foundMetadata || channelLabels.Count > 0;
            FileTotalSamples = totalSamples;

            if (channelLabels.Count > 0)
            {
                FileParseStatus = $"Found {channelLabels.Count} channels, {totalSamples:N0} samples at {sampleRate} Hz (Version: V1.0)";
                return true;
            }
            else
            {
                FileParseStatus = "No channel information found in file";
                return false;
            }
        }

        /// <summary>
        /// Parses a generic CSV file without PowerScope headers
        /// </summary>
        /// <param name="lines">All lines from the file</param>
        /// <returns>True if parsing was successful</returns>
        private bool ParseGenericCSVFile(string[] lines)
        {
            List<string> channelLabels = new List<string>();
            string delimiter = ",";
            double sampleRate = 1000.0; // Default for generic files
            long totalSamples = 0;

            // Find first non-empty line
            string firstLine = null;
            foreach (string line in lines)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    firstLine = line.Trim();
                    break;
                }
            }

            if (firstLine == null)
            {
                FileParseStatus = "No valid data found in file";
                return false;
            }

            // Try to detect delimiter
            if (firstLine.Contains(",")) delimiter = ",";
            else if (firstLine.Contains("\t")) delimiter = "\t";
            else if (firstLine.Contains(";")) delimiter = ";";
            else if (firstLine.Contains(" ")) delimiter = " ";

            // Parse header/first line to get column names
            string[] columns = firstLine.Split(delimiter.ToCharArray(), StringSplitOptions.RemoveEmptyEntries);

            // Check if first line looks like data (all numbers) or header (contains text)
            bool firstLineIsHeader = false;
            foreach (string col in columns)
            {
                if (!double.TryParse(col.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
                {
                    firstLineIsHeader = true;
                    break;
                }
            }

            if (firstLineIsHeader)
            {
                // Use column names as channel labels
                foreach (string col in columns)
                {
                    string cleanCol = col.Trim();
                    if (!string.IsNullOrEmpty(cleanCol))
                    {
                        channelLabels.Add(cleanCol);
                    }
                }
            }
            else
            {
                // Generate generic channel names
                for (int i = 0; i < columns.Length; i++)
                {
                    channelLabels.Add($"CH{i + 1}");
                }
            }

            // Count data lines
            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                    
                if (firstLineIsHeader && line == firstLine)
                    continue; // Skip header line
                    
                totalSamples++;
            }

            // Update properties
            FileSampleRate = sampleRate;
            FileDelimiter = delimiter;
            FileChannelLabels = channelLabels;
            NumberOfChannels = channelLabels.Count;
            FileHasHeader = firstLineIsHeader;
            FileTotalSamples = totalSamples;

            if (channelLabels.Count > 0)
            {
                FileParseStatus = $"Found {channelLabels.Count} channels, {totalSamples:N0} samples (Generic CSV file, sample rate assumed: {sampleRate} Hz)";
                return true;
            }
            else
            {
                FileParseStatus = "No valid columns found in file";
                return false;
            }
        }
    }
}
