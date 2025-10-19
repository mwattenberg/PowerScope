using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace PowerScope.Model
{
    /// <summary>
    /// Exception thrown when an FTDI device operation fails
    /// </summary>
    public class FTDIException : Exception
    {
        public uint ErrorCode { get; }

        public FTDIException(uint errorCode, string operation) 
            : base($"FTDI operation '{operation}' failed with error code: {errorCode}")
        {
            ErrorCode = errorCode;
        }

        public FTDIException(uint errorCode, string operation, Exception innerException) 
            : base($"FTDI operation '{operation}' failed with error code: {errorCode}", innerException)
        {
            ErrorCode = errorCode;
        }
    }

    /// <summary>
    /// Data stream implementation for FTDI MPSSE SPI interface
    /// Provides high-speed data acquisition using FTDI's Multi-Protocol Synchronous Serial Engine (MPSSE)
    /// </summary>
    public class FTDI_SerialDataStream : IDataStream, IChannelConfigurable, IBufferResizable, IUpDownSampling
    {
        #region FTDI D2XX DLL Imports (x64)
        
        // Use x64 FTDI DLL - assuming 64-bit system
        private const string FtdiDllName = "ftd2xx64.dll";
        
        [DllImport(FtdiDllName)]
        private static extern uint FT_Open(uint deviceNumber, ref IntPtr ftHandle);
        
        [DllImport(FtdiDllName)]
        private static extern uint FT_Close(IntPtr ftHandle);
        
        [DllImport(FtdiDllName)]
        private static extern uint FT_SetBitMode(IntPtr ftHandle, byte mask, byte mode);
        
        [DllImport(FtdiDllName)]
        private static extern uint FT_SetUSBParameters(IntPtr ftHandle, uint inTransferSize, uint outTransferSize);
        
        [DllImport(FtdiDllName)]
        private static extern uint FT_SetLatencyTimer(IntPtr ftHandle, byte latency);
        
        [DllImport(FtdiDllName)]
        private static extern uint FT_Write(IntPtr ftHandle, byte[] buffer, uint bytesToWrite, ref uint bytesWritten);
        
        [DllImport(FtdiDllName)]
        private static extern uint FT_Read(IntPtr ftHandle, byte[] buffer, uint bytesToRead, ref uint bytesRead);
        
        [DllImport(FtdiDllName)]
        private static extern uint FT_Purge(IntPtr ftHandle, uint mask);
        
        [DllImport(FtdiDllName)]
        private static extern uint FT_GetQueueStatus(IntPtr ftHandle, ref uint rxBytes);

        [DllImport(FtdiDllName)]
        private static extern uint FT_GetDeviceInfoList([Out] FT_DEVICE_LIST_INFO_NODE[] pDest, ref uint numDevices);

        [DllImport(FtdiDllName)]
        private static extern uint FT_ListDevices(uint argument1, IntPtr argument2, uint flags);

        [DllImport(FtdiDllName)]
        private static extern uint FT_CreateDeviceInfoList(ref uint numDevices);

        #endregion

        #region FTDI Constants

        private const uint FT_OK = 0;
        private const byte FT_BITMODE_RESET = 0x00;
        private const byte FT_BITMODE_MPSSE = 0x02;
        private const uint FT_PURGE_RX = 1;
        private const uint FT_PURGE_TX = 2;

        // MPSSE Commands
        private const byte MPSSE_WRITE_NEG = 0x01;  // Write on negative clock edge
        private const byte MPSSE_BITMODE = 0x02;     // Bit mode
        private const byte MPSSE_READ_NEG = 0x04;    // Read on negative clock edge
        private const byte MPSSE_LSB_FIRST = 0x08;   // LSB first
        private const byte MPSSE_DO_WRITE = 0x10;    // Write TDI/DO
        private const byte MPSSE_DO_READ = 0x20;     // Read TDO/DI
        private const byte MPSSE_WRITE_TMS = 0x40;   // Write TMS/CS

        // Common MPSSE commands
        private const byte MPSSE_CMD_SET_DATA_BITS_LOW = 0x80;
        private const byte MPSSE_CMD_SET_DATA_BITS_HIGH = 0x82;
        private const byte MPSSE_CMD_GET_DATA_BITS_LOW = 0x81;
        private const byte MPSSE_CMD_GET_DATA_BITS_HIGH = 0x83;
        private const byte MPSSE_CMD_SET_CLOCK_DIVISOR = 0x86;
        private const byte MPSSE_CMD_DISABLE_CLOCK_DIVIDE_BY_5 = 0x8A;
        private const byte MPSSE_CMD_ENABLE_3_PHASE_CLOCK = 0x8C;

        #endregion

        #region Private Fields

        private IntPtr _ftHandle = IntPtr.Zero;
        private Thread _readThread;
        private bool _disposed = false;
        private bool _isConnected;
        private bool _isStreaming;
        private string _statusMessage;

        // FTDI Configuration
        private uint _deviceIndex;
        private uint _spiClockFrequency;
        private int _channelCount;

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
        private RingBuffer<double>[] _receivedData;
        private readonly byte[] _readBuffer;
        private readonly byte[] _workingBuffer;
        private DataParser _parser;

        // Channel-specific processing
        private ChannelSettings[] _channelSettings;
        private IDigitalFilter[] _channelFilters;
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
        /// Initializes a new instance of FTDI_SerialDataStream
        /// </summary>
        /// <param name="deviceIndex">FTDI device index (0 for first device)</param>
        /// <param name="clockFrequency">SPI clock frequency in Hz</param>
        /// <param name="channelCount">Number of data channels</param>
        /// <param name="parser">Data parser for processing incoming data</param>
        public FTDI_SerialDataStream(uint deviceIndex, uint clockFrequency, int channelCount, DataParser parser)
        {
            _deviceIndex = deviceIndex;
            _spiClockFrequency = clockFrequency;
            _channelCount = channelCount;
            _parser = parser;

            // Initialize up/down sampling
            _upDownSampling = new UpDownSampling(1);
            _upDownSampling.PropertyChanged += OnUpDownSamplingPropertyChanged;

            // Initialize buffers
            _readBuffer = new byte[16384]; // 16KB read buffer
            _workingBuffer = new byte[32768]; // 32KB working buffer

            // Initialize with default buffer size
            int defaultBufferSize = 500000;
            InitializeRingBuffers(defaultBufferSize);

            // Initialize channel processing arrays
            _channelSettings = new ChannelSettings[channelCount];
            _channelFilters = new IDigitalFilter[channelCount];

            // Verify FTDI DLL availability during initialization
            var dllStatus = VerifyFTDIDLL();
            if (dllStatus.IsAvailable)
            {
                StatusMessage = $"FTDI DLL loaded successfully - {dllStatus.Message}";
            }
            else
            {
                StatusMessage = $"FTDI DLL verification failed - {dllStatus.Message}";
            }

            // Initialize connection state
            _isConnected = false;
            _isStreaming = false;
        }

        #endregion

        #region FTDI Device Management

        /// <summary>
        /// Gets a list of available FTDI devices with their serial numbers and descriptions
        /// </summary>
        /// <returns>Array of device descriptions with serial numbers, empty array if DLL not available</returns>
        public static string[] GetAvailableDevices()
        {
            try
            {
                // First verify the DLL is available
                var dllStatus = VerifyFTDIDLL();
                if (!dllStatus.IsAvailable)
                {
                    // Return empty array but log the reason
                    System.Diagnostics.Debug.WriteLine($"FTDI GetAvailableDevices failed: {dllStatus.Message}");
                    return new string[0];
                }

                // Try to get device count
                uint numDevices = 0;
                uint status = FT_CreateDeviceInfoList(ref numDevices);
                
                if (status != FT_OK)
                {
                    System.Diagnostics.Debug.WriteLine($"FTDI CreateDeviceInfoList failed with status: {status} ({GetFTDIStatusDescription(status)})");
                    return new string[0];
                }

                if (numDevices == 0)
                {
                    return new string[0];
                }

                // Allocate array for device info
                var deviceInfoArray = new FT_DEVICE_LIST_INFO_NODE[numDevices];
                
                // Get device information list
                status = FT_GetDeviceInfoList(deviceInfoArray, ref numDevices);
                
                if (status != FT_OK)
                {
                    System.Diagnostics.Debug.WriteLine($"FTDI GetDeviceInfoList failed with status: {status} ({GetFTDIStatusDescription(status)})");
                    return new string[0];
                }

                // Build array of device strings with serial numbers
                string[] devices = new string[numDevices];
                for (int i = 0; i < numDevices; i++)
                {
                    var deviceInfo = deviceInfoArray[i];
                    
                    // Use the new helper method for consistent formatting
                    devices[i] = CreateDeviceDisplayName(deviceInfo, i);
                }
                
                return devices;
            }
            catch (DllNotFoundException ex)
            {
                System.Diagnostics.Debug.WriteLine($"FTDI DLL not found: {ex.Message}");
                return new string[0];
            }
            catch (BadImageFormatException ex)
            {
                System.Diagnostics.Debug.WriteLine($"FTDI DLL architecture mismatch: {ex.Message}");
                return new string[0];
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FTDI GetAvailableDevices error: {ex.Message}");
                return new string[0];
            }
        }

        /// <summary>
        /// Gets detailed information about available FTDI devices
        /// </summary>
        /// <returns>Array of FT_DEVICE_LIST_INFO_NODE structures with device details</returns>
        public static FT_DEVICE_LIST_INFO_NODE[] GetAvailableDeviceDetails()
        {
            try
            {
                // First verify the DLL is available
                var dllStatus = VerifyFTDIDLL();
                if (!dllStatus.IsAvailable)
                {
                    System.Diagnostics.Debug.WriteLine($"FTDI GetAvailableDeviceDetails failed: {dllStatus.Message}");
                    return new FT_DEVICE_LIST_INFO_NODE[0];
                }

                // Try to get device count
                uint numDevices = 0;
                uint status = FT_CreateDeviceInfoList(ref numDevices);
                
                if (status != FT_OK || numDevices == 0)
                {
                    return new FT_DEVICE_LIST_INFO_NODE[0];
                }

                // Allocate array for device info
                var deviceInfoArray = new FT_DEVICE_LIST_INFO_NODE[numDevices];
                
                // Get device information list
                status = FT_GetDeviceInfoList(deviceInfoArray, ref numDevices);
                
                if (status != FT_OK)
                {
                    System.Diagnostics.Debug.WriteLine($"FTDI GetDeviceInfoList failed with status: {status} ({GetFTDIStatusDescription(status)})");
                    return new FT_DEVICE_LIST_INFO_NODE[0];
                }

                return deviceInfoArray;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FTDI GetAvailableDeviceDetails error: {ex.Message}");
                return new FT_DEVICE_LIST_INFO_NODE[0];
            }
        }

        /// <summary>
        /// Extracts the serial number from a device string returned by GetAvailableDevices()
        /// </summary>
        /// <param name="deviceString">Device string in format "SerialNumber - Description"</param>
        /// <returns>Serial number or null if not found</returns>
        public static string ExtractSerialNumber(string deviceString)
        {
            if (string.IsNullOrEmpty(deviceString))
                return null;

            // New format: "SerialNumber - Description"
            int separatorIndex = deviceString.IndexOf(" - ");
            if (separatorIndex > 0)
            {
                return deviceString.Substring(0, separatorIndex).Trim();
            }

            // Fallback: try old format "Serial: XXXXXXXX - Description"  
            const string serialPrefix = "Serial: ";
            int startIndex = deviceString.IndexOf(serialPrefix);
            if (startIndex == -1)
                return null;

            startIndex += serialPrefix.Length;
            int endIndex = deviceString.IndexOf(" - ", startIndex);
            if (endIndex == -1)
                endIndex = deviceString.Length;

            if (startIndex < endIndex)
            {
                return deviceString.Substring(startIndex, endIndex - startIndex).Trim();
            }

            return null;
        }

        /// <summary>
        /// Gets device information by index (0-based)
        /// </summary>
        /// <param name="deviceIndex">Zero-based device index</param>
        /// <returns>Device information or null if not found</returns>
        public static FT_DEVICE_LIST_INFO_NODE? GetDeviceInfo(uint deviceIndex)
        {
            try
            {
                var devices = GetAvailableDeviceDetails();
                if (deviceIndex < devices.Length)
                {
                    return devices[deviceIndex];
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FTDI GetDeviceInfo error: {ex.Message}");
            }
            
            return null;
        }

        private bool OpenDevice()
        {
            uint status = FT_Open(_deviceIndex, ref _ftHandle);
            if (status != FT_OK)
            {
                StatusMessage = $"Failed to open FTDI device {_deviceIndex}. Error: {status}";
                return false;
            }
            
            StatusMessage = "FTDI device opened successfully";
            return true;
        }

        private bool ConfigureMPSSE()
        {
            if (_ftHandle == IntPtr.Zero)
            {
                StatusMessage = "Device not opened";
                return false;
            }

            try
            {
                // Reset the device
                uint status = FT_SetBitMode(_ftHandle, 0x00, FT_BITMODE_RESET);
                if (status != FT_OK)
                {
                    StatusMessage = $"Failed to reset device. Error: {status}";
                    return false;
                }
                Thread.Sleep(50);

                // Set MPSSE mode
                status = FT_SetBitMode(_ftHandle, 0x00, FT_BITMODE_MPSSE);
                if (status != FT_OK)
                {
                    StatusMessage = $"Failed to set MPSSE mode. Error: {status}";
                    return false;
                }
                Thread.Sleep(50);

                // Configure USB transfer sizes
                status = FT_SetUSBParameters(_ftHandle, 65536, 65536);
                if (status != FT_OK)
                {
                    StatusMessage = $"Failed to set USB parameters. Error: {status}";
                    return false;
                }

                // Set latency timer to 1ms
                status = FT_SetLatencyTimer(_ftHandle, 1);
                if (status != FT_OK)
                {
                    StatusMessage = $"Failed to set latency timer. Error: {status}";
                    return false;
                }

                // Purge buffers
                status = FT_Purge(_ftHandle, FT_PURGE_RX | FT_PURGE_TX);
                if (status != FT_OK)
                {
                    StatusMessage = $"Failed to purge buffers. Error: {status}";
                    return false;
                }

                StatusMessage = "MPSSE mode configured successfully";
                return true;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Exception during MPSSE configuration: {ex.Message}";
                return false;
            }
        }

        private bool ConfigureSPI()
        {
            if (_ftHandle == IntPtr.Zero)
            {
                StatusMessage = "Device not opened";
                return false;
            }

            try
            {
                byte[] buffer = new byte[20];
                int idx = 0;

                // Disable clock divide-by-5 for higher frequencies
                buffer[idx++] = MPSSE_CMD_DISABLE_CLOCK_DIVIDE_BY_5;

                // Disable adaptive clocking
                buffer[idx++] = 0x97;

                // Enable 3-phase clocking for better reliability
                buffer[idx++] = MPSSE_CMD_ENABLE_3_PHASE_CLOCK;

                // Set clock divisor
                // Clock = 60MHz / ((1 + divisor) * 2)
                uint divisor = (30000000 / _spiClockFrequency) - 1;
                buffer[idx++] = MPSSE_CMD_SET_CLOCK_DIVISOR;
                buffer[idx++] = (byte)(divisor & 0xFF);
                buffer[idx++] = (byte)((divisor >> 8) & 0xFF);

                // Configure GPIO pins (lower byte)
                // Set initial states: SCK=0, MOSI=0, CS=1 (active low)
                // Direction: SCK=out, MOSI=out, MISO=in, CS=out
                // AD0=SCK, AD1=MOSI, AD2=MISO, AD3=CS
                byte initialState = 0x08;  // CS high (bit 3)
                byte direction = 0x0B;     // SCK, MOSI, CS as outputs (bits 0,1,3)
                
                buffer[idx++] = MPSSE_CMD_SET_DATA_BITS_LOW;
                buffer[idx++] = initialState;
                buffer[idx++] = direction;

                // Configure upper GPIO pins (if needed)
                buffer[idx++] = MPSSE_CMD_SET_DATA_BITS_HIGH;
                buffer[idx++] = 0x00;
                buffer[idx++] = 0x00;

                uint bytesWritten = 0;
                uint status = FT_Write(_ftHandle, buffer, (uint)idx, ref bytesWritten);
                
                if (status != FT_OK || bytesWritten != idx)
                {
                    StatusMessage = $"Failed to configure SPI. Error: {status}";
                    return false;
                }

                StatusMessage = $"SPI configured successfully at {_spiClockFrequency}Hz";
                return true;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Exception during SPI configuration: {ex.Message}";
                return false;
            }
        }

        private void CloseDevice()
        {
            if (_ftHandle != IntPtr.Zero)
            {
                try
                {
                    FT_Close(_ftHandle);
                    _ftHandle = IntPtr.Zero;
                    StatusMessage = "FTDI device closed";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Error closing FTDI device: {ex.Message}";
                }
            }
        }

        #endregion

        #region IDataStream Implementation

        public void Connect()
        {
            try
            {
                if (OpenDevice() && ConfigureMPSSE() && ConfigureSPI())
                {
                    IsConnected = true;
                    StatusMessage = "Connected";
                }
                else
                {
                    IsConnected = false;
                    CloseDevice();
                }
            }
            catch (Exception ex)
            {
                IsConnected = false;
                StatusMessage = $"Connection failed: {ex.Message}";
                CloseDevice();
            }
        }

        public void Disconnect()
        {
            StopStreaming();
            CloseDevice();
            IsConnected = false;
            StatusMessage = "Disconnected";
        }

        public void StartStreaming()
        {
            if (!_isConnected || _isStreaming)
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
            if (channel < 0 || channel >= _receivedData.Length)
                return 0;
            
            if (!_isConnected && !_isStreaming)
                return 0;
                
            try
            {
                return _receivedData[channel].CopyLatestTo(destination, n);
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
            int minBytesThreshold = 64;
            int maxReadSize = _readBuffer.Length;
            int consecutiveErrorCount = 0;
            const int maxConsecutiveErrors = 5;

            while (_isStreaming && _ftHandle != IntPtr.Zero)
            {
                try
                {
                    // Check how many bytes are available
                    uint bytesAvailable = 0;
                    uint status = FT_GetQueueStatus(_ftHandle, ref bytesAvailable);
                    
                    if (status != FT_OK)
                    {
                        HandleRuntimeDisconnection($"Queue status error: {status}");
                        break;
                    }

                    if (bytesAvailable >= minBytesThreshold)
                    {
                        uint bytesToRead = Math.Min(bytesAvailable, (uint)maxReadSize);
                        uint bytesRead = 0;
                        
                        status = FT_Read(_ftHandle, _readBuffer, bytesToRead, ref bytesRead);
                        
                        if (status != FT_OK)
                        {
                            consecutiveErrorCount++;
                            if (consecutiveErrorCount >= maxConsecutiveErrors)
                            {
                                HandleRuntimeDisconnection($"Read error: {status}");
                                break;
                            }
                            Thread.Sleep(10);
                            continue;
                        }

                        if (bytesRead > 0)
                        {
                            TotalBits += bytesRead * 8;
                            ProcessReceivedData(_readBuffer, (int)bytesRead);
                            consecutiveErrorCount = 0;
                        }
                    }
                    else
                    {
                        Thread.Sleep(1); // Short sleep when no data
                    }
                }
                catch (Exception ex)
                {
                    consecutiveErrorCount++;
                    if (consecutiveErrorCount >= maxConsecutiveErrors)
                    {
                        HandleRuntimeDisconnection($"Unexpected error: {ex.Message}");
                        break;
                    }
                    Thread.Sleep(50);
                }
            }
        }

        private void ProcessReceivedData(byte[] readBuffer, int bytesRead)
        {
            try
            {
                // Copy data to working buffer
                Array.Copy(readBuffer, 0, _workingBuffer, 0, bytesRead);
                
                // Parse the data using the configured parser
                ParsedData parsedData = _parser.ParseData(_workingBuffer.AsSpan(0, bytesRead));
                
                if (parsedData.Data != null)
                {
                    AddDataToRingBuffers(parsedData.Data);
                }
            }
            catch (Exception ex)
            {
                // Log parsing error but continue processing
                System.Diagnostics.Debug.WriteLine($"FTDI data parsing error: {ex.Message}");
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
                if (finalData[channel] != null)
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
                CloseDevice();
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

        private void OnUpDownSamplingPropertyChanged(object sender, PropertyChangedEventArgs e)
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

        public event PropertyChangedEventHandler PropertyChanged;

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

            CloseDevice();

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

        #region DLL Verification Methods

        /// <summary>
        /// Result of FTDI DLL verification
        /// </summary>
        public class FTDIVerificationResult
        {
            public bool IsAvailable { get; set; }
            public string Message { get; set; } = string.Empty;
            public uint DeviceCount { get; set; }
            public string DllPath { get; set; } = string.Empty;
            public string Architecture { get; set; } = string.Empty;
        }

        /// <summary>
        /// Verifies that the FTDI x64 DLL is available and can be loaded
        /// </summary>
        /// <returns>Verification result with detailed information</returns>
        public static FTDIVerificationResult VerifyFTDIDLL()
        {
            var result = new FTDIVerificationResult
            {
                Architecture = Environment.Is64BitProcess ? "x64" : "x86"
            };

            try
            {
                // Check if DLL file exists in application directory
                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                string dllPath = Path.Combine(appDir, FtdiDllName);
                
                if (!File.Exists(dllPath))
                {
                    // Also check system directory as fallback
                    string systemPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), FtdiDllName);
                    if (!File.Exists(systemPath))
                    {
                        result.IsAvailable = false;
                        result.Message = $"{FtdiDllName} not found in application directory ({appDir}) or system directory";
                        return result;
                    }
                    result.DllPath = systemPath;
                }
                else
                {
                    result.DllPath = dllPath;
                }

                // Test DLL loading by calling a simple function
                uint numDevices = 0;
                uint status = FT_CreateDeviceInfoList(ref numDevices);
                
                result.IsAvailable = true;
                result.DeviceCount = numDevices;
                result.Message = $"Success. Status={status} ({GetFTDIStatusDescription(status)}), Devices={numDevices}, DLL={result.DllPath}";
                
                return result;
            }
            catch (DllNotFoundException ex)
            {
                result.IsAvailable = false;
                result.Message = $"DLL not found: {ex.Message}. Expected: {FtdiDllName}";
                return result;
            }
            catch (EntryPointNotFoundException ex)
            {
                result.IsAvailable = false;
                result.Message = $"DLL function not found: {ex.Message}. Wrong DLL version?";
                return result;
            }
            catch (BadImageFormatException ex)
            {
                result.IsAvailable = false;
                result.Message = $"Architecture mismatch: {ex.Message}. Process is {result.Architecture}, ensure {FtdiDllName} matches";
                return result;
            }
            catch (Exception ex)
            {
                result.IsAvailable = false;
                result.Message = $"Unexpected error: {ex.Message}";
                return result;
            }
        }

        /// <summary>
        /// Gets detailed information about the loaded FTDI DLL
        /// </summary>
        /// <returns>Dictionary with DLL information</returns>
        public static Dictionary<string, string> GetFTDIDLLInfo()
        {
            var info = new Dictionary<string, string>();
            
            try
            {
                var verification = VerifyFTDIDLL();
                info["DLL Available"] = verification.IsAvailable.ToString();
                info["Architecture"] = verification.Architecture;
                info["Expected DLL"] = FtdiDllName;
                info["Verification Message"] = verification.Message;
                
                if (verification.IsAvailable)
                {
                    info["Device Count"] = verification.DeviceCount.ToString();
                    info["DLL Path"] = verification.DllPath;
                    
                    // Get file information if available
                    if (File.Exists(verification.DllPath))
                    {
                        var fileInfo = new FileInfo(verification.DllPath);
                        info["File Size"] = $"{fileInfo.Length:N0} bytes";
                        info["File Date"] = fileInfo.LastWriteTime.ToString();
                        
                        try
                        {
                            var versionInfo = FileVersionInfo.GetVersionInfo(verification.DllPath);
                            info["File Version"] = versionInfo.FileVersion ?? "Unknown";
                            info["Product Version"] = versionInfo.ProductVersion ?? "Unknown";
                            info["File Description"] = versionInfo.FileDescription ?? "Unknown";
                            info["Company"] = versionInfo.CompanyName ?? "Unknown";
                        }
                        catch (Exception ex)
                        {
                            info["Version Info Error"] = ex.Message;
                        }
                    }
                }

                // Get the loaded module information if DLL is loaded
                try
                {
                    var currentProcess = Process.GetCurrentProcess();
                    var ftdiModule = currentProcess.Modules.Cast<ProcessModule>()
                        .FirstOrDefault(m => m.ModuleName.ToLowerInvariant().Contains("ftd2xx"));
                    
                    if (ftdiModule != null)
                    {
                        info["Module Loaded"] = "Yes";
                        info["Module Path"] = ftdiModule.FileName;
                        info["Base Address"] = $"0x{ftdiModule.BaseAddress.ToInt64():X}";
                        info["Module Size"] = $"{ftdiModule.ModuleMemorySize:N0} bytes";
                    }
                    else
                    {
                        info["Module Loaded"] = "No";
                    }
                }
                catch (Exception ex)
                {
                    info["Module Info Error"] = ex.Message;
                }
            }
            catch (Exception ex)
            {
                info["Error"] = ex.Message;
            }
            
            return info;
        }

        /// <summary>
        /// Gets architecture and DLL deployment information
        /// </summary>
        public static Dictionary<string, string> GetArchitectureInfo()
        {
            var info = new Dictionary<string, string>();
            
            info["Process Architecture"] = Environment.Is64BitProcess ? "x64" : "x86";
            info["OS Architecture"] = Environment.Is64BitOperatingSystem ? "x64" : "x86";
            info["Expected FTDI DLL"] = FtdiDllName;
            info[".NET Runtime"] = RuntimeInformation.RuntimeIdentifier;
            info["Framework Description"] = RuntimeInformation.FrameworkDescription;
            
            // Check what FTDI DLLs exist in the application directory
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            bool has32BitDll = File.Exists(Path.Combine(appDir, "ftd2xx.dll"));
            bool has64BitDll = File.Exists(Path.Combine(appDir, "ftd2xx64.dll"));
            
            info["ftd2xx.dll Present"] = has32BitDll.ToString();
            info["ftd2xx64.dll Present"] = has64BitDll.ToString();
            
            if (has32BitDll)
            {
                try
                {
                    var fileInfo = new FileInfo(Path.Combine(appDir, "ftd2xx.dll"));
                    info["ftd2xx.dll Size"] = $"{fileInfo.Length:N0} bytes";
                    info["ftd2xx.dll Date"] = fileInfo.LastWriteTime.ToString();
                }
                catch { }
            }
            
            if (has64BitDll)
            {
                try
                {
                    var fileInfo = new FileInfo(Path.Combine(appDir, "ftd2xx64.dll"));
                    info["ftd2xx64.dll Size"] = $"{fileInfo.Length:N0} bytes";
                    info["ftd2xx64.dll Date"] = fileInfo.LastWriteTime.ToString();
                }
                catch { }
            }
            
            // Deployment recommendations
            if (!has64BitDll && Environment.Is64BitProcess)
            {
                info["Deployment Issue"] = "Missing ftd2xx64.dll for x64 process";
                info["Recommendation"] = "Copy ftd2xx64.dll to application directory or install FTDI drivers";
            }
            else if (has32BitDll && !has64BitDll && Environment.Is64BitProcess)
            {
                info["Architecture Warning"] = "Only 32-bit DLL found, but process is 64-bit";
            }
            
            return info;
        }

        /// <summary>
        /// Gets a human-readable description of FTDI status codes
        /// </summary>
        private static string GetFTDIStatusDescription(uint status)
        {
            return status switch
            {
                0 => "FT_OK - Success",
                1 => "FT_INVALID_HANDLE - Invalid handle",
                2 => "FT_DEVICE_NOT_FOUND - Device not found",
                3 => "FT_DEVICE_NOT_OPENED - Device not opened",
                4 => "FT_IO_ERROR - I/O error",
                5 => "FT_INSUFFICIENT_RESOURCES - Insufficient resources",
                6 => "FT_INVALID_PARAMETER - Invalid parameter",
                7 => "FT_INVALID_BAUD_RATE - Invalid baud rate",
                8 => "FT_DEVICE_NOT_OPENED_FOR_ERASE - Device not opened for erase",
                9 => "FT_DEVICE_NOT_OPENED_FOR_WRITE - Device not opened for write",
                10 => "FT_FAILED_TO_WRITE_DEVICE - Failed to write device",
                11 => "FT_EEPROM_READ_FAILED - EEPROM read failed",
                12 => "FT_EEPROM_WRITE_FAILED - EEPROM write failed",
                13 => "FT_EEPROM_ERASE_FAILED - EEPROM erase failed",
                14 => "FT_EEPROM_NOT_PRESENT - EEPROM not present",
                15 => "FT_EEPROM_NOT_PROGRAMMED - EEPROM not programmed",
                16 => "FT_INVALID_ARGS - Invalid arguments",
                17 => "FT_NOT_SUPPORTED - Not supported",
                18 => "FT_OTHER_ERROR - Other error",
                19 => "FT_DEVICE_LIST_NOT_READY - Device list not ready",
                _ => $"Unknown status code: {status}"
            };
        }

        #endregion

        #region ComboBox Display Methods

        /// <summary>
        /// Creates a user-friendly display name for FTDI devices
        /// </summary>
        /// <param name="deviceInfo">Device information structure</param>
        /// <param name="index">Device index for fallback naming</param>
        /// <returns>Formatted display string</returns>
        public static string CreateDeviceDisplayName(FT_DEVICE_LIST_INFO_NODE deviceInfo, int index)
        {
            string serialNumber = deviceInfo.SerialNumber ?? $"Device{index}";
            string description = deviceInfo.Description ?? "FTDI Device";
            
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
            string serialNumber = ExtractSerialNumber(deviceString);
            return string.IsNullOrEmpty(serialNumber) ? "Unknown" : serialNumber;
        }

        #endregion

        #region Test Methods

        /// <summary>
        /// Test method to demonstrate FTDI device enumeration functionality
        /// This method is for development/testing purposes
        /// </summary>
        public static void TestDeviceEnumeration()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("=== FTDI Device Enumeration Test ===");
                
                // Test DLL verification
                var dllStatus = VerifyFTDIDLL();
                System.Diagnostics.Debug.WriteLine($"DLL Status: {dllStatus.IsAvailable} - {dllStatus.Message}");
                
                if (!dllStatus.IsAvailable)
                {
                    System.Diagnostics.Debug.WriteLine("Skipping device enumeration - DLL not available");
                    return;
                }
                
                // Test device listing
                string[] devices = GetAvailableDevices();
                System.Diagnostics.Debug.WriteLine($"Found {devices.Length} FTDI devices:");
                
                for (int i = 0; i < devices.Length; i++)
                {
                    System.Diagnostics.Debug.WriteLine($"  [{i}] {devices[i]}");
                    
                    // Test serial number extraction
                    string serialNumber = ExtractSerialNumber(devices[i]);
                    System.Diagnostics.Debug.WriteLine($"      Serial: {serialNumber ?? "N/A"}");
                    
                    // Test short name generation
                    string shortName = GetDeviceShortName(devices[i]);
                    System.Diagnostics.Debug.WriteLine($"      Short: {shortName}");
                }
                
                // Test detailed device info
                var deviceDetails = GetAvailableDeviceDetails();
                System.Diagnostics.Debug.WriteLine($"\nDetailed device information ({deviceDetails.Length} devices):");
                
                for (int i = 0; i < deviceDetails.Length; i++)
                {
                    var detail = deviceDetails[i];
                    System.Diagnostics.Debug.WriteLine($"  Device {i}:");
                    System.Diagnostics.Debug.WriteLine($"    Serial: {detail.SerialNumber}");
                    System.Diagnostics.Debug.WriteLine($"    Description: {detail.Description}");
                    System.Diagnostics.Debug.WriteLine($"    Type: {detail.Type}");
                    System.Diagnostics.Debug.WriteLine($"    ID: 0x{detail.ID:X8}");
                    System.Diagnostics.Debug.WriteLine($"    Location: 0x{detail.LocId:X8}");
                    System.Diagnostics.Debug.WriteLine($"    Flags: 0x{detail.Flags:X}");
                }
                
                System.Diagnostics.Debug.WriteLine("=== End FTDI Device Enumeration Test ===");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FTDI Test Exception: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        #endregion
    }
}