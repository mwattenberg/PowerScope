using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;

namespace PowerScope.Model
{
    public class FileDataStream : IDataStream, IChannelConfigurable, IBufferResizable
    {
        private bool _disposed = false;
        private bool _isConnected = false;
        private bool _isStreaming = false;
        private string _statusMessage;
        private Timer _dataStreamingTimer;
        
        public FileSettings FileSettings { get; init; }
        public long TotalSamples { get; private set; }
        public long TotalBits { get; private set; }
        private RingBuffer<double>[] ReceivedData { get; set; }
        private readonly object _dataLock = new object();
        
        // File streaming state
        private double[][] _fileData; // All data loaded from file
        private int _currentSampleIndex = 0;
        private bool _loopPlayback = false;
        
        // Channel-specific processing
        private ChannelSettings[] _channelSettings;
        private IDigitalFilter[] _channelFilters;
        private readonly object _channelConfigLock = new object();

        public int ChannelCount { get; private set; }
        public double SampleRate { get { return FileSettings.SampleRate; } }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public string StatusMessage
        {
            get { return _statusMessage; }
            private set 
            { 
                if (_statusMessage != value)
                {
                    _statusMessage = value;
                    OnPropertyChanged(nameof(StatusMessage));
                }
            }
        }

        public string StreamType
        {
            get { return "File"; }
        }

        public bool IsConnected
        {
            get { return _isConnected; }
            private set 
            { 
                if (_isConnected != value)
                {
                    _isConnected = value;
                    OnPropertyChanged(nameof(IsConnected));
                }
            }
        }

        public bool IsStreaming
        {
            get { return _isStreaming; }
            private set 
            { 
                if (_isStreaming != value)
                {
                    _isStreaming = value;
                    OnPropertyChanged(nameof(IsStreaming));
                }
            }
        }

        public FileDataStream(FileSettings fileSettings)
        {
            FileSettings = fileSettings ?? throw new ArgumentNullException(nameof(fileSettings));
            _loopPlayback = fileSettings.LoopPlayback;
            
            StatusMessage = "Disconnected";
        }

        private void InitializeRingBuffers(int bufferSize)
        {
            ReceivedData = new RingBuffer<double>[ChannelCount];
            for (int i = 0; i < ChannelCount; i++)
            {
                ReceivedData[i] = new RingBuffer<double>(bufferSize);
            }
        }

        #region IBufferResizable Implementation

        public int BufferSize 
        { 
            get 
            { 
                lock (_dataLock)
                {
                    if (ReceivedData == null)
                        return 0;
                    if (ReceivedData[0] == null)
                        return 0;
                    return ReceivedData[0].Capacity;
                }
            }
            set
            {
                if (value <= 0)
                    return;

                lock (_dataLock)
                {
                    // Clear existing data
                    if (ReceivedData != null)
                    {
                        foreach (var buffer in ReceivedData)
                        {
                            buffer?.Clear();
                        }
                    }

                    // Recreate ring buffers with new size - no need to stop/restart streaming
                    InitializeRingBuffers(value);
                    
                    // Reset statistics
                    TotalSamples = 0;
                    TotalBits = 0;
                    _currentSampleIndex = 0;
                }
                
                // Notify that buffer size changed
                OnPropertyChanged(nameof(BufferSize));
            }
        }

        #endregion

        #region IChannelConfigurable Implementation

        public void SetChannelSetting(int channelIndex, ChannelSettings settings)
        {
            if (channelIndex < 0 || channelIndex >= ChannelCount)
                return;

            lock (_channelConfigLock)
            {
                _channelSettings[channelIndex] = settings;
                
                // Update filter reference and reset if filter type changed
                var newFilter = settings?.Filter;
                if (_channelFilters[channelIndex] != newFilter)
                {
                    _channelFilters[channelIndex] = newFilter;
                    _channelFilters[channelIndex]?.Reset();
                }
            }
        }

        public void UpdateChannelSettings(IReadOnlyList<ChannelSettings> channelSettings)
        {
            if (channelSettings == null)
                return;

            lock (_channelConfigLock)
            {
                for (int i = 0; i < Math.Min(ChannelCount, channelSettings.Count); i++)
                {
                    SetChannelSetting(i, channelSettings[i]);
                }
            }
        }

        public void ResetChannelFilters()
        {
            lock (_channelConfigLock)
            {
                for (int i = 0; i < ChannelCount; i++)
                {
                    _channelFilters[i]?.Reset();
                }
            }
        }

        #endregion

        #region Data Processing

        private double ApplyChannelProcessing(int channel, double rawSample)
        {
            // Get channel settings safely
            ChannelSettings settings;
            IDigitalFilter filter;
            
            lock (_channelConfigLock)
            {
                settings = _channelSettings[channel];
                filter = _channelFilters[channel];
            }
            
            if (settings == null)
                return rawSample;
            
            // Apply gain and offset
            double processed = rawSample * settings.Gain + settings.Offset;
            
            // Apply filter if configured
            if (filter != null)
            {
                processed = filter.Filter(processed);
            }
            
            return processed;
        }

        #endregion

        #region File Loading

        private bool LoadFileData()
        {
            try
            {
                if (!File.Exists(FileSettings.FilePath))
                {
                    StatusMessage = $"Error: File not found - {FileSettings.FilePath}";
                    return false;
                }

                string[] lines = File.ReadAllLines(FileSettings.FilePath);
                
                if (lines.Length == 0)
                {
                    StatusMessage = "Error: File is empty";
                    return false;
                }

                // Filter out comment lines and empty lines, but keep track of header
                var processedLines = new List<string>();
                string headerLine = null;
                bool foundHeader = false;

                foreach (string line in lines)
                {
                    string trimmedLine = line.Trim();
                    
                    // Skip empty lines
                    if (string.IsNullOrWhiteSpace(trimmedLine))
                        continue;
                        
                    // Skip comment lines (lines starting with #)
                    if (trimmedLine.StartsWith("#"))
                        continue;
                    
                    // If we expect a header and haven't found one yet, this should be the header
                    if (FileSettings.HasHeader && !foundHeader)
                    {
                        headerLine = trimmedLine;
                        foundHeader = true;
                        continue;
                    }
                    
                    // This is a data line
                    processedLines.Add(trimmedLine);
                }
                
                if (processedLines.Count == 0)
                {
                    StatusMessage = "Error: No data found in file";
                    return false;
                }

                // Parse first data line to determine number of columns/channels
                string[] firstLineValues = ParseLine(processedLines[0]);
                
                // For V1.0 PowerScope CSV files, all columns are channel data (no Sample/Time columns)
                if (FileSettings.HasHeader && headerLine != null)
                {
                    string[] headerColumns = ParseLine(headerLine);
                    ChannelCount = headerColumns.Length; // All columns are channels in V1.0 format
                }
                else
                {
                    ChannelCount = firstLineValues.Length;
                }

                if (ChannelCount == 0)
                {
                    StatusMessage = "Error: No valid data columns found";
                    return false;
                }

                // Initialize data arrays
                _fileData = new double[ChannelCount][];
                for (int i = 0; i < ChannelCount; i++)
                {
                    _fileData[i] = new double[processedLines.Count];
                }

                // Parse all data - all columns are channels in V1.0 format
                for (int row = 0; row < processedLines.Count; row++)
                {
                    string[] values = ParseLine(processedLines[row]);
                    
                    // Handle rows with different number of columns (pad with zeros)
                    for (int col = 0; col < ChannelCount; col++)
                    {
                        if (col < values.Length && double.TryParse(values[col], NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                        {
                            _fileData[col][row] = value;
                        }
                        else
                        {
                            _fileData[col][row] = 0.0; // Default value for missing data
                        }
                    }
                }

                // Initialize ring buffers for each channel
                int ringBufferSize = Math.Max(500000, (int)(FileSettings.SampleRate * 10)); // 10 seconds of data
                InitializeRingBuffers(ringBufferSize);
                
                // Initialize channel processing arrays
                _channelSettings = new ChannelSettings[ChannelCount];
                _channelFilters = new IDigitalFilter[ChannelCount];

                StatusMessage = $"Loaded {processedLines.Count} samples, {ChannelCount} channels";
                return true;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading file: {ex.Message}";
                return false;
            }
        }

        private string[] ParseLine(string line)
        {
            char delimiter = FileSettings.Delimiter switch
            {
                "," => ',',
                "\t" => '\t',
                " " => ' ',
                ";" => ';',
                _ => ','
            };

            return line.Split(delimiter, StringSplitOptions.RemoveEmptyEntries);
        }

        #endregion

        #region IDataStream Implementation

        public void Connect()
        {
            if (string.IsNullOrWhiteSpace(FileSettings.FilePath))
            {
                StatusMessage = "Error: No file path specified";
                return;
            }

            StatusMessage = "Loading file...";
            
            if (LoadFileData())
            {
                IsConnected = true;
                _currentSampleIndex = 0;
                StatusMessage = $"Connected - File: {Path.GetFileName(FileSettings.FilePath)}";
            }
        }

        public void Disconnect()
        {
            StopStreaming();
            IsConnected = false;
            _fileData = null;
            ChannelCount = 0;
            StatusMessage = "Disconnected";
        }

        public void StartStreaming()
        {
            if (!_isConnected || _isStreaming || _fileData == null)
                return;

            IsStreaming = true;
            StatusMessage = "Streaming from file";
            
            // Reset filters when starting streaming
            ResetChannelFilters();
            
            // Calculate timer interval to stream data at the specified sample rate
            // Generate data in chunks for smooth streaming
            int samplesPerChunk = Math.Max(1, (int)(FileSettings.SampleRate / 100)); // ~100 updates per second
            int timerInterval = Math.Max(1, (samplesPerChunk * 1000) / (int)FileSettings.SampleRate);
            
            _dataStreamingTimer = new Timer(StreamData, samplesPerChunk, 0, timerInterval);
        }

        public void StopStreaming()
        {
            if (!_isStreaming)
                return;
                
            IsStreaming = false;
            _dataStreamingTimer?.Dispose();
            _dataStreamingTimer = null;
            StatusMessage = IsConnected ? "Stopped" : "Disconnected";
        }

        private void StreamData(object state)
        {
            if (!IsStreaming || _fileData == null)
                return;

            int samplesPerChunk = (int)state;
            
            lock (_dataLock)
            {
                int samplesAvailable = _fileData[0].Length;
                int actualSamplesToRead = Math.Min(samplesPerChunk, samplesAvailable - _currentSampleIndex);
                
                if (actualSamplesToRead <= 0)
                {
                    if (_loopPlayback)
                    {
                        // Loop back to beginning
                        _currentSampleIndex = 0;
                        actualSamplesToRead = Math.Min(samplesPerChunk, samplesAvailable);
                    }
                    else
                    {
                        // End of file reached, stop streaming
                        Task.Run(() => StopStreaming()); // Stop on background thread to avoid timer issues
                        return;
                    }
                }

                // Process and add data to ring buffers
                for (int channel = 0; channel < ChannelCount; channel++)
                {
                    for (int i = 0; i < actualSamplesToRead; i++)
                    {
                        double rawSample = _fileData[channel][_currentSampleIndex + i];
                        
                        // Apply channel-specific processing (gain, offset, filtering)
                        double processedSample = ApplyChannelProcessing(channel, rawSample);
                        
                        ReceivedData[channel].Add(processedSample);
                    }
                }
                
                // Update position and statistics
                _currentSampleIndex += actualSamplesToRead;
                TotalSamples += actualSamplesToRead;
                TotalBits += actualSamplesToRead * ChannelCount * 16; // Assume 16 bits per sample
            }
        }

        public int CopyLatestTo(int channel, double[] destination, int n)
        {
            if (channel < 0 || channel >= ChannelCount || ReceivedData == null)
                return 0;
                
            lock (_dataLock)
            {
                return ReceivedData[channel].CopyLatestTo(destination, n);
            }
        }

        public void clearData()
        {
            lock (_dataLock)
            {
                if (ReceivedData != null)
                {
                    foreach (RingBuffer<double> ringBuffer in ReceivedData)
                    {
                        ringBuffer?.Clear();
                    }
                }
                TotalSamples = 0;
                TotalBits = 0;
                _currentSampleIndex = 0;
            }
            
            // Reset filters when clearing data
            ResetChannelFilters();
        }

        public void Dispose()
        {
            if (_disposed)
                return;
                
            if (_isStreaming)
                StopStreaming();
                
            _dataStreamingTimer?.Dispose();
            
            if (ReceivedData != null)
            {
                foreach (RingBuffer<double> ringBuffer in ReceivedData)
                {
                    ringBuffer?.Clear();
                }
            }
            
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        #endregion
    }

    /// <summary>
    /// Configuration settings for file-based data streaming
    /// </summary>
    public class FileSettings
    {
        public string FilePath { get; init; }
        public double SampleRate { get; init; }
        public bool LoopPlayback { get; init; }
        public bool HasHeader { get; init; }
        public string Delimiter { get; init; } // ",", "\t", " ", ";"

        public FileSettings(string filePath, double sampleRate, bool loopPlayback = false, bool hasHeader = false, string delimiter = ",")
        {
            FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            SampleRate = sampleRate > 0 ? sampleRate : throw new ArgumentOutOfRangeException(nameof(sampleRate), "Sample rate must be positive");
            LoopPlayback = loopPlayback;
            HasHeader = hasHeader;
            Delimiter = delimiter ?? ",";
        }
    }
}