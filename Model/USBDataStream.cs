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

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SP_DEVICE_INTERFACE_DETAIL_DATA
        {
            public int cbSize;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string DevicePath;
        }

        public UsbSourceSetting SourceSetting { get; init; }
        public long TotalSamples { get; private set; }
        public long TotalBits { get; private set; }
        public long TotalFrames { get; private set; }
        public long TotalReadErrors { get; private set; }
        public int LastWin32Error { get; private set; }
        private RingBuffer<double>[] ReceivedData { get; set; }
        public DataParser Parser { get; init; }
        public int UsbUpdateRateHz { get; set; } = 1000;

        // Channel-specific processing
        private ChannelSettings[] _channelSettings;
        private IDigitalFilter[] _channelFilters;
        private readonly object _channelConfigLock = new object();

        // INotifyPropertyChanged implementation
        public event PropertyChangedEventHandler PropertyChanged;
    
        /// <summary>
        /// Raised when the data stream is being disposed
        /// Allows dependent virtual streams to clean up automatically
        /// </summary>
        public event EventHandler Disposing;

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

            // Apply offset and gain
            double processed = (rawSample + settings.Offset) * settings.Gain;

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

                // Send start command to device.
                // This is a best-effort vendor control transfer — if the firmware does not
                // implement the request the device will STALL it, which is not fatal.
                // Data streaming begins regardless via the bulk IN endpoint.
                try
                {
                    SendStart(_winUsbHandle);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"USB SendStart warning (non-fatal): {ex.Message}");
                }

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
                        int error = Marshal.GetLastWin32Error();
                        LastWin32Error = error;

                        if (error == 995) // ERROR_OPERATION_ABORTED - normal when stopping
                            break;

                        TotalReadErrors++;
                        consecutiveErrorCount++;

                        StatusMessage = $"Read error Win32={error} (total errors: {TotalReadErrors})";

                        if (consecutiveErrorCount >= maxConsecutiveErrors)
                        {
                            HandleRuntimeDisconnection($"USB read error Win32={error}: {new System.ComponentModel.Win32Exception(error).Message}");
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

                        // Update status with live byte count so the user can see data arriving
                        long totalBytes = TotalBits / 8;
                        if (totalBytes % (64 * 1024) < bytesRead) // update roughly every 64KB
                            StatusMessage = $"Streaming — {totalBytes / 1024} KB  {TotalFrames} frames";
                    }
                    else
                    {
                        Thread.Sleep(Math.Max(1, 1000 / UsbUpdateRateHz));
                    }
                }
                catch (System.ComponentModel.Win32Exception ex)
                {
                    TotalReadErrors++;
                    LastWin32Error = ex.NativeErrorCode;
                    consecutiveErrorCount++;
                    StatusMessage = $"Win32 error {ex.NativeErrorCode}: {ex.Message}";
                    if (consecutiveErrorCount >= maxConsecutiveErrors)
                    {
                        HandleRuntimeDisconnection($"USB communication error: {ex.Message}");
                        break;
                    }
                    Thread.Sleep(50);
                }
                catch (Exception ex)
                {
                    TotalReadErrors++;
                    consecutiveErrorCount++;
                    StatusMessage = $"Error: {ex.Message}";
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

           // Dynamically parse binary frames with a custom starting sequence or continuous layout
           // Determine our single-channel data size based on the parser format
           int bytesPerChannel = Parser.Format switch
           {
               DataParser.BinaryFormat.int16_t => 2,
               DataParser.BinaryFormat.uint16_t => 2,
               DataParser.BinaryFormat.int32_t => 4,
               DataParser.BinaryFormat.uint32_t => 4,
               DataParser.BinaryFormat.float_t => 4,
               _ => 2
           };

           int payloadSize = Parser.NumberOfChannels * bytesPerChannel;
           byte[] frameStart = Parser.FrameStart;
           bool usesFraming = frameStart != null && frameStart.Length > 0;
           int frameSize = usesFraming ? (frameStart.Length + payloadSize) : payloadSize;

           if (usesFraming)
           {
               // Parse packets aligning with our starting sequence (e.g., 0xAA, 0xAA)
               List<int> frameIndices = new List<int>();
               for (int i = 0; i <= _holdBufferLength - frameSize; i++)
               {
                   bool isMatch = true;
                   for (int k = 0; k < frameStart.Length; k++)
                   {
                       if (_holdBuffer[i + k] != frameStart[k])
                       {
                           isMatch = false;
                           break;
                       }
                   }
                   if (isMatch)
                   {
                       frameIndices.Add(i);
                       i += frameSize - 1; // skip past this frame
                   }
               }

               int completeFrames = frameIndices.Count;
               if (completeFrames > 0)
               {
                   double[][] frameData = new double[Parser.NumberOfChannels][];
                   for (int ch = 0; ch < Parser.NumberOfChannels; ch++)
                   {
                       frameData[ch] = new double[completeFrames];
                   }

                   for (int frame = 0; frame < completeFrames; frame++)
                    {
                       int dataOffset = frameIndices[frame] + frameStart.Length;
                       for (int channel = 0; channel < Parser.NumberOfChannels; channel++)
                       {
                           int offset = dataOffset + (channel * bytesPerChannel);
                           frameData[channel][frame] = ReadBinaryValue(_holdBuffer, offset, Parser.Format);
                       }
                   }

                   try
                   {
                       AddDataToRingBuffers(frameData);
                   }
                   catch (Exception)
                   {
                       // Ignore processing issues and keep streaming
                   }

                   // Move remaining data after the last processed frame to the start
                   int lastFrameEnd = frameIndices[completeFrames - 1] + frameSize;
                   int remainingBytes = _holdBufferLength - lastFrameEnd;
                   if (remainingBytes > 0)
                   {
                       Array.Copy(_holdBuffer, lastFrameEnd, _holdBuffer, 0, remainingBytes);
                   }
                   _holdBufferLength = remainingBytes;
               }
               else if (_holdBufferLength > 1024)
               {
                   // If we have a lot of bytes but no sync sequences found, flush buffer to avoid infinite growth
                   // but keep the very end to scan for partial matches
                   int keepBytes = frameSize - 1;
                   Array.Copy(_holdBuffer, _holdBufferLength - keepBytes, _holdBuffer, 0, keepBytes);
                   _holdBufferLength = keepBytes;
               }
           }
           else
           {
               // Simple stream layout: sequential un-framed raw binary
               int completeFrames = _holdBufferLength / frameSize;
               if (completeFrames > 0)
               {
                   double[][] frameData = new double[Parser.NumberOfChannels][];
                   for (int ch = 0; ch < Parser.NumberOfChannels; ch++)
                   {
                       frameData[ch] = new double[completeFrames];
                   }

                   int offset = 0;
                   for (int frame = 0; frame < completeFrames; frame++)
                   {
                       for (int channel = 0; channel < Parser.NumberOfChannels; channel++)
                       {
                           frameData[channel][frame] = ReadBinaryValue(_holdBuffer, offset, Parser.Format);
                           offset += bytesPerChannel;
                       }
                   }

                   try
                   {
                       AddDataToRingBuffers(frameData);
                   }
                   catch (Exception)
                   {
                       // Ignore processing issues and keep streaming
                   }

                   int remainingBytes = _holdBufferLength - (completeFrames * frameSize);
                   if (remainingBytes > 0)
                   {
                       Array.Copy(_holdBuffer, completeFrames * frameSize, _holdBuffer, 0, remainingBytes);
                   }
                   _holdBufferLength = remainingBytes;
               }
           }
        }

        private static double ReadBinaryValue(byte[] data, int offset, DataParser.BinaryFormat format)
        {
           switch (format)
           {
               case DataParser.BinaryFormat.int16_t:
                   return (short)(data[offset] | (data[offset + 1] << 8));
               case DataParser.BinaryFormat.uint16_t:
                   return (ushort)(data[offset] | (data[offset + 1] << 8));
               case DataParser.BinaryFormat.int32_t:
                   return BitConverter.ToInt32(data, offset);
               case DataParser.BinaryFormat.uint32_t:
                   return BitConverter.ToUInt32(data, offset);
               case DataParser.BinaryFormat.float_t:
                   return BitConverter.ToSingle(data, offset);
               default:
                   return 0;
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
                TotalFrames += newSamples;

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

            // Raise Disposing event BEFORE disposing to notify virtual channels
            Disposing?.Invoke(this, EventArgs.Empty);

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
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid ClassGuid, IntPtr Enumerator, IntPtr hwndParent, uint Flags);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr DeviceInfoSet, IntPtr DeviceInfoData, ref Guid InterfaceClassGuid, uint MemberIndex, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr DeviceInfoSet, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData, IntPtr DeviceInterfaceDetailData, int DeviceInterfaceDetailDataSize, out int RequiredSize, ref SP_DEVINFO_DATA DeviceInfoData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
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
        private const int FILE_FLAG_OVERLAPPED = unchecked((int)0x40000000);
        private const uint POLICY_ALLOW_PARTIAL_READS = 0x00000002;
        private const uint POLICY_IGNORE_SHORT_PACKETS = 0x00000003;
        private const uint POLICY_RAW_IO = 0x00000001;

        /// <summary>
        /// Enumerates all connected devices matching the PowerScope WinUSB interface GUID.
        /// Returns display strings suitable for a ComboBox.
        /// Format: "FX2G3 PowerScope [path index]" or the device path itself if only one is found.
        /// </summary>
        public static string[] GetAvailableDevices()
        {
            Guid guid = new Guid(DeviceInterfaceGuid);
            List<string> results = new List<string>();

            IntPtr h = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (h == IntPtr.Zero || h.ToInt64() == -1)
                return new string[0];

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
                                Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                                if (SetupDiGetDeviceInterfaceDetail(h, ref did, detail, required, out required, ref devInfo))
                                {
                                    string path = Marshal.PtrToStringUni(IntPtr.Add(detail, 4));
                                    if (!string.IsNullOrEmpty(path))
                                        results.Add(path);
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

                    // Build human-readable display strings from device paths.
            // A path looks like \\?\USB#VID_04B4&PID_0081#<instance>#{guid}
            // Extract the instance ID segment (index 2) as a short discriminator.
            string[] displayNames = new string[results.Count];
            for (int i = 0; i < results.Count; i++)
            {
                string path = results[i];
                string instance = string.Empty;

                string[] segments = path.TrimStart('\\', '?').Split('#');
                if (segments.Length >= 3)
                    instance = segments[2]; // e.g. "7&49e5708&0&4"

                if (results.Count == 1)
                    displayNames[i] = "FX2G3 PowerScope";
                else
                    displayNames[i] = $"FX2G3 PowerScope [{instance}]";
            }

            return displayNames;
        }

        /// <summary>
        /// Returns the raw device paths indexed identically to GetAvailableDevices().
        /// Used internally to resolve the selected display name back to a path.
        /// </summary>
        public static string[] GetAvailableDevicePaths()
        {
            Guid guid = new Guid(DeviceInterfaceGuid);
            List<string> results = new List<string>();

            IntPtr h = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (h == IntPtr.Zero || h.ToInt64() == -1)
                return new string[0];

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
                                Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                                if (SetupDiGetDeviceInterfaceDetail(h, ref did, detail, required, out required, ref devInfo))
                                {
                                    string path = Marshal.PtrToStringUni(IntPtr.Add(detail, 4));
                                    if (!string.IsNullOrEmpty(path))
                                        results.Add(path);
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

                    return results.ToArray();
        }

        private static string FindDevicePath(Guid guid)
        {
            IntPtr h = SetupDiGetClassDevs(
                ref guid,
                IntPtr.Zero,
                IntPtr.Zero,
                DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);

            if (h == IntPtr.Zero || h.ToInt64() == -1)
                return null;

            try
            {
                SP_DEVICE_INTERFACE_DATA did = new SP_DEVICE_INTERFACE_DATA
                {
                    cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>()
                };

                for (uint i = 0; SetupDiEnumDeviceInterfaces(h, IntPtr.Zero, ref guid, i, ref did); i++)
                {
                    SP_DEVINFO_DATA devInfo = new SP_DEVINFO_DATA
                    {
                        cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>()
                    };

                    // First call: get required buffer size
                    SetupDiGetDeviceInterfaceDetail(
                        h, ref did, IntPtr.Zero, 0, out int required, ref devInfo);

                    IntPtr detail = Marshal.AllocHGlobal(required);
                    try
                    {
                        // Write cbSize into raw buffer first
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);

                        if (SetupDiGetDeviceInterfaceDetail(
                            h, ref did, detail, required, out required, ref devInfo))
                        {
                            // Dump first 32 bytes so we can see the raw memory
                            Debug.WriteLine("Raw bytes:");
                            for (int b = 0; b < Math.Min(32, required); b++)
                            {
                                byte val = Marshal.ReadByte(detail, b);
                                Debug.Write($"{val:X2} ");
                            }
                            Debug.WriteLine("\n");
                            Debug.WriteLine($"required size = {required}");
                            Debug.WriteLine($"IntPtr.Size = {IntPtr.Size}");

                            // Marshal the whole struct — no manual offset needed
                            var detailData = Marshal.PtrToStructure<SP_DEVICE_INTERFACE_DETAIL_DATA>(detail);
                            string path = detailData.DevicePath;
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

            var dev = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (dev.IsInvalid)
            {
                int err = Marshal.GetLastWin32Error();
                throw new System.ComponentModel.Win32Exception(err, $"CreateFile failed for path '{path}' (Win32 error {err})");
            }

            if (!WinUsb_Initialize(dev, out var h))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

            // Allow partial reads and ignore short packets so ReadPipe returns
            // as soon as data is available rather than waiting for a full buffer.
            // RAW_IO is intentionally omitted: it requires exact-multiple-of-MaxPacketSize
            // transfers and would block indefinitely on variable-length payloads.
            uint one = 1;
            WinUsb_SetPipePolicy(h, PipeIn, POLICY_ALLOW_PARTIAL_READS, sizeof(uint), ref one);
            WinUsb_SetPipePolicy(h, PipeIn, POLICY_IGNORE_SHORT_PACKETS, sizeof(uint), ref one);

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