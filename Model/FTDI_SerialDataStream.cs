using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FtdiSharp;
using FtdiSharp.Protocols;

namespace PowerScope.Model
{
    /// <summary>
    /// FTDI data stream implementation using FtdiSharp library
    /// Provides high-speed data acquisition with improved reliability and maintainability
    /// </summary>
    public class FTDI_SerialDataStream : IDataStream, IChannelConfigurable, IBufferResizable, IUpDownSampling
    {
        #region Private Fields

        private FtdiDevice? _ftdiDevice;
        private SPI? _spi;
        private Thread? _readThread;
        private bool _disposed = false;
        private bool _isConnected;
        private bool _isStreaming;
        private string _statusMessage = string.Empty;

        // FTDI Configuration
        private uint _deviceIndex;
        private string? _deviceSerialNumber;
        private int _channelCount;
        private uint _clockFrequency;  // Store the clock frequency for SPI configuration

        // Sample rate calculation
        private long _totalSamplesAtLastCalculation = 0;
        private readonly Stopwatch _sampleRateStopwatch = new Stopwatch();
        private double _calculatedSampleRate = 0.0;
        private readonly object _sampleRateCalculationLock = new object();
        private const double DefaultFilterAlpha = 0.1;
        private double _sampleRateFilterAlpha = DefaultFilterAlpha;

        // Up/Down sampling
        private readonly UpDownSampling _upDownSampling;

        // Data buffers and processing
        private RingBuffer<double>[]? _receivedData;
        private DataParser _parser;

        // Channel-specific processing
        private ChannelSettings[] _channelSettings;
        private IDigitalFilter?[] _channelFilters;
        private readonly object _channelConfigLock = new object();

        #endregion

        #region Properties

        public long TotalSamples { get; private set; }
        
        public long TotalBits { get; private set; }

        public int ChannelCount => _channelCount;

        public double SampleRate
        {
            get
            {
                lock (_sampleRateCalculationLock)
                {
                    double baseSampleRate = _calculatedSampleRate;
                    
                    if (_upDownSampling.IsEnabled)
                    {
                        return baseSampleRate * _upDownSampling.SampleRateMultiplier;
                    }
                    
                    return baseSampleRate;
                }
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

        public string StreamType => "FTDI MPSSE";

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

        #endregion

        #region Constructor

        /// <summary>
        /// Initializes a new instance of FTDI_SerialDataStream using FtdiSharp
        /// </summary>
        /// <param name="deviceIndex">FTDI device index (0 for first device)</param>
        /// <param name="clockFrequency">SPI clock frequency in Hz (e.g., 15000000 for 15MHz)</param>
        /// <param name="channelCount">Number of data channels</param>
        /// <param name="parser">Data parser for processing incoming data</param>
        public FTDI_SerialDataStream(uint deviceIndex, uint clockFrequency, int channelCount, DataParser parser)
        {
            _deviceIndex = deviceIndex;
            _clockFrequency = clockFrequency;  // Store the clock frequency
            _channelCount = channelCount;
            _parser = parser;

            // Initialize up/down sampling
            _upDownSampling = new UpDownSampling(1);
            _upDownSampling.PropertyChanged += OnUpDownSamplingPropertyChanged;

            // Initialize with default buffer size
            int defaultBufferSize = 500000;
            InitializeRingBuffers(defaultBufferSize);

            // Initialize channel processing arrays
            _channelSettings = new ChannelSettings[channelCount];
            _channelFilters = new IDigitalFilter?[channelCount];

            // Get device serial number for the specified index
            _deviceSerialNumber = GetDeviceSerialNumberByIndex(deviceIndex);

            StatusMessage = "Ready";

            // Initialize connection state
            _isConnected = false;
            _isStreaming = false;
        }

        #endregion

        #region FTDI Device Management

        /// <summary>
        /// Gets a list of available FTDI devices with their serial numbers and descriptions
        /// </summary>
        /// <returns>Array of device descriptions with serial numbers</returns>
        public static string[] GetAvailableDevices()
        {
            try
            {
                var devices = FtdiDevices.Scan();
                var deviceStrings = new string[devices.Length];
                
                for (int i = 0; i < devices.Length; i++)
                {
                    deviceStrings[i] = CreateDeviceDisplayName(devices[i]);
                }
                
                return deviceStrings;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FtdiSharp GetAvailableDevices error: {ex.Message}");
                return new string[0];
            }
        }

        /// <summary>
        /// Gets detailed information about available FTDI devices
        /// </summary>
        /// <returns>Array of FtdiDevice objects with device details</returns>
        public static FtdiDevice[] GetAvailableDeviceDetails()
        {
            try
            {
                return FtdiDevices.Scan();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FtdiSharp GetAvailableDeviceDetails error: {ex.Message}");
                return new FtdiDevice[0];
            }
        }

        /// <summary>
        /// Extracts the serial number from a device string returned by GetAvailableDevices()
        /// </summary>
        /// <param name="deviceString">Device string in format "SerialNumber - Description"</param>
        /// <returns>Serial number or null if not found</returns>
        public static string? ExtractSerialNumber(string deviceString)
        {
            if (string.IsNullOrEmpty(deviceString))
                return null;

            // Format: "SerialNumber - Description"
            int separatorIndex = deviceString.IndexOf(" - ");
            if (separatorIndex > 0)
            {
                return deviceString.Substring(0, separatorIndex).Trim();
            }

            return null;
        }

        /// <summary>
        /// Gets device information by index (0-based)
        /// </summary>
        /// <param name="deviceIndex">Zero-based device index</param>
        /// <returns>Device information or null if not found</returns>
        public static FtdiDevice? GetDeviceInfo(uint deviceIndex)
        {
            try
            {
                var devices = FtdiDevices.Scan();
                if (deviceIndex < devices.Length)
                {
                    return devices[deviceIndex];
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FtdiSharp GetDeviceInfo error: {ex.Message}");
            }
            
            return null;
        }

        /// <summary>
        /// Creates a user-friendly display name for FTDI devices
        /// </summary>
        /// <param name="device">FtdiDevice object</param>
        /// <returns>Formatted display string</returns>
        public static string CreateDeviceDisplayName(FtdiDevice device)
        {
            string serialNumber = device.SerialNumber ?? "Unknown";
            string description = device.Description ?? "FTDI Device";
            
            // Clean up the description by removing redundant information
            string cleanDescription = description;
            if (cleanDescription.Contains("(") && cleanDescription.Contains(")"))
            {
                // Remove serial number from description if it's already there
                int parenIndex = cleanDescription.LastIndexOf('(');
                if (parenIndex > 0)
                {
                    string beforeParen = cleanDescription.Substring(0, parenIndex).Trim();
                    if (!string.IsNullOrEmpty(beforeParen))
                    {
                        cleanDescription = beforeParen;
                    }
                }
            }
            
            // Format: "SerialNumber - CleanDescription"
            return $"{serialNumber} - {cleanDescription}";
        }

        /// <summary>
        /// Gets a shortened device identifier for logging or status display
        /// </summary>
        /// <param name="deviceString">Full device string from GetAvailableDevices()</param>
        /// <returns>Short identifier</returns>
        public static string GetDeviceShortName(string deviceString)
        {
            if (string.IsNullOrEmpty(deviceString))
                return "Unknown";
                
            // Extract just the serial number for short identification
            string? serialNumber = ExtractSerialNumber(deviceString);
            return string.IsNullOrEmpty(serialNumber) ? "Unknown" : serialNumber;
        }

        /// <summary>
        /// Gets device serial number by index for backward compatibility
        /// </summary>
        /// <param name="deviceIndex">Zero-based device index</param>
        /// <returns>Device serial number or null if not found</returns>
        private static string? GetDeviceSerialNumberByIndex(uint deviceIndex)
        {
            try
            {
                var devices = FtdiDevices.Scan();
                if (deviceIndex < devices.Length)
                {
                    return devices[deviceIndex].SerialNumber;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FtdiSharp GetDeviceSerialNumberByIndex error: {ex.Message}");
            }
            
            return null;
        }

        #endregion

        #region IDataStream Implementation

        public void Connect()
        {
            try
            {
                // Find the device by serial number or index
                var devices = FtdiDevices.Scan();
                
                if (!string.IsNullOrEmpty(_deviceSerialNumber))
                {
                    var foundDevice = devices.FirstOrDefault(d => d.SerialNumber == _deviceSerialNumber);
                    if (!string.IsNullOrEmpty(foundDevice.SerialNumber))
                    {
                        _ftdiDevice = foundDevice;
                    }
                }
                else if (_deviceIndex < devices.Length)
                {
                    _ftdiDevice = devices[_deviceIndex];
                }
                
                if (_ftdiDevice == null || string.IsNullOrEmpty(_ftdiDevice.Value.SerialNumber))
                {
                    StatusMessage = $"FTDI device not found (index: {_deviceIndex}, serial: {_deviceSerialNumber})";
                    IsConnected = false;
                    return;
                }

            

                // Create SPI protocol handler with calculated slowDownFactor
                _spi = new SPI(_ftdiDevice.Value, spiMode: 1, _clockFrequency / 1000);
                //_spi = new SPI(_ftdiDevice.Value, spiMode: 1);

                IsConnected = true;
                StatusMessage = $"Connected to {_ftdiDevice.Value.SerialNumber}";
            }
            catch (Exception ex)
            {
                IsConnected = false;
                StatusMessage = $"Connection failed: {ex.Message}";
                try
                {
                    _spi?.Dispose();
                    _spi = null;
                }
                catch { }
            }
        }

        public void Disconnect()
        {
            StopStreaming();
            
            try
            {
                _spi?.Dispose();
                _spi = null;
                _ftdiDevice = null;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error during disconnect: {ex.Message}";
            }
            
            IsConnected = false;
            StatusMessage = "Disconnected";
        }

        public void StartStreaming()
        {
            if (!_isConnected || _isStreaming || _spi == null)
                return;

            IsStreaming = true;
            
            // Reset filters when starting streaming
            ResetChannelFilters();
            
            // Initialize up/down sampling for current channel configuration
            _upDownSampling.InitializeChannels(ChannelCount);
            _upDownSampling.Reset();

            // Stop any existing thread
            if (_readThread != null && _readThread.IsAlive)
            {
                IsStreaming = false;
                _readThread.Join(1000);
            }

            IsStreaming = true;
            _readThread = new Thread(ReadFTDIData) { IsBackground = true };
            _readThread.Start();

            StatusMessage = "Streaming";
        }

        public void StopStreaming()
        {
            if (!_isStreaming)
                return;

            IsStreaming = false;
            
            if (_readThread != null && _readThread.IsAlive)
                _readThread.Join(1000);

            if (_isConnected)
            {
                StatusMessage = "Stopped";
            }
        }

        public int CopyLatestTo(int channel, double[] destination, int n)
        {
            if (channel < 0 || channel >= (_receivedData?.Length ?? 0))
                return 0;
            
            if (!_isConnected && !_isStreaming)
                return 0;
                
            try
            {
                return _receivedData![channel].CopyLatestTo(destination, n);
            }
            catch (Exception)
            {
                return 0;
            }
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
            
            // Reset sample rate calculation
            lock (_sampleRateCalculationLock)
            {
                _calculatedSampleRate = 0.0;
                _sampleRateStopwatch.Reset();
                _totalSamplesAtLastCalculation = 0;
            }
            
            ResetChannelFilters();
            _upDownSampling?.Reset();
        }

        #endregion

        #region Data Reading and Processing

        private void ReadFTDIData()
        {
            const int readSize = 1024; // Adjust based on your needs
            int consecutiveErrorCount = 0;
            const int maxConsecutiveErrors = 5;

            while (_isStreaming && _spi != null)
            {
                try
                {
                    // Use FtdiSharp SPI to read data
                    byte[] readData = _spi.ReadBytes(readSize);
                    
                    if (readData.Length > 0)
                    {
                        TotalBits += readData.Length * 8;
                        ProcessReceivedData(readData);
                        consecutiveErrorCount = 0;
                    }
                }
                catch (Exception ex)
                {
                    consecutiveErrorCount++;
                    if (consecutiveErrorCount >= maxConsecutiveErrors)
                    {
                        HandleRuntimeDisconnection($"Read error: {ex.Message}");
                        break;
                    }
                    
                }
                
                Thread.Sleep(50);
            }
        }

        private void ProcessReceivedData(byte[] readBuffer)
        {
            try
            {
                // Parse the data using the configured parser
                ParsedData parsedData = _parser.ParseData(readBuffer.AsSpan());
                
                if (parsedData.Data != null)
                {
                    AddDataToRingBuffers(parsedData.Data);
                }
            }
            catch (Exception ex)
            {
                // Log parsing error but continue processing
                System.Diagnostics.Debug.WriteLine($"Data parsing error: {ex.Message}");
            }
        }

        private void AddDataToRingBuffers(double[][] parsedData)
        {
            int channelsToProcess = Math.Min(parsedData.Length, ChannelCount);
            
            // Pre-allocate arrays for parallel processing
            double[][] finalData = new double[channelsToProcess][];
            
            // Process all channels in parallel
            Parallel.For(0, channelsToProcess, channel =>
            {
                if (parsedData[channel] != null && parsedData[channel].Length > 0)
                {
                    // Apply channel processing (gain, offset, filtering)
                    double[] channelProcessedSamples = new double[parsedData[channel].Length];
                    for (int sample = 0; sample < parsedData[channel].Length; sample++)
                    {
                        double processedSample = ApplyChannelProcessing(channel, parsedData[channel][sample]);
                        
                        // Safety check for invalid values
                        if (!double.IsFinite(processedSample))
                        {
                            processedSample = 0.0;
                        }
                        
                        channelProcessedSamples[sample] = processedSample;
                    }
                    
                    // Apply up/down sampling if enabled
                    double[] channelFinalData = channelProcessedSamples;
                    if (_upDownSampling.IsEnabled)
                    {
                        try
                        {
                            channelFinalData = _upDownSampling.ProcessChannelData(channel, channelProcessedSamples);
                            
                            // Safety check for up/down sampled data
                            for (int i = 0; i < channelFinalData.Length; i++)
                            {
                                if (!double.IsFinite(channelFinalData[i]))
                                {
                                    channelFinalData[i] = 0.0;
                                }
                            }
                        }
                        catch (Exception)
                        {
                            channelFinalData = channelProcessedSamples;
                        }
                    }
                    
                    finalData[channel] = channelFinalData;
                }
            });
            
            // Add processed data to ring buffers
            for (int channel = 0; channel < channelsToProcess; channel++)
            {
                if (finalData[channel] != null && _receivedData != null)
                {
                    _receivedData[channel].AddRange(finalData[channel]);
                }
            }
            
            if (channelsToProcess > 0 && parsedData[0] != null)
            {
                int actualSamplesGenerated = finalData[0]?.Length ?? parsedData[0].Length;
                TotalSamples += actualSamplesGenerated;
                
                UpdateSampleRateCalculation(actualSamplesGenerated);
            }
        }

        private double ApplyChannelProcessing(int channel, double rawSample)
        {
            ChannelSettings? settings;
            IDigitalFilter? filter;
            
            lock (_channelConfigLock)
            {
                settings = _channelSettings[channel];
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

        private void UpdateSampleRateCalculation(int newSamples)
        {
            lock (_sampleRateCalculationLock)
            {
                if (!_sampleRateStopwatch.IsRunning)
                {
                    _sampleRateStopwatch.Start();
                    _totalSamplesAtLastCalculation = TotalSamples;
                    return;
                }
                
                double elapsedSeconds = _sampleRateStopwatch.Elapsed.TotalSeconds;
                
                if (elapsedSeconds >= 0.5)
                {
                    long samplesSinceLastCalculation = TotalSamples - _totalSamplesAtLastCalculation;
                    double instantaneousSampleRate = samplesSinceLastCalculation / elapsedSeconds;
                    
                    if (_calculatedSampleRate == 0.0)
                    {
                        _calculatedSampleRate = instantaneousSampleRate;
                    }
                    else
                    {
                        _calculatedSampleRate = _sampleRateFilterAlpha * instantaneousSampleRate + 
                                              (1.0 - _sampleRateFilterAlpha) * _calculatedSampleRate;
                    }
                    
                    _totalSamplesAtLastCalculation = TotalSamples;
                    _sampleRateStopwatch.Restart();
                    
                    OnPropertyChanged(nameof(SampleRate));
                }
            }
        }

        private void HandleRuntimeDisconnection(string reason)
        {
            try
            {
                StatusMessage = $"Disconnected: {reason}";
                IsStreaming = false;
                try
                {
                    _spi?.Dispose();
                    _spi = null;
                }
                catch { }
                IsConnected = false;
            }
            catch (Exception ex)
            {
                try
                {
                    StatusMessage = $"Error during disconnection handling: {ex.Message}";
                    IsConnected = false;
                    IsStreaming = false;
                }
                catch
                {
                    _isConnected = false;
                    _isStreaming = false;
                }
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

        #region IBufferResizable Implementation

        public int BufferSize 
        { 
            get 
            { 
                lock (_channelConfigLock)
                {
                    return _receivedData?[0]?.Capacity ?? 0;
                }
            }
            set
            {
                if (value <= 0)
                    return;

                lock (_channelConfigLock)
                {
                    if (_receivedData != null)
                    {
                        foreach (var buffer in _receivedData)
                        {
                            buffer?.Clear();
                        }
                    }

                    InitializeRingBuffers(value);
                    
                    TotalSamples = 0;
                    TotalBits = 0;
                }
                
                OnPropertyChanged(nameof(BufferSize));
            }
        }

        private void InitializeRingBuffers(int bufferSize)
        {
            _receivedData = new RingBuffer<double>[ChannelCount];
            for (int i = 0; i < ChannelCount; i++)
            {
                _receivedData[i] = new RingBuffer<double>(bufferSize);
            }
        }

        #endregion

        #region IUpDownSampling Implementation

        public int UpDownSamplingFactor
        {
            get { return _upDownSampling.SamplingFactor; }
            set { _upDownSampling.SamplingFactor = value; }
        }

        public double SampleRateMultiplier
        {
            get { return _upDownSampling.SampleRateMultiplier; }
        }

        public bool IsUpDownSamplingEnabled
        {
            get { return _upDownSampling.IsEnabled; }
        }

        public string UpDownSamplingDescription
        {
            get { return _upDownSampling.GetDescription(); }
        }

        private void OnUpDownSamplingPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(UpDownSampling.SamplingFactor))
            {
                OnPropertyChanged(nameof(UpDownSamplingFactor));
                OnPropertyChanged(nameof(SampleRateMultiplier));
                OnPropertyChanged(nameof(IsUpDownSamplingEnabled));
                OnPropertyChanged(nameof(UpDownSamplingDescription));
                OnPropertyChanged(nameof(SampleRate));
                
                _upDownSampling.InitializeChannels(ChannelCount);
            }
        }

        #endregion

        #region INotifyPropertyChanged Implementation

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion

        #region IDisposable Implementation

        public void Dispose()
        {
            if (_disposed)
                return;

            if (_isStreaming)
                StopStreaming();

            try
            {
                _spi?.Dispose();
            }
            catch { }

            if (_receivedData != null)
            {
                foreach (var ringBuffer in _receivedData)
                {
                    ringBuffer?.Clear();
                }
            }
            
            if (_upDownSampling != null)
            {
                _upDownSampling.PropertyChanged -= OnUpDownSamplingPropertyChanged;
            }
            
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}