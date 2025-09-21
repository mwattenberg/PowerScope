using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace PowerScope.Model
{
    /// <summary>
    /// Exception thrown when a USB device is not found on the system
    /// </summary>
    public class UsbDeviceNotFoundException : Exception
    {
        public string DeviceGuid { get; }

        public UsbDeviceNotFoundException(string deviceGuid)
            : base($"USB device with GUID '{deviceGuid}' was not found on this system")
        {
            DeviceGuid = deviceGuid;
        }

        public UsbDeviceNotFoundException(string deviceGuid, Exception innerException)
            : base($"USB device with GUID '{deviceGuid}' was not found on this system", innerException)
        {
            DeviceGuid = deviceGuid;
        }
    }

    /// <summary>
    /// Exception thrown when a USB device is already in use by another application
    /// </summary>
    public class UsbDeviceInUseException : Exception
    {
        public string DeviceGuid { get; }

        public UsbDeviceInUseException(string deviceGuid)
            : base($"USB device with GUID '{deviceGuid}' is already in use by another application")
        {
            DeviceGuid = deviceGuid;
        }

        public UsbDeviceInUseException(string deviceGuid, Exception innerException)
            : base($"USB device with GUID '{deviceGuid}' is already in use by another application", innerException)
        {
            DeviceGuid = deviceGuid;
        }
    }

    public class USBDataStream : IDataStream, IChannelConfigurable
    {
        // Constants from your example
        private const string DeviceInterfaceGuid = "{8D2C9D52-5C6B-4F0B-9F1B-3EBE8C4F9A61}";
        private const byte PipeIn = 0x81;
        private const byte REQ_START = 0xA0;
        private const byte REQ_STOP = 0xA1;

        // USB handles
        private SafeFileHandle _deviceHandle;
        private IntPtr _winUsbHandle = IntPtr.Zero;
        private readonly Guid _deviceGuid;

        // Data processing
        private byte[] _readBuffer;
        private byte[] _holdBuffer;
        private int _holdBufferLength;
        private Thread _readUsbThread;
        private bool _disposed = false;
        private bool _isConnected;
        private bool _isStreaming;
        private string _statusMessage;

        // Sample rate calculation for USB data stream
        private long _lastSampleCount = 0;
        private DateTime _lastSampleTime = DateTime.Now;
        private double _calculatedSampleRate = 0.0;
        private readonly object _sampleRateCalculationLock = new object();

        public UsbSourceSetting SourceSetting { get; init; }
        public long TotalSamples { get; private set; }
        public long TotalBits { get; private set; }
        private RingBuffer<double>[] ReceivedData { get; set; }
        public DataParser Parser { get; init; }
        public int UsbUpdateRateHz { get; set; } = 1000;

        // Channel-specific processing
        private ChannelSettings[] _channelSettings;
        private IDigitalFilter[] _channelFilters;
        private readonly object _channelConfigLock = new object();

        // INotifyPropertyChanged implementation
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public int ChannelCount
        {
            get
            {
                return Parser.NumberOfChannels;
            }
        }

        /// <summary>
        /// Sample rate of the USB data stream in samples per second (Hz)
        /// Calculated dynamically based on received data rate
        /// </summary>
        public double SampleRate 
        { 
            get 
            { 
                lock (_sampleRateCalculationLock)
                {
                    return _calculatedSampleRate;
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

        public string StreamType
        {
            get { return "USB"; }
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

        public USBDataStream(UsbSourceSetting source, DataParser dataParser)
        {
            SourceSetting = source;
            Parser = dataParser;
            _deviceGuid = new Guid(source.DeviceGuid);

            int ringBufferSize = Math.Max(500000, 1000000); // Large buffer for USB throughput
            ReceivedData = new RingBuffer<double>[dataParser.NumberOfChannels];

            for (int i = 0; i < dataParser.NumberOfChannels; i++)
            {
                ReceivedData[i] = new RingBuffer<double>(ringBufferSize);
            }

            // Initialize channel processing arrays
            _channelSettings = new ChannelSettings[dataParser.NumberOfChannels];
            _channelFilters = new IDigitalFilter[dataParser.NumberOfChannels];

            // USB transfer buffers - larger for better throughput
            _readBuffer = new byte[64 * 1024]; // 64KB read buffer
            _holdBuffer = new byte[64 * 1024]; // 64KB hold buffer for frame assembly
            _holdBufferLength = 0;

            _isConnected = false;
            _isStreaming = false;
            StatusMessage = "Disconnected";
        }

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
            double processed = settings.Gain * (rawSample + settings.Offset);

            // Apply filter if configured
            if (filter != null)
            {
                processed = filter.Filter(processed);
            }

            return processed;
        }

        #endregion

        public void Connect()
        {
            try
            {
                var (dev, winusb) = OpenByGuid(_deviceGuid);
                _deviceHandle = dev;
                _winUsbHandle = winusb;

                IsConnected = true;
                StatusMessage = "Connected";
            }
            catch (Exception ex) when (ex.Message.Contains("Device not found"))
            {
                StatusMessage = "USB device not found";
                IsConnected = false;
                CleanupHandles();
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                StatusMessage = $"USB device access error: {ex.Message}";
                IsConnected = false;
                CleanupHandles();
            }
            catch (Exception ex)
            {
                StatusMessage = $"USB connection error: {ex.Message}";
                IsConnected = false;
                CleanupHandles();
            }
        }

        public void Disconnect()
        {
            StopStreaming();
            try
            {
                if (_winUsbHandle != IntPtr.Zero)
                {
                    SendStop(_winUsbHandle);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Warning during USB stop: {ex.Message}";
            }
            finally
            {
                CleanupHandles();
                IsConnected = false;
                if (_statusMessage != "Disconnected")
                {
                    StatusMessage = "Disconnected";
                }
            }
        }

        private void CleanupHandles()
        {
            if (_winUsbHandle != IntPtr.Zero)
            {
                WinUsb_Free(_winUsbHandle);
                _winUsbHandle = IntPtr.Zero;
            }

            if (_deviceHandle != null && !_deviceHandle.IsInvalid)
            {
                _deviceHandle.Dispose();
                _deviceHandle = null;
            }
        }

        public void StartStreaming()
        {
            if (!_isConnected || _isStreaming)
                return;

            try
            {
                if (_winUsbHandle == IntPtr.Zero)
                {
                    StatusMessage = "Cannot start streaming: USB not connected";
                    IsConnected = false;
                    return;
                }

                // Send start command to device
                SendStart(_winUsbHandle);

                IsStreaming = true;

                // Reset filters when starting streaming
                ResetChannelFilters();

                if (_readUsbThread != null && _readUsbThread.IsAlive)
                {
                    IsStreaming = false;
                    _readUsbThread.Join(1000);
                }

                _readUsbThread = new Thread(ReadUsbData) { IsBackground = true };
                _readUsbThread.Start();

                StatusMessage = "Streaming";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to start streaming: {ex.Message}";
                IsStreaming = false;
            }
        }

        public void StopStreaming()
        {
            if (!_isStreaming)
                return;

            IsStreaming = false;

            try
            {
                if (_winUsbHandle != IntPtr.Zero)
                {
                    SendStop(_winUsbHandle);
                }
            }
            catch (Exception)
            {
                // Ignore errors when stopping
            }

            if (_readUsbThread != null && _readUsbThread.IsAlive)
                _readUsbThread.Join(1000);

            // Only update status if we're still connected
            if (_isConnected)
            {
                StatusMessage = "Stopped";
            }
        }

        public int CopyLatestTo(int channel, double[] destination, int n)
        {
            if (channel < 0)
                return 0;
            if (channel >= ReceivedData.Length)
                return 0;

            // If we're not connected or streaming, return 0 (no data)
            if (!_isConnected && !_isStreaming)
                return 0;

            try
            {
                return ReceivedData[channel].CopyLatestTo(destination, n);
            }
            catch (Exception)
            {
                // If there's an error accessing the buffer, return 0
                return 0;
            }
        }

        private void clearData()
        {
            if (ReceivedData != null)
            {
                foreach (RingBuffer<double> ringBuffer in ReceivedData)
                {
                    if (ringBuffer != null)
                        ringBuffer.Clear();
                }
            }
            TotalSamples = 0;
            TotalBits = 0;

            // Reset filters when clearing data
            ResetChannelFilters();
        }

        private void ReadUsbData()
        {
            int consecutiveErrorCount = 0;
            const int maxConsecutiveErrors = 5;

            while (_isStreaming && _winUsbHandle != IntPtr.Zero)
            {
                try
                {
                    int bytesRead;
                    if (!WinUsb_ReadPipe(_winUsbHandle, PipeIn, _readBuffer, _readBuffer.Length, out bytesRead, IntPtr.Zero))
                    {
                        var error = Marshal.GetLastWin32Error();
                        if (error == 995) // ERROR_OPERATION_ABORTED - normal when stopping
                            break;

                        consecutiveErrorCount++;
                        if (consecutiveErrorCount >= maxConsecutiveErrors)
                        {
                            HandleRuntimeDisconnection($"USB read error: {new System.ComponentModel.Win32Exception(error).Message}");
                            break;
                        }
                        Thread.Sleep(10);
                        continue;
                    }

                    if (bytesRead > 0)
                    {
                        TotalBits += bytesRead * 8;
                        ProcessReceivedUsbData(_readBuffer, bytesRead);
                        consecutiveErrorCount = 0;
                    }
                    else
                    {
                        Thread.Sleep(Math.Max(1, 1000 / UsbUpdateRateHz));
                    }
                }
                catch (System.ComponentModel.Win32Exception ex)
                {
                    consecutiveErrorCount++;
                    if (consecutiveErrorCount >= maxConsecutiveErrors)
                    {
                        HandleRuntimeDisconnection($"USB communication error: {ex.Message}");
                        break;
                    }
                    Thread.Sleep(50);
                }
                catch (Exception ex)
                {
                    consecutiveErrorCount++;
                    if (consecutiveErrorCount >= maxConsecutiveErrors)
                    {
                        HandleRuntimeDisconnection($"Unexpected USB error: {ex.Message}");
                        break;
                    }
                    Thread.Sleep(50);
                }
            }
        }

        private void HandleRuntimeDisconnection(string reason)
        {
            try
            {
                StatusMessage = $"Disconnected: {reason}";
                IsStreaming = false;
                CleanupHandles();
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

        private void ProcessReceivedUsbData(byte[] readBuffer, int bytesRead)
        {
            // Append new data to hold buffer
            if (_holdBufferLength + bytesRead > _holdBuffer.Length)
            {
                // Hold buffer overflow - this shouldn't happen with proper sizing
                _holdBufferLength = 0;
                return;
            }

            Array.Copy(readBuffer, 0, _holdBuffer, _holdBufferLength, bytesRead);
            _holdBufferLength += bytesRead;

            // Process complete 24-byte frames (12 channels * 2 bytes each)
            const int frameSize = 24;
            int completeFrames = _holdBufferLength / frameSize;

            if (completeFrames > 0)
            {
                double[][] frameData = new double[12][];
                for (int ch = 0; ch < 12; ch++)
                {
                    frameData[ch] = new double[completeFrames];
                }

                int offset = 0;
                for (int frame = 0; frame < completeFrames; frame++)
                {
                    for (int channel = 0; channel < 12; channel++)
                    {
                        // Convert little-endian 16-bit values to doubles
                        ushort rawValue = (ushort)(_holdBuffer[offset] | (_holdBuffer[offset + 1] << 8));
                        frameData[channel][frame] = rawValue;
                        offset += 2;
                    }
                }

                // Process the frame data through the parser
                try
                {
                    AddDataToRingBuffers(frameData);
                }
                catch (Exception)
                {
                    // Error processing data, continue
                }

                // Move remaining bytes to start of buffer
                int remainingBytes = _holdBufferLength - (completeFrames * frameSize);
                if (remainingBytes > 0)
                {
                    Array.Copy(_holdBuffer, completeFrames * frameSize, _holdBuffer, 0, remainingBytes);
                }
                _holdBufferLength = remainingBytes;
            }
        }

        private void AddDataToRingBuffers(double[][] parsedData)
        {
            int channelsToProcess = Math.Min(parsedData.Length, Parser.NumberOfChannels);

            for (int channel = 0; channel < channelsToProcess; channel++)
            {
                if (parsedData[channel] != null && parsedData[channel].Length > 0)
                {
                    // Apply channel processing (gain, offset, filtering) to each sample
                    double[] processedSamples = new double[parsedData[channel].Length];
                    for (int sample = 0; sample < parsedData[channel].Length; sample++)
                    {
                        processedSamples[sample] = ApplyChannelProcessing(channel, parsedData[channel][sample]);
                    }

                    // Add processed data to ring buffer
                    ReceivedData[channel].AddRange(processedSamples);
                }
            }

            if (channelsToProcess > 0 && parsedData[0] != null)
            {
                int newSamples = parsedData[0].Length;
                TotalSamples += newSamples;
                
                // Update sample rate calculation
                UpdateSampleRateCalculation(newSamples);
            }
        }

        /// <summary>
        /// Updates the calculated sample rate based on incoming data
        /// Uses a moving average approach similar to StreamInfoPanel
        /// </summary>
        private void UpdateSampleRateCalculation(int newSamples)
        {
            lock (_sampleRateCalculationLock)
            {
                DateTime currentTime = DateTime.Now;
                double timeDeltaSeconds = (currentTime - _lastSampleTime).TotalSeconds;
                
                // Only update if we have at least 100ms of data to avoid noise
                if (timeDeltaSeconds >= 0.1)
                {
                    double instantaneousSampleRate = newSamples / timeDeltaSeconds;
                    
                    // Use exponential moving average to smooth the sample rate calculation
                    if (_calculatedSampleRate == 0.0)
                    {
                        // First calculation
                        _calculatedSampleRate = instantaneousSampleRate;
                    }
                    else
                    {
                        // Exponential moving average with alpha = 0.1 for smoothing
                        double alpha = 0.1;
                        _calculatedSampleRate = alpha * instantaneousSampleRate + (1 - alpha) * _calculatedSampleRate;
                    }
                    
                    _lastSampleTime = currentTime;
                    
                    // Notify propertyChanged for real-time updates
                    OnPropertyChanged(nameof(SampleRate));
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            if (_isStreaming)
                StopStreaming();

            CleanupHandles();

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

        void IDataStream.clearData()
        {
            clearData();
        }

        #region WinUSB P/Invoke - Based on your example code

        [DllImport("winusb.dll", SetLastError = true)]
        private static extern bool WinUsb_Initialize(SafeFileHandle deviceHandle, out IntPtr interfaceHandle);

        [DllImport("winusb.dll", SetLastError = true)]
        private static extern bool WinUsb_Free(IntPtr interfaceHandle);

        [DllImport("winusb.dll", SetLastError = true)]
        private static extern bool WinUsb_ReadPipe(IntPtr handle, byte pipeId, byte[] buffer, int bufferLength, out int lengthTransferred, IntPtr overlapped);

        [DllImport("winusb.dll", SetLastError = true)]
        private static extern bool WinUsb_ControlTransfer(IntPtr handle, WINUSB_SETUP_PACKET setupPacket, byte[] buffer, int bufferLength, out int lengthTransferred, IntPtr overlapped);

        [DllImport("winusb.dll", SetLastError = true)]
        private static extern bool WinUsb_SetPipePolicy(IntPtr handle, byte pipeId, uint policyType, int valueLength, ref uint value);

        [StructLayout(LayoutKind.Sequential)]
        private struct WINUSB_SETUP_PACKET
        {
            public byte RequestType;
            public byte Request;
            public ushort Value;
            public ushort Index;
            public ushort Length;
        }

        // SetupAPI for device enumeration
        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid ClassGuid, IntPtr Enumerator, IntPtr hwndParent, uint Flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr DeviceInfoSet, IntPtr DeviceInfoData, ref Guid InterfaceClassGuid, uint MemberIndex, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr DeviceInfoSet, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData, IntPtr DeviceInterfaceDetailData, int DeviceInterfaceDetailDataSize, out int RequiredSize, ref SP_DEVINFO_DATA DeviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern SafeFileHandle CreateFile(string lpFileName, int dwDesiredAccess, int dwShareMode, IntPtr lpSecurityAttributes, int dwCreationDisposition, int dwFlagsAndAttributes, IntPtr hTemplateFile);

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVINFO_DATA
        {
            public int cbSize;
            public Guid ClassGuid;
            public int DevInst;
            public IntPtr Reserved;
        }

        private const uint DIGCF_PRESENT = 0x2;
        private const uint DIGCF_DEVICEINTERFACE = 0x10;
        private const int GENERIC_WRITE = unchecked((int)0x40000000);
        private const int GENERIC_READ = unchecked((int)0x80000000);
        private const int FILE_SHARE_READ = 1;
        private const int FILE_SHARE_WRITE = 2;
        private const int OPEN_EXISTING = 3;
        private const int FILE_ATTRIBUTE_NORMAL = 0x80;
        private const uint POLICY_ALLOW_PARTIAL_READS = 0x00000002;
        private const uint POLICY_IGNORE_SHORT_PACKETS = 0x00000003;
        private const uint POLICY_RAW_IO = 0x00000001;

        private static string FindDevicePath(Guid guid)
        {
            IntPtr h = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (h == IntPtr.Zero || h.ToInt64() == -1) return null;

            try
            {
                SP_DEVICE_INTERFACE_DATA did = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
                for (uint i = 0; SetupDiEnumDeviceInterfaces(h, IntPtr.Zero, ref guid, i, ref did); i++)
                {
                    int required;
                    SP_DEVINFO_DATA devInfo = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };
                    SetupDiGetDeviceInterfaceDetail(h, ref did, IntPtr.Zero, 0, out required, ref devInfo);
                    IntPtr detail = Marshal.AllocHGlobal(required);
                    try
                    {
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6); // cbSize
                        if (SetupDiGetDeviceInterfaceDetail(h, ref did, detail, required, out required, ref devInfo))
                        {
                            string path = Marshal.PtrToStringUni(IntPtr.Add(detail, IntPtr.Size == 8 ? 8 : 4));
                            return path;
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(detail);
                    }
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(h);
            }
            return null;
        }

        private static (SafeFileHandle dev, IntPtr winusb) OpenByGuid(Guid guid)
        {
            var path = FindDevicePath(guid);
            if (path == null)
                throw new Exception("Device not found");

            var dev = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);
            if (dev.IsInvalid)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

            if (!WinUsb_Initialize(dev, out var h))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

            // Set recommended pipe policies
            uint one = 1;
            WinUsb_SetPipePolicy(h, PipeIn, POLICY_ALLOW_PARTIAL_READS, sizeof(uint), ref one);
            WinUsb_SetPipePolicy(h, PipeIn, POLICY_IGNORE_SHORT_PACKETS, sizeof(uint), ref one);
            WinUsb_SetPipePolicy(h, PipeIn, POLICY_RAW_IO, sizeof(uint), ref one);

            return (dev, h);
        }

        private static void SendStart(IntPtr h)
        {
            var setup = new WINUSB_SETUP_PACKET
            {
                RequestType = 0x40, // Host-to-device | Vendor | Recipient: Device
                Request = REQ_START,
                Value = 0,
                Index = 0,
                Length = 0
            };
            int transferred;
            if (!WinUsb_ControlTransfer(h, setup, null, 0, out transferred, IntPtr.Zero))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        private static void SendStop(IntPtr h)
        {
            var setup = new WINUSB_SETUP_PACKET
            {
                RequestType = 0x40,
                Request = REQ_STOP,
                Value = 0,
                Index = 0,
                Length = 0
            };
            int transferred;
            WinUsb_ControlTransfer(h, setup, null, 0, out transferred, IntPtr.Zero);
        }

        #endregion
    }

    /// <summary>
    /// USB-specific source settings
    /// </summary>
    public class UsbSourceSetting
    {
        public string DeviceGuid { get; init; }
        public string DeviceName { get; init; }
        public int ExpectedChannels { get; init; }

        public UsbSourceSetting(string deviceGuid, string deviceName = "USB Device", int expectedChannels = 12)
        {
            DeviceGuid = deviceGuid;
            DeviceName = deviceName;
            ExpectedChannels = expectedChannels;
        }
    }
}