using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using System.CodeDom.Compiler;

namespace PowerScope.Model
{
    public class FileDataStream : IDataStream, IChannelConfigurable, IBufferResizable
    {
        private bool _disposed = false;
        private bool _isConnected = false;
        private bool _isStreaming = false;
        private string _statusMessage = "Disconnected";
        private Timer _dataStreamingTimer;
        
        public string FilePath { get; init; }
        public bool LoopPlayback { get; init; }
        
        public long TotalSamples { get; private set; }
        public long TotalBits { get; private set; }
        private RingBuffer<double>[] _receivedData;
        
        // File streaming state
        private double[][] _fileData;
        private FileHeader _fileHeader;
        private int _currentSampleIndex = 0;
        
        // Channel-specific processing
        private ChannelSettings[] _channelSettings;
        private IDigitalFilter[] _channelFilters;

        public int ChannelCount { get; private set; }
        public double SampleRate => _fileHeader?.SampleRate ?? 1000.0;

        public event PropertyChangedEventHandler PropertyChanged;

        public string StatusMessage
        {
            get => _statusMessage;
            private set 
            { 
                if (_statusMessage != value)
                {
                    _statusMessage = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusMessage)));
                }
            }
        }

        public string StreamType => "File";

        public bool IsConnected
        {
            get => _isConnected;
            private set 
            { 
                if (_isConnected != value)
                {
                    _isConnected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsConnected)));
                }
            }
        }

        public bool IsStreaming
        {
            get => _isStreaming;
            private set 
            { 
                if (_isStreaming != value)
                {
                    _isStreaming = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsStreaming)));
                }
            }
        }

        public FileDataStream(string filePath, bool loopPlayback = false)
        {
            FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            LoopPlayback = loopPlayback;
        }

        #region IBufferResizable Implementation

        public int BufferSize 
        { 
            get => _receivedData?[0]?.Capacity ?? 0;
            set
            {
                if (value <= 0 || ChannelCount == 0) return;

                // Recreate ring buffers with new size
                _receivedData = new RingBuffer<double>[ChannelCount];
                for (int i = 0; i < ChannelCount; i++)
                {
                    _receivedData[i] = new RingBuffer<double>(value);
                }
                
                // Reset statistics
                TotalSamples = 0;
                TotalBits = 0;
                _currentSampleIndex = 0;
                
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BufferSize)));
            }
        }

        #endregion

        #region IChannelConfigurable Implementation

        public void SetChannelSetting(int channelIndex, ChannelSettings settings)
        {
            if (channelIndex < 0 || channelIndex >= ChannelCount || _channelSettings == null)
                return;

            _channelSettings[channelIndex] = settings;
            
            // Update filter reference and reset if filter type changed
            var newFilter = settings?.Filter;
            if (_channelFilters[channelIndex] != newFilter)
            {
                _channelFilters[channelIndex] = newFilter;
                _channelFilters[channelIndex]?.Reset();
            }
        }

        public void UpdateChannelSettings(IReadOnlyList<ChannelSettings> channelSettings)
        {
            if (channelSettings == null) return;

            for (int i = 0; i < Math.Min(ChannelCount, channelSettings.Count); i++)
            {
                SetChannelSetting(i, channelSettings[i]);
            }
        }

        public void ResetChannelFilters()
        {
            if (_channelFilters == null) return;
            
            for (int i = 0; i < ChannelCount; i++)
            {
                _channelFilters[i]?.Reset();
            }
        }

        #endregion

        #region Data Processing

        private double ApplyChannelProcessing(int channel, double rawSample)
        {
            var settings = _channelSettings?[channel];
            if (settings == null) return rawSample;
            
            // Apply gain and offset
            double processed = rawSample * settings.Gain + settings.Offset;
            
            // Apply filter if configured
            var filter = _channelFilters?[channel];
            if (filter != null)
            {
                processed = filter.Filter(processed);
            }
            
            return processed;
        }

        #endregion

        #region IDataStream Implementation

        public void Connect()
        {
            if (string.IsNullOrWhiteSpace(FilePath))
            {
                StatusMessage = "Error: No file path specified";
                return;
            }

            StatusMessage = "Loading file...";
            
            try
            {
                // Use FileIOManager for all parsing
                var result = FileIOManager.ParseFile(FilePath);
                
                if (!result.IsSuccess)
                {
                    StatusMessage = $"Error: {result.ErrorMessage}";
                    return;
                }

                _fileHeader = result.Header;
                _fileData = result.Data;
                ChannelCount = result.Header.ChannelCount;

                int bufferSize = 10000; //Minimum buffer size
                if (ChannelCount > 0)
                    bufferSize = Math.Min(result.Data[0].Length, (int)10e6); //Limit to 10 million samples max

                _receivedData = new RingBuffer<double>[ChannelCount];
                for (int i = 0; i < ChannelCount; i++)
                {
                    _receivedData[i] = new RingBuffer<double>(bufferSize);
                }
                
                // Initialize channel processing arrays
                _channelSettings = new ChannelSettings[ChannelCount];
                _channelFilters = new IDigitalFilter[ChannelCount];

                IsConnected = true;
                _currentSampleIndex = 0;
                StatusMessage = $"Connected - File: {Path.GetFileName(FilePath)} ({result.Header.TotalSamples:N0} samples, {ChannelCount} channels)";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading file: {ex.Message}";
            }
        }

        public void Disconnect()
        {
            StopStreaming();
            IsConnected = false;
            _fileData = null;
            _fileHeader = null;
            _receivedData = null;
            _channelSettings = null;
            _channelFilters = null;
            ChannelCount = 0;
            StatusMessage = "Disconnected";
        }

        public void StartStreaming()
        {
            if (!IsConnected || IsStreaming || _fileData == null) return;

            IsStreaming = true;
            StatusMessage = "Streaming from file";
            
            ResetChannelFilters();
            
            // Calculate timer interval for smooth streaming
            int samplesPerChunk = Math.Max(1, (int)(SampleRate / 100)); // ~100 updates per second
            int timerInterval = Math.Max(1, (samplesPerChunk * 1000) / (int)SampleRate);
            
            _dataStreamingTimer = new Timer(StreamData, samplesPerChunk, 0, timerInterval);
        }

        public void StopStreaming()
        {
            if (!IsStreaming) return;
                
            IsStreaming = false;
            _dataStreamingTimer?.Dispose();
            _dataStreamingTimer = null;
            StatusMessage = IsConnected ? "Stopped" : "Disconnected";
        }

        private void StreamData(object state)
        {
            if (!IsStreaming || _fileData == null || _receivedData == null) return;

            int samplesPerChunk = (int)state;
            int samplesAvailable = _fileData[0].Length;
            int actualSamplesToRead = Math.Min(samplesPerChunk, samplesAvailable - _currentSampleIndex);
            
            if (actualSamplesToRead <= 0)
            {
                if (LoopPlayback)
                {
                    // Loop back to beginning
                    _currentSampleIndex = 0;
                    actualSamplesToRead = Math.Min(samplesPerChunk, samplesAvailable);
                }
                else
                {
                    // End of file reached, stop streaming
                    Task.Run(StopStreaming);
                    return;
                }
            }

            // Process and add data to ring buffers
            for (int channel = 0; channel < ChannelCount; channel++)
            {
                for (int i = 0; i < actualSamplesToRead; i++)
                {
                    double rawSample = _fileData[channel][_currentSampleIndex + i];
                    double processedSample = ApplyChannelProcessing(channel, rawSample);
                    _receivedData[channel].Add(processedSample);
                }
            }
            
            // Update position and statistics
            _currentSampleIndex += actualSamplesToRead;
            TotalSamples += actualSamplesToRead;
            TotalBits += actualSamplesToRead * ChannelCount * 16; // Assume 16 bits per sample
        }

        public int CopyLatestTo(int channel, double[] destination, int n)
        {
            if (channel < 0 || channel >= ChannelCount || _receivedData == null)
                return 0;
                
            return _receivedData[channel].CopyLatestTo(destination, n);
        }

        public void clearData()
        {
            if (_receivedData != null)
            {
                foreach (var ringBuffer in _receivedData)
                {
                    ringBuffer?.Clear();
                }
            }
            
            TotalSamples = 0;
            TotalBits = 0;
            _currentSampleIndex = 0;
            
            ResetChannelFilters();
        }

        public void Dispose()
        {
            if (_disposed) return;
                
            StopStreaming();
            _dataStreamingTimer?.Dispose();
            
            if (_receivedData != null)
            {
                foreach (var ringBuffer in _receivedData)
                {
                    ringBuffer?.Clear();
                }
            }
            
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}