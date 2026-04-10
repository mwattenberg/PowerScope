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
    /// 
    /// Architecture Notes:
    /// - FTDI chip acts as SPI master
    /// - Slave ensures only complete data frames are sent
    /// - No residue handling needed - frames are complete or not present
    /// - Clock frequency parameter is in Hz, SPI class expects frequency divisor
    /// </summary>
    public class FTDI_SerialDataStream : IDataStream, IChannelConfigurable, IBufferResizable, IUpDownSampling
    {
        #region Private Fields

        private FtdiDevice? _ftdiDevice;
        private SPI? _spi;
        private Thread? _readThread;
        private CancellationTokenSource? _readThreadCancellation;
        private bool _disposed = false;
        private bool _isConnected;
        private bool _isStreaming;
        private string _statusMessage = string.Empty;

        // FTDI Configuration
        private uint _deviceIndex;
        private string? _deviceSerialNumber;
        private int _channelCount;
        private uint _clockFrequency;
        private int _spiMode;

        // SPI clock frequency configuration
        // FtdiSharp's SPI class expects a frequency divisor (1000 = 15MHz at 15000000 Hz input)
        // This will be refined when we address FtdiSharp library shortcomings
        private const uint ClockFrequencyDivisor = 1000;

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

        // FT2232H has 4096-byte RX buffer per channel.
        // The MPSSE command itself consumes 3 bytes of the TX buffer (opcode + 2 length bytes),
        // but the received SPI data goes into the RX buffer.
        // Requesting more than 4096 bytes in a single ReadWrite() causes the FTDI chip to
        // pause SPI clocking mid-transfer while it flushes the RX buffer over USB.
        // This works but adds an extra USB round-trip per 4K chunk, reducing throughput.
        // Clamping to 4094 bytes of data + 2 bytes next-count = 4096 total keeps it in one flush.
        private const int FTDI_RX_BUFFER_SIZE = 4096;
        private const int MAX_TRANSFER_DATA_BYTES = FTDI_RX_BUFFER_SIZE - NEXT_COUNT_LENGTH;

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
        /// <param name="spiMode">SPI mode (0 or 2, as only these are supported by FTDI MPSSE)</param>
        public FTDI_SerialDataStream(uint deviceIndex, uint clockFrequency, int channelCount, DataParser parser, int spiMode = 0)
        {
            _deviceIndex = deviceIndex;
            _clockFrequency = clockFrequency;
            _channelCount = channelCount;
            _parser = parser;
            _spiMode = spiMode;

            _upDownSampling = new UpDownSampling(1);
            _upDownSampling.PropertyChanged += OnUpDownSamplingPropertyChanged;

            int defaultBufferSize = 500000;
            InitializeRingBuffers(defaultBufferSize);

            _channelSettings = new ChannelSettings[channelCount];
            _channelFilters = new IDigitalFilter?[channelCount];

            _deviceSerialNumber = GetDeviceSerialNumberByIndex(deviceIndex);

            StatusMessage = "Ready";
            _isConnected = false;
            _isStreaming = false;
        }

        #endregion

        #region FTDI Device Management

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

        public static string? ExtractSerialNumber(string deviceString)
        {
            if (string.IsNullOrEmpty(deviceString))
                return null;

            int separatorIndex = deviceString.IndexOf(" - ");
            if (separatorIndex > 0)
            {
                return deviceString.Substring(0, separatorIndex).Trim();
            }

            return null;
        }

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

        public static string CreateDeviceDisplayName(FtdiDevice device)
        {
            string serialNumber = device.SerialNumber ?? "Unknown";
            string description = device.Description ?? "FTDI Device";
            
            string cleanDescription = description;
            if (cleanDescription.Contains("(") && cleanDescription.Contains(")"))
            {
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
            
            return $"{serialNumber} - {cleanDescription}";
        }

        public static string GetDeviceShortName(string deviceString)
        {
            if (string.IsNullOrEmpty(deviceString))
                return "Unknown";
                
            string? serialNumber = ExtractSerialNumber(deviceString);
            return string.IsNullOrEmpty(serialNumber) ? "Unknown" : serialNumber;
        }

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
                var devices = FtdiDevices.Scan();
                
                if (devices.Length == 0)
                {
                    StatusMessage = "No FTDI devices found";
                    IsConnected = false;
                    return;
                }

                if (_deviceIndex < devices.Length)
                {
                    _ftdiDevice = devices[_deviceIndex];
                }
                else
                {
                    StatusMessage = $"FTDI device at index {_deviceIndex} not found";
                    IsConnected = false;
                    return;
                }
                
                _spi = new SPI(_ftdiDevice.Value, spiMode: _spiMode, _clockFrequency / ClockFrequencyDivisor, latencyMs: 2);
                _spi.UseFastCsMethods = true;
                
                IsConnected = true;
                StatusMessage = "Connected to FTDI device";
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
            
            ResetChannelFilters();
            
            _upDownSampling.InitializeChannels(ChannelCount);
            _upDownSampling.Reset();

            // Stop any existing thread gracefully
            if (_readThread != null && _readThread.IsAlive)
            {
                IsStreaming = false;
                _readThreadCancellation?.Cancel();
                if (!_readThread.Join(2000))
                {
                    System.Diagnostics.Debug.WriteLine("WARNING: Previous read thread did not stop gracefully");
                }
            }

            // Pull CS LOW at start of streaming session (stays LOW until StopStreaming)
            _spi.CsLow();

            // Start new read thread with cancellation token
            IsStreaming = true;
            _readThreadCancellation = new CancellationTokenSource();
            _readThread = new Thread(() => ReadFTDIData(_readThreadCancellation.Token))
            { 
                IsBackground = true,
                Name = "FTDI_ReadThread"
            };
            _readThread.Start();

            StatusMessage = "Streaming - CS held LOW, ready for data";
        }

        public void StopStreaming()
        {
            if (!_isStreaming)
                return;

            IsStreaming = false;
            
            _readThreadCancellation?.Cancel();
            
            if (_readThread != null && _readThread.IsAlive)
            {
                if (!_readThread.Join(2000))
                {
                    System.Diagnostics.Debug.WriteLine("WARNING: Read thread did not stop gracefully within timeout");
                }
            }

            // Release CS HIGH at end of streaming session
            try
            {
                _spi?.CsHigh();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FTDI] Error releasing CS: {ex.Message}");
            }

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

        /// <summary>
        /// Queries the slave device to determine how many bytes are available.
        /// Used only for the initial bootstrap cycle. During steady-state streaming,
        /// the slave piggybacks the next byte count at the end of each data transfer.
        /// </summary>
        /// <returns>Number of bytes available from slave (0 to 65535), or -1 on error</returns>
        private int QuerySlaveByteCount()
        {
            try
            {
                // Step 1: Send query command (master asks "how many bytes do you have?")
                byte[] queryCmd = new byte[QUERY_CMD_LENGTH]
                {
                    QUERY_CMD_BYTE1,
                    QUERY_CMD_BYTE2,
                    QUERY_CMD_BYTE3,
                    QUERY_CMD_BYTE4
                };
                _spi.ReadWrite(queryCmd);
                
                // Step 2: Read the count response (slave sends 2 bytes: count_high, count_low)
                byte[] countRequest = new byte[COUNT_RESPONSE_LENGTH];
                byte[] countResponse = _spi.ReadWrite(countRequest);

                if (countResponse == null || countResponse.Length < COUNT_RESPONSE_LENGTH)
                {
                    System.Diagnostics.Debug.WriteLine($"[FTDI] Invalid count response length: {countResponse?.Length ?? 0}");
                    return -1;
                }
                
                // Convert response to uint16 (big-endian: high byte first)
                ushort availableBytes = (ushort)((countResponse[0] << 8) | countResponse[1]);

                return availableBytes;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FTDI] Query failed: {ex.Message}");
                return -1;
            }
        }

        /// <summary>
        /// Reads data from the slave and extracts the piggybacked next byte count.
        /// The slave appends 2 bytes (big-endian uint16) at the end of every data transfer
        /// indicating how many bytes it will have ready for the next request.
        /// This eliminates the need for a separate query on subsequent cycles.
        /// Uses ReadWriteNoFlush for maximum throughput — the piggybacked protocol
        /// guarantees we always read exactly what is produced, so no stale data accumulates.
        /// </summary>
        /// <param name="dataLength">Number of data bytes expected (not including the 2-byte next count)</param>
        /// <param name="nextByteCount">Output: the next transfer size reported by the slave</param>
        /// <returns>Byte array containing only the frame data (next count stripped), or null on error</returns>
        private byte[] ReadDataWithNextCount(int dataLength, out int nextByteCount)
        {
            nextByteCount = 0;

            if (dataLength <= 0)
                return null;

            // Clamp to FT2232H RX buffer size to avoid mid-transfer USB flushes.
            // The slave may have promised more, but we only read up to MAX_TRANSFER_DATA_BYTES.
            // Remaining data stays in the slave's ring buffer and will be picked up next cycle
            // via the piggybacked next-count (which reflects actual remaining data).
            if (dataLength > MAX_TRANSFER_DATA_BYTES)
                dataLength = MAX_TRANSFER_DATA_BYTES;

            try
            {
                // Request data bytes + 2 piggybacked next-count bytes in a single USB round-trip.
                // Uses ReadWriteNoFlush to skip the defensive FlushBuffer() call — in steady-state
                // streaming the RX buffer is always clean because we read exactly what we request.
                int totalBytes = dataLength + NEXT_COUNT_LENGTH;
                byte[] dummyData = new byte[totalBytes];
                byte[] response = _spi.ReadWriteNoFlush(dummyData);

                if (response == null || response.Length != totalBytes)
                {
                    System.Diagnostics.Debug.WriteLine($"[FTDI] Response length mismatch: expected {totalBytes}, got {response?.Length ?? 0}");
                    return null;
                }

                // Extract piggybacked next count from the last 2 bytes (big-endian)
                nextByteCount = (response[dataLength] << 8) | response[dataLength + 1];

                // Return only the frame data (strip the next count bytes)
                byte[] frameData = new byte[dataLength];
                Array.Copy(response, 0, frameData, 0, dataLength);

                return frameData;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FTDI] ReadDataWithNextCount failed: {ex.Message}");
                return null;
            }
        }

        private void ReadFTDIData(CancellationToken cancellation)
        {
            int consecutiveErrorCount = 0;
            const int maxConsecutiveErrors = 5;
            
            System.Diagnostics.Debug.WriteLine("[FTDI] Read thread started, CS is held LOW for entire session");

            long cycleCount = 0;
            Stopwatch cycleTimer = Stopwatch.StartNew();

            // Bootstrap: initial query to get the first byte count (costs 2 USB round-trips)
            int nextByteCount = 0;

            while (!cancellation.IsCancellationRequested && _spi != null)
            {
                try
                {
                    // If we have no promised byte count, fall back to query (bootstrap or recovery)
                    if (nextByteCount <= 0)
                    {
                        nextByteCount = QuerySlaveByteCount();

                        if (nextByteCount < 0)
                        {
                            consecutiveErrorCount++;
                            if (consecutiveErrorCount >= maxConsecutiveErrors)
                            {
                                HandleRuntimeDisconnection("Query failed after multiple retries");
                                break;
                            }
                            Thread.Sleep(1);
                            continue;
                        }

                        if (nextByteCount == 0)
                        {
                            // Slave has no data yet, wait and re-query
                            Thread.Sleep(1);
                            continue;
                        }
                    }

                    // Steady-state: single USB round-trip for data + piggybacked next count
                    int requestedBytes = nextByteCount;
                    int nextCount = 0;
                    byte[] rxData = ReadDataWithNextCount(requestedBytes, out nextCount);

                    if (rxData == null || rxData.Length == 0)
                    {
                        consecutiveErrorCount++;
                        nextByteCount = 0; // Force re-query on next cycle

                        if (consecutiveErrorCount >= maxConsecutiveErrors)
                        {
                            HandleRuntimeDisconnection("Data request failed after multiple retries");
                            break;
                        }
                    }
                    else
                    {
                        TotalBits += rxData.Length * 8;
                        ProcessReceivedData(rxData);

                        // Use piggybacked count for next cycle
                        nextByteCount = nextCount;

                        consecutiveErrorCount = 0;
                    }

                    cycleCount++;
                    if (cycleCount % 100 == 0)
                    {
                        double cyclesPerSecond = cycleCount / cycleTimer.Elapsed.TotalSeconds;
                        System.Diagnostics.Debug.WriteLine($"[FTDI] Cycle rate: {cyclesPerSecond:F1}/sec, Data: {requestedBytes} bytes, Next: {nextByteCount} bytes");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[FTDI] Read thread exception: {ex.Message}");
                    consecutiveErrorCount++;
                    nextByteCount = 0; // Force re-query on next cycle

                    if (consecutiveErrorCount >= maxConsecutiveErrors)
                    {
                        HandleRuntimeDisconnection($"Read error: {ex.Message}");
                        break;
                    }
                }

                // No Thread.Sleep here in steady-state: the USB round-trip in ReadWrite()
                // already provides the necessary pacing. Adding Thread.Sleep(1) on Windows
                // actually sleeps 1-15ms due to the default timer resolution (15.6ms),
                // which is the dominant bottleneck when cycle rates drop below ~250/sec.
                // Only sleep when we have no data to request (bootstrap/recovery).
            }
            
            System.Diagnostics.Debug.WriteLine("[FTDI] Read thread stopped");
        }

        private void ProcessReceivedData(byte[] readBuffer)
        {
            try
            {
                ParsedData parsedData = _parser.ParseData(readBuffer.AsSpan());
                
                if (parsedData.Data != null)
                {
                    AddDataToRingBuffers(parsedData.Data);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Data parsing error: {ex.Message}");
            }
        }

        private void AddDataToRingBuffers(double[][] parsedData)
        {
            int channelsToProcess = Math.Min(parsedData.Length, ChannelCount);
            
            double[][] finalData = new double[channelsToProcess][];
            
            Parallel.For(0, channelsToProcess, channel =>
            {
                if (parsedData[channel] != null && parsedData[channel].Length > 0)
                {
                    double[] channelProcessedSamples = new double[parsedData[channel].Length];
                    for (int sample = 0; sample < parsedData[channel].Length; sample++)
                    {
                        double processedSample = ApplyChannelProcessing(channel, parsedData[channel][sample]);
                        
                        if (!double.IsFinite(processedSample))
                        {
                            processedSample = 0.0;
                        }
                        
                        channelProcessedSamples[sample] = processedSample;
                    }
                    
                    double[] channelFinalData = channelProcessedSamples;
                    if (_upDownSampling.IsEnabled)
                    {
                        try
                        {
                            channelFinalData = _upDownSampling.ProcessChannelData(channel, channelProcessedSamples);
                            
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
            
            double processed = settings.Gain * (rawSample + settings.Offset);
            
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
    
        public event EventHandler Disposing;

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

            Disposing?.Invoke(this, EventArgs.Empty);

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
            
            _readThreadCancellation?.Dispose();
            
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        #endregion

        #region SPI Protocol Commands
        private const byte QUERY_CMD_BYTE1 = 0xFF;
        private const byte QUERY_CMD_BYTE2 = 0x00;
        private const byte QUERY_CMD_BYTE3 = 0xFF;
        private const byte QUERY_CMD_BYTE4 = 0xFF;
        
        private const int QUERY_CMD_LENGTH = 4;
        private const int COUNT_RESPONSE_LENGTH = 2;
        private const int NEXT_COUNT_LENGTH = 2;
        private const int MAX_RETRIES = 3;
        #endregion
    }
}
