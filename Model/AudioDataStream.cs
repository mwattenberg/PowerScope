using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace SerialPlotDN_WPF.Model
{
    public class AudioDataStream : IDataStream, IChannelConfigurable, IDisposable
    {
        private bool _disposed = false;
        private bool _isConnected = false;
        private bool _isStreaming = false;
        private string _statusMessage;
        
        // Audio capture components
        private WasapiCapture _audioCapture;
        private MMDevice _audioDevice;
        private readonly string _deviceName;
        private readonly int _sampleRate;
        private int _channelCount;
        
        // Data storage and processing
        public long TotalSamples { get; private set; }
        public long TotalBits { get; private set; }
        private RingBuffer<double>[] ReceivedData { get; set; }
        private readonly object _dataLock = new object();
        
        // Channel-specific processing
        private ChannelSettings[] _channelSettings;
        private IDigitalFilter[] _channelFilters;
        private readonly object _channelConfigLock = new object();
        
        // Audio processing parameters
        private const int DefaultSampleRate = 44100;
        private const int RingBufferDurationSeconds = 10;

        public int ChannelCount 
        { 
            get { return _channelCount; } 
        }
        
        /// <summary>
        /// Gets the sample rate being used by the audio device
        /// </summary>
        public int SampleRate 
        { 
            get { return _sampleRate; } 
        }

        /// <summary>
        /// Gets the name of the audio device being used
        /// </summary>
        public string DeviceName 
        { 
            get { return _deviceName; } 
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
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
            get { return "Audio"; } 
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

        public AudioDataStream(string deviceName = null, int sampleRate = 44100)
        {
            if (string.IsNullOrEmpty(deviceName))
            {
                MMDevice defaultDevice = GetDefaultAudioDevice();
                if (defaultDevice != null)
                    _deviceName = defaultDevice.FriendlyName;
                else
                    _deviceName = "Default";
            }
            else
            {
                _deviceName = deviceName;
            }
            
            _sampleRate = sampleRate;
            StatusMessage = "Disconnected";
            TotalSamples = 0;
            TotalBits = 0;
        }

        #region IChannelConfigurable Implementation

        public void SetChannelSetting(int channelIndex, ChannelSettings settings)
        {
            if (channelIndex < 0 || channelIndex >= ChannelCount)
                return;

            lock (_channelConfigLock)
            {
                if (_channelSettings == null) 
                    return;
                
                _channelSettings[channelIndex] = settings;
                
                // Update filter reference and reset if filter type changed
                IDigitalFilter newFilter = null;
                if (settings != null)
                    newFilter = settings.Filter;
                    
                if (_channelFilters[channelIndex] != newFilter)
                {
                    _channelFilters[channelIndex] = newFilter;
                    if (_channelFilters[channelIndex] != null)
                        _channelFilters[channelIndex].Reset();
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
                if (_channelFilters == null) 
                    return;
                
                for (int i = 0; i < ChannelCount; i++)
                {
                    if (_channelFilters[i] != null)
                        _channelFilters[i].Reset();
                }
            }
        }

        #endregion

        #region IDataStream Implementation

        public void Connect()
        {
            try
            {
                // Find and initialize audio device
                _audioDevice = FindAudioDevice(_deviceName);
                if (_audioDevice == null)
                {
                    StatusMessage = $"Audio device '{_deviceName}' not found";
                    IsConnected = false;
                    return;
                }

                // Initialize WASAPI capture with the specified sample rate
                WaveFormat mixFormat = _audioDevice.AudioClient.MixFormat;
                WaveFormat desiredFormat = new WaveFormat(_sampleRate, mixFormat.BitsPerSample, mixFormat.Channels);
                
                // Create WASAPI capture with the desired format
                _audioCapture = new WasapiCapture(_audioDevice, false, 100);
                
                // Get actual channel count from the audio format
                _channelCount = _audioCapture.WaveFormat.Channels;
                
                // Initialize ring buffers for each channel
                int ringBufferSize = _sampleRate * RingBufferDurationSeconds;
                ReceivedData = new RingBuffer<double>[_channelCount];
                for (int i = 0; i < _channelCount; i++)
                {
                    ReceivedData[i] = new RingBuffer<double>(ringBufferSize);
                }
                
                // Initialize channel processing arrays
                _channelSettings = new ChannelSettings[_channelCount];
                _channelFilters = new IDigitalFilter[_channelCount];
                
                // Set up data available event handler
                _audioCapture.DataAvailable += OnDataAvailable;
                _audioCapture.RecordingStopped += OnRecordingStopped;
                
                IsConnected = true;
                StatusMessage = $"Connected to {_audioDevice.FriendlyName} ({_channelCount} ch, {_sampleRate} Hz)";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Connection failed: {ex.Message}";
                IsConnected = false;
                CleanupAudioResources();
            }
        }

        public void Disconnect()
        {
            StopStreaming();
            CleanupAudioResources();
            IsConnected = false;
            StatusMessage = "Disconnected";
        }

        public void StartStreaming()
        {
            if (!_isConnected || _isStreaming || _audioCapture == null)
                return;

            try
            {
                // Reset filters when starting streaming
                ResetChannelFilters();
                
                _audioCapture.StartRecording();
                IsStreaming = true;
                StatusMessage = $"Streaming from {_audioDevice.FriendlyName}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to start streaming: {ex.Message}";
                IsStreaming = false;
            }
        }

        public void StopStreaming()
        {
            if (!_isStreaming || _audioCapture == null)
                return;

            try
            {
                _audioCapture.StopRecording();
                IsStreaming = false;
                
                if (_isConnected)
                {
                    StatusMessage = $"Stopped - {_audioDevice.FriendlyName}";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error stopping stream: {ex.Message}";
                IsStreaming = false;
            }
        }

        public int CopyLatestTo(int channel, double[] destination, int n)
        {
            if (channel < 0 || channel >= ChannelCount || ReceivedData == null)
                return 0;
            
            try
            {
                return ReceivedData[channel].CopyLatestTo(destination, n);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        public void clearData()
        {
            if (ReceivedData != null)
            {
                lock (_dataLock)
                {
                    foreach (RingBuffer<double> ringBuffer in ReceivedData)
                    {
                        if (ringBuffer != null)
                            ringBuffer.Clear();
                    }
                }
            }
            
            TotalSamples = 0;
            TotalBits = 0;
            
            // Reset filters when clearing data
            ResetChannelFilters();
        }

        #endregion

        #region Audio Processing

        private void OnDataAvailable(object sender, WaveInEventArgs e)
        {
            if (!IsStreaming || e.Buffer == null || e.BytesRecorded == 0)
                return;

            try
            {
                ProcessAudioData(e.Buffer, e.BytesRecorded);
            }
            catch (Exception ex)
            {
                // Log error but don't stop streaming
                StatusMessage = $"Audio processing error: {ex.Message}";
            }
        }

        private void OnRecordingStopped(object sender, StoppedEventArgs e)
        {
            IsStreaming = false;
            
            if (e.Exception != null)
            {
                StatusMessage = $"Recording stopped due to error: {e.Exception.Message}";
            }
            else if (_isConnected)
            {
                StatusMessage = $"Stopped - {_audioDevice.FriendlyName}";
            }
        }

        private void ProcessAudioData(byte[] buffer, int bytesRecorded)
        {
            WaveFormat format = _audioCapture.WaveFormat;
            int bytesPerSample = format.BitsPerSample / 8;
            int samplesRecorded = bytesRecorded / (bytesPerSample * format.Channels);
            
            if (samplesRecorded == 0) 
                return;

            lock (_dataLock)
            {
                // Pre-allocate arrays for each channel
                double[][] channelData = new double[_channelCount][];
                for (int ch = 0; ch < _channelCount; ch++)
                {
                    channelData[ch] = new double[samplesRecorded];
                }

                // Convert audio samples to double and separate channels
                switch (format.BitsPerSample)
                {
                    case 16:
                        ProcessInt16Samples(buffer, bytesRecorded, format.Channels, channelData);
                        break;
                    case 24:
                        ProcessInt24Samples(buffer, bytesRecorded, format.Channels, channelData);
                        break;
                    case 32:
                        if (format.Encoding == WaveFormatEncoding.IeeeFloat)
                        {
                            ProcessFloat32Samples(buffer, bytesRecorded, format.Channels, channelData);
                        }
                        else
                        {
                            ProcessInt32Samples(buffer, bytesRecorded, format.Channels, channelData);
                        }
                        break;
                }

                // Apply channel processing and add to ring buffers
                for (int ch = 0; ch < _channelCount; ch++)
                {
                    for (int s = 0; s < samplesRecorded; s++)
                    {
                        channelData[ch][s] = ApplyChannelProcessing(ch, channelData[ch][s]);
                    }
                    ReceivedData[ch].AddRange(channelData[ch]);
                }

                // Update statistics
                TotalSamples += samplesRecorded;
                TotalBits += bytesRecorded * 8;
            }
        }

        private void ProcessInt16Samples(byte[] buffer, int bytesRecorded, int channels, double[][] channelData)
        {
            int samplesPerChannel = bytesRecorded / (2 * channels);
            for (int i = 0; i < samplesPerChannel; i++)
            {
                for (int ch = 0; ch < channels; ch++)
                {
                    int byteIndex = (i * channels + ch) * 2;
                    short sample = BitConverter.ToInt16(buffer, byteIndex);
                    channelData[ch][i] = sample / 32768.0; // Normalize to [-1, 1]
                }
            }
        }

        private void ProcessInt24Samples(byte[] buffer, int bytesRecorded, int channels, double[][] channelData)
        {
            int samplesPerChannel = bytesRecorded / (3 * channels);
            for (int i = 0; i < samplesPerChannel; i++)
            {
                for (int ch = 0; ch < channels; ch++)
                {
                    int byteIndex = (i * channels + ch) * 3;
                    int sample = buffer[byteIndex] | (buffer[byteIndex + 1] << 8) | (buffer[byteIndex + 2] << 16);
                    if ((sample & 0x800000) != 0) // Sign extend
                        sample |= unchecked((int)0xFF000000);
                    channelData[ch][i] = sample / 8388608.0; // Normalize to [-1, 1]
                }
            }
        }

        private void ProcessInt32Samples(byte[] buffer, int bytesRecorded, int channels, double[][] channelData)
        {
            int samplesPerChannel = bytesRecorded / (4 * channels);
            for (int i = 0; i < samplesPerChannel; i++)
            {
                for (int ch = 0; ch < channels; ch++)
                {
                    int byteIndex = (i * channels + ch) * 4;
                    int sample = BitConverter.ToInt32(buffer, byteIndex);
                    channelData[ch][i] = sample / 2147483648.0; // Normalize to [-1, 1]
                }
            }
        }

        private void ProcessFloat32Samples(byte[] buffer, int bytesRecorded, int channels, double[][] channelData)
        {
            int samplesPerChannel = bytesRecorded / (4 * channels);
            for (int i = 0; i < samplesPerChannel; i++)
            {
                for (int ch = 0; ch < channels; ch++)
                {
                    int byteIndex = (i * channels + ch) * 4;
                    float sample = BitConverter.ToSingle(buffer, byteIndex);
                    channelData[ch][i] = sample; // Already normalized
                }
            }
        }

        private double ApplyChannelProcessing(int channel, double rawSample)
        {
            // Get channel settings safely
            ChannelSettings settings;
            IDigitalFilter filter;
            
            lock (_channelConfigLock)
            {
                settings = null;
                if (_channelSettings != null && channel < _channelSettings.Length)
                    settings = _channelSettings[channel];
                    
                filter = null;
                if (_channelFilters != null && channel < _channelFilters.Length)
                    filter = _channelFilters[channel];
            }
            
            if (settings == null)
                return rawSample;
            
            // Apply gain and offset
            double processed = settings.Gain * (rawSample + settings.Offset);
            
            // Apply filter if configured
            if (filter != null)
            {
                processed = filter.Filter(processed);
            }
            
            return processed;
        }

        #endregion

        #region Audio Device Management

        private static MMDevice GetDefaultAudioDevice()
        {
            try
            {
                MMDeviceEnumerator deviceEnumerator = new MMDeviceEnumerator();
                return deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
            }
            catch
            {
                return null;
            }
        }

        private static MMDevice FindAudioDevice(string deviceName)
        {
            try
            {
                MMDeviceEnumerator deviceEnumerator = new MMDeviceEnumerator();
                MMDeviceCollection devices = deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
                
                foreach (MMDevice device in devices)
                {
                    if (device.FriendlyName.Equals(deviceName, StringComparison.OrdinalIgnoreCase) ||
                        string.IsNullOrEmpty(deviceName))
                    {
                        return device;
                    }
                }
                
                // If no match found and deviceName was specified, try default device
                if (!string.IsNullOrEmpty(deviceName))
                {
                    return GetDefaultAudioDevice();
                }
            }
            catch
            {
                // Fall back to default device
            }
            
            return GetDefaultAudioDevice();
        }

        public static List<string> GetAvailableAudioDevices()
        {
            List<string> deviceNames = new List<string>();
            try
            {
                MMDeviceEnumerator deviceEnumerator = new MMDeviceEnumerator();
                MMDeviceCollection devices = deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
                
                foreach (MMDevice device in devices)
                {
                    deviceNames.Add(device.FriendlyName);
                }
            }
            catch
            {
                // Return empty list on error
            }
            
            return deviceNames;
        }

        #endregion

        #region Cleanup

        private void CleanupAudioResources()
        {
            try
            {
                if (_audioCapture != null)
                {
                    _audioCapture.DataAvailable -= OnDataAvailable;
                    _audioCapture.RecordingStopped -= OnRecordingStopped;
                    
                    if (_audioCapture.CaptureState != CaptureState.Stopped)
                    {
                        _audioCapture.StopRecording();
                    }
                    
                    _audioCapture.Dispose();
                    _audioCapture = null;
                }
                
                _audioDevice = null;
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            StopStreaming();
            CleanupAudioResources();
            
            if (ReceivedData != null)
            {
                foreach (RingBuffer<double> ringBuffer in ReceivedData)
                {
                    if (ringBuffer != null)
                        ringBuffer.Clear();
                }
            }

            _disposed = true;
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
