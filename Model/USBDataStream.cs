using System.ComponentModel;
using System.Runtime.InteropServices;
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

    public class USBDataStream : IDataStream, IChannelConfigurable, IResamplable
    {
        // Constants from your example
        public const string DeviceInterfaceGuid = "{8D2C9D52-5C6B-4F0B-9F1B-3EBE8C4F9A61}";
        private const byte PipeIn = 0x81;
        private const byte REQ_START             = 0xA0;
        private const byte REQ_STOP              = 0xA1;
        private const byte REQ_SET_BUF_THRESHOLD = 0xA2;
        private const byte REQ_SET_BAUD          = 0xA3;
        private const byte REQ_SET_INTERFACE     = 0xA4;

        // USB handles
        private SafeFileHandle _deviceHandle;
        private IntPtr _winUsbHandle = IntPtr.Zero;
        private readonly Guid _deviceGuid;

        // Data processing
        private byte[] _readBuffer;      // raw USB read chunk
        private byte[] _workingBuffer;   // residue + new bytes, handed to DataParser

        // Zero-allocation binary parse path (see DataParser.ParseInto). Allocated once and reused on
        // every read; only used when the parser is in a binary mode (USB streams are binary in practice).
        // ASCII parsers fall back to the allocating DataParser.ParseData path.
        //   _parseOutput[channel][0.._sampleCount)  — samples decoded by the last ParseInto call
        //   _processedSamples[0.._sampleCount)        — gain/offset/filter scratch, reused per channel
        //   _residueBuffer[0.._residueLength)         — incomplete trailing bytes carried to the next read
        // All three are written only on the USB read thread.
        private readonly bool _useFastBinaryPath;
        private double[][] _parseOutput;
        private double[] _processedSamples;
        private byte[] _residueBuffer;
        private int _residueLength;

        private Thread _readUsbThread;
        private bool _disposed = false;
        private bool _isConnected;
        private bool _isStreaming;
        private string _statusMessage;

        // Streaming metrics — all calculated inside the read thread, exposed via INotifyPropertyChanged
        private long _pendingSampleCount = 0;   // samples accumulated since last metric window
        private long _pendingBytesCount = 0;    // raw bytes accumulated since last metric window
        private DateTime _lastSampleTime = DateTime.Now;
        private double _calculatedSampleRate = 0.0;
        private double _throughputKBps = 0.0;
        private readonly object _metricsLock = new object();

        public UsbSourceSetting SourceSetting { get; init; }

        // Lifetime counters: written on the USB read thread, read on the UI thread.
        // Accessed via Interlocked/Volatile so readers never see a torn or stale value.
        private long _totalSamples;
        private long _totalBits;
        private long _totalFrames;
        private long _totalReadErrors;
        private int _lastWin32Error;

        public long TotalSamples => Interlocked.Read(ref _totalSamples);
        public long TotalBits => Interlocked.Read(ref _totalBits);
        public long TotalFrames => Interlocked.Read(ref _totalFrames);
        public long TotalReadErrors => Interlocked.Read(ref _totalReadErrors);
        public int LastWin32Error => Volatile.Read(ref _lastWin32Error);

        private RingBuffer<double>[] ReceivedData { get; set; }
        public DataParser Parser { get; init; }
        public int UsbUpdateRateHz { get; set; } = 1000;

        /// <summary>
        /// WinUSB device-interface path of the specific FX2G3 unit to open (uniquely identifies
        /// one physical board via its USB instance segment). When null/empty, Connect() opens the
        /// first available PowerScope device. Must be set before Connect().
        /// </summary>
        public string SelectedDevicePath { get; set; }

        // Physical interface the FX2G3 uses to communicate with the attached device.
        private UsbInterfaceType _interface = UsbInterfaceType.UART;

        /// <summary>
        /// Physical interface type (SPI / UART / I2C). Stored for display and serialization;
        /// the firmware is notified via REQ_SET_BAUD when Interface = UART and UartBaudRate is set.
        /// </summary>
        public UsbInterfaceType Interface
        {
            get => _interface;
            set
            {
                if (_interface == value) return;
                _interface = value;
                OnPropertyChanged(nameof(Interface));

                if (_winUsbHandle != IntPtr.Zero)
                {
                    try { SendSetInterface(_winUsbHandle, value); }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"USB SetInterface warning: {ex.Message}");
                    }
                }
            }
        }

        // Baud rate sent to the FX2G3 via 0xA3 SET_BAUD control transfer.
        // 0 means "don't send" (leave firmware default intact).
        private int _uartBaudRate = 0;

        /// <summary>
        /// UART baud rate for the FX2G3's SCB1 interface.
        /// When set to a non-zero value while connected, sends a 0xA3 SET_BAUD control transfer.
        /// 0 = do not change the firmware's baud rate.
        /// </summary>
        public int UartBaudRate
        {
            get => _uartBaudRate;
            set
            {
                if (_uartBaudRate == value) return;
                _uartBaudRate = value;
                OnPropertyChanged(nameof(UartBaudRate));

                if (_winUsbHandle != IntPtr.Zero && value > 0)
                {
                    try { SendSetBaudRate(_winUsbHandle, (uint)value); }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"USB SetBaudRate warning: {ex.Message}");
                    }
                }
            }
        }

        // Bytes to accumulate on the FX2G3 before it fires a USB transfer.
        // Smaller = lower latency, more USB overhead. Valid range: 1..512.
        // Default 128 bytes: balances latency and USB overhead across a wide range
        // of UART baud rates. At 115200 baud: ~11 ms per packet (~90 Hz), well above
        // the 30 Hz plot timer. Increase to 256–512 for higher baud rates (≥1 MBaud).
        private int _uartBufThreshold = 128;

        /// <summary>
        /// Controls how many UART bytes the FX2G3 accumulates before sending a USB packet.
        /// Smaller values reduce latency at the cost of USB bus efficiency.
        /// Valid range: 1–512. Changes are sent to the device immediately if connected.
        /// </summary>
        public int UartBufferThreshold
        {
            get => _uartBufThreshold;
            set
            {
                int clamped = Math.Max(1, Math.Min(512, value));
                if (_uartBufThreshold == clamped) return;
                _uartBufThreshold = clamped;
                OnPropertyChanged(nameof(UartBufferThreshold));

                if (_winUsbHandle != IntPtr.Zero)
                {
                    try { SendSetBufThreshold(_winUsbHandle, (ushort)clamped); }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"USB SetBufThreshold warning: {ex.Message}");
                    }
                }
            }
        }

        // Channel-specific processing
        private ChannelSettings[] _channelSettings;
        private IDigitalFilter[] _channelFilters;
        private readonly object _channelConfigLock = new object();

        // Up/Down sampling
        private readonly Resampler _resampler;

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
        /// Per-channel sample rate in samples/second. Calculated by the USB read thread
        /// using a 200 ms accumulation window + exponential moving average.
        /// </summary>
        public double SampleRate
        {
            get { lock (_metricsLock) { return _calculatedSampleRate; } }
        }

        /// <summary>
        /// Raw USB throughput in KB/s. Calculated by the USB read thread from actual
        /// bytes received (includes framing overhead). Updated every 200 ms.
        /// </summary>
        public double ThroughputKBps
        {
            get { lock (_metricsLock) { return _throughputKBps; } }
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

            // Initialize up/down sampling
            _resampler = new Resampler(1);
            _resampler.PropertyChanged += OnResamplerPropertyChanged;

            int ringBufferSize = Math.Max(500000, 1000000); // Large buffer for USB throughput
            ReceivedData = new RingBuffer<double>[dataParser.NumberOfChannels];

            for (int i = 0; i < dataParser.NumberOfChannels; i++)
            {
                ReceivedData[i] = new RingBuffer<double>(ringBufferSize);
            }

            // Initialize channel processing arrays
            _channelSettings = new ChannelSettings[dataParser.NumberOfChannels];
            _channelFilters = new IDigitalFilter[dataParser.NumberOfChannels];

            // USB transfer buffers. _workingBuffer must hold residue + one full read,
            // so it is sized larger than _readBuffer.
            _readBuffer = new byte[64 * 1024];      // 64KB USB read chunk
            _workingBuffer = new byte[128 * 1024];  // residue + new data assembly space

            // Residue can be at most one full working buffer (when no complete frame is found).
            _residueBuffer = new byte[_workingBuffer.Length];
            _residueLength = 0;

            // Pre-allocate the zero-allocation parse buffers for binary parsers. Worst case is one
            // full working buffer of the smallest sample, so size to (working buffer / bytes-per-sample).
            _useFastBinaryPath = Parser.Mode == DataParser.ParserMode.Binary;
            if (_useFastBinaryPath)
            {
                int maxSamplesPerBatch = (_workingBuffer.Length / Parser.BytesPerSample) + 1;
                _parseOutput = new double[dataParser.NumberOfChannels][];
                for (int channel = 0; channel < dataParser.NumberOfChannels; channel++)
                {
                    _parseOutput[channel] = new double[maxSamplesPerBatch];
                }
                _processedSamples = new double[maxSamplesPerBatch];
            }

            _isConnected = false;
            _isStreaming = false;
            StatusMessage = "Disconnected";
        }

        private void OnResamplerPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // Forward up/down sampling property change notifications
            if (e.PropertyName == nameof(Resampler.SamplingFactor))
            {
                OnPropertyChanged(nameof(ResamplingFactor));
                OnPropertyChanged(nameof(SampleRateMultiplier));
                OnPropertyChanged(nameof(IsResamplingEnabled));
                OnPropertyChanged(nameof(ResamplingDescription));
                OnPropertyChanged(nameof(SampleRate));

                // Reinitialize for new sampling factor
                _resampler.InitializeChannels(ChannelCount);
            }
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
                IDigitalFilter newFilter = settings?.Filter;
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

        /// <summary>
        /// Applies gain/offset and filtering for one channel across a whole batch of samples.
        /// Settings/filter are fetched once per batch rather than once per sample: a config
        /// change landing mid-batch is visible on the next batch instead of immediately, which
        /// is an acceptable tradeoff for a live data stream and avoids per-sample lock overhead.
        /// The gain/offset pass has no cross-sample dependency and auto-vectorizes; the filter
        /// pass is inherently sequential (IIR state) so it stays as a separate loop.
        /// </summary>
        private void ApplyChannelProcessing(int channel, double[] rawSamples, double[] destination, int sampleCount)
        {
            ChannelSettings settings;
            IDigitalFilter filter;

            lock (_channelConfigLock)
            {
                settings = _channelSettings[channel];
                filter = _channelFilters[channel];
            }

            if (settings == null)
            {
                Array.Copy(rawSamples, destination, sampleCount);
            }
            else
            {
                double gain = settings.Gain;
                double offset = settings.Offset;
                for (int i = 0; i < sampleCount; i++)
                {
                    double processed = gain * (rawSamples[i] + offset);
                    if (!double.IsFinite(processed))
                        processed = 0.0;
                    destination[i] = processed;
                }
            }

            if (filter != null)
            {
                for (int i = 0; i < sampleCount; i++)
                {
                    double filtered = filter.Filter(destination[i]);
                    if (!double.IsFinite(filtered))
                        filtered = 0.0;
                    destination[i] = filtered;
                }
            }
        }

        #endregion

        public void Connect()
        {
            try
            {
                string path = ResolveDevicePath();
                if (path == null)
                    throw new UsbDeviceNotFoundException(_deviceGuid.ToString());

                (SafeFileHandle dev, IntPtr winusb) = OpenPath(path);
                _deviceHandle = dev;
                _winUsbHandle = winusb;

                IsConnected = true;
                StatusMessage = "Connected";
            }
            catch (UsbDeviceNotFoundException)
            {
                StatusMessage = "USB device not found";
                IsConnected = false;
                CleanupHandles();
            }
            catch (UsbDeviceInUseException)
            {
                StatusMessage = "USB device already in use by another application";
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
                    // Set interface first so the firmware reconfigures SCB1 before
                    // streaming starts. SPI ignores SET_BAUD and SET_BUF_THRESHOLD.
                    SendSetInterface(_winUsbHandle, _interface);
                    SendStart(_winUsbHandle);
                    SendSetBufThreshold(_winUsbHandle, (ushort)_uartBufThreshold);
                    if (_uartBaudRate > 0 && _interface == UsbInterfaceType.UART)
                        SendSetBaudRate(_winUsbHandle, (uint)_uartBaudRate);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"USB SendStart warning (non-fatal): {ex.Message}");
                }

                IsStreaming = true;

                // Reset filters when starting streaming
                ResetChannelFilters();

                // Initialize up/down sampling for current channel configuration
                _resampler.InitializeChannels(ChannelCount);
                _resampler.Reset();

                if (_readUsbThread != null && _readUsbThread.IsAlive)
                {
                    IsStreaming = false;
                    _readUsbThread.Join(1000);
                }

                _readUsbThread = new Thread(ReadUsbData) { IsBackground = true };
                _readUsbThread.Start();

                StatusMessage = "Running";
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

            return ReceivedData[channel].CopyLatestTo(destination, n);
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
            Interlocked.Exchange(ref _totalSamples, 0);
            Interlocked.Exchange(ref _totalBits, 0);
            Interlocked.Exchange(ref _totalFrames, 0);
            _residueLength = 0;
            lock (_metricsLock)
            {
                _pendingSampleCount = 0;
                _pendingBytesCount = 0;
                _calculatedSampleRate = 0.0;
                _throughputKBps = 0.0;
                _lastSampleTime = DateTime.Now;
            }
            OnPropertyChanged(nameof(SampleRate));
            OnPropertyChanged(nameof(ThroughputKBps));

            // Reset filters and up/down sampling when clearing data
            ResetChannelFilters();
            _resampler.Reset();
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
                        Volatile.Write(ref _lastWin32Error, error);

                        if (error == 995) // ERROR_OPERATION_ABORTED - normal when stopping
                            break;

                        long errorCount = Interlocked.Increment(ref _totalReadErrors);
                        consecutiveErrorCount++;

                        StatusMessage = $"Read error Win32={error} (total errors: {errorCount})";

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
                        Interlocked.Add(ref _totalBits, bytesRead * 8L);
                        lock (_metricsLock) { _pendingBytesCount += bytesRead; }
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
                    Interlocked.Increment(ref _totalReadErrors);
                    Volatile.Write(ref _lastWin32Error, ex.NativeErrorCode);
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
                    Interlocked.Increment(ref _totalReadErrors);
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
            StatusMessage = $"Disconnected: {reason}";
            IsStreaming = false;

            try
            {
                CleanupHandles();
            }
            catch
            {
                // Handles might already be invalid; ignore errors
            }

            IsConnected = false;
        }

        /// <summary>
        /// Assembles received bytes (prepending any residue from the previous read) and
        /// delegates decoding to the shared <see cref="DataParser"/>. This is the same
        /// path used by SerialDataStream — there is no USB-specific
        /// parser. ASCII and binary (framed or continuous) all flow through Parser.ParseData.
        /// </summary>
        private void ProcessReceivedUsbData(byte[] readBuffer, int bytesRead)
        {
            int totalDataLength = bytesRead;
            int workingBufferOffset = 0;

            // Prepend the unparsed tail from the previous read so frames spanning a read boundary are
            // reassembled before parsing. Residue lives in _residueBuffer[0.._residueLength).
            if (_residueLength > 0)
            {
                if (_residueLength + bytesRead > _workingBuffer.Length)
                {
                    // Residue unexpectedly large — drop it and resync on the fresh data.
                    _residueLength = 0;
                }
                else
                {
                    Array.Copy(_residueBuffer, 0, _workingBuffer, 0, _residueLength);
                    workingBufferOffset = _residueLength;
                    totalDataLength += _residueLength;
                }
            }

            Array.Copy(readBuffer, 0, _workingBuffer, workingBufferOffset, bytesRead);

            if (_useFastBinaryPath)
            {
                // Zero-allocation path: ParseInto fills _parseOutput and refills _residueBuffer in place.
                int sampleCount = Parser.ParseInto(
                    _workingBuffer.AsSpan(0, totalDataLength),
                    _parseOutput,
                    _residueBuffer,
                    out _residueLength);

                // Safety: never let residue grow unbounded (indicates a sync/format mismatch).
                if (_residueLength > _workingBuffer.Length / 2)
                    _residueLength = 0;

                if (sampleCount > 0)
                    AddProcessedToRingBuffers(sampleCount);
            }
            else
            {
                // Legacy allocating path for ASCII (not throughput-critical). Copy ParseData's residue
                // into our reusable buffer so the prepend logic above stays uniform across both paths.
                ParsedData parsedData = Parser.ParseData(_workingBuffer.AsSpan(0, totalDataLength));

                _residueLength = 0;
                if (parsedData.Residue != null &&
                    parsedData.Residue.Length > 0 &&
                    parsedData.Residue.Length <= _workingBuffer.Length / 2)
                {
                    _residueLength = parsedData.Residue.Length;
                    Array.Copy(parsedData.Residue, 0, _residueBuffer, 0, _residueLength);
                }

                if (parsedData.Data != null)
                    AddDataToRingBuffers(parsedData.Data);
            }
        }

        /// <summary>
        /// Hot-path consumer for the zero-allocation binary parse. Applies gain/offset/filter to the
        /// freshly decoded samples in <see cref="_parseOutput"/> and pushes them to the ring buffers.
        ///
        /// <see cref="_processedSamples"/> is reused across channels rather than allocated per call.
        /// This is safe because (1) each channel is fully processed and pushed before the next channel
        /// overwrites the buffer, and (2) IDigitalFilter.Filter() is sample-by-sample — the filter owns
        /// its own history, so we never need the whole input window resident at once (no block convolution).
        ///
        /// Up/down sampling, when enabled, runs through Resampler's zero-allocation overload
        /// (reused per-channel buffers), so this path stays allocation-free either way.
        /// </summary>
        /// <param name="sampleCount">Number of valid samples per channel in _parseOutput.</param>
        private void AddProcessedToRingBuffers(int sampleCount)
        {
            int producedSamples = sampleCount;

            for (int channel = 0; channel < ChannelCount; channel++)
            {
                double[] rawSamples = _parseOutput[channel];
                ApplyChannelProcessing(channel, rawSamples, _processedSamples, sampleCount);

                if (_resampler.IsEnabled)
                {
                    // Zero-allocation resampling: reads _processedSamples[0..sampleCount) and hands back
                    // a reused per-channel buffer we consume immediately (before the next channel's call).
                    int resampledCount = _resampler.ProcessChannelData(channel, _processedSamples, sampleCount, out double[] resampled);
                    for (int i = 0; i < resampledCount; i++)
                    {
                        if (!double.IsFinite(resampled[i]))
                            resampled[i] = 0.0;
                    }

                    ReceivedData[channel].AddRange(resampled, resampledCount);
                    if (channel == 0)
                        producedSamples = resampledCount;
                }
                else
                {
                    ReceivedData[channel].AddRange(_processedSamples, sampleCount);
                }
            }

            // Bookkeeping uses the final per-channel count (after optional resampling).
            Interlocked.Add(ref _totalSamples, producedSamples);
            Interlocked.Add(ref _totalFrames, producedSamples);
            UpdateStreamingMetrics(producedSamples);
        }

        /// <summary>
        /// Legacy allocating consumer used only by the ASCII path. Mirrors <see cref="AddProcessedToRingBuffers"/>
        /// but takes the freshly allocated double[][] from <see cref="DataParser.ParseData"/>.
        /// </summary>
        private void AddDataToRingBuffers(double[][] parsedData)
        {
            int channelsToProcess = Math.Min(parsedData.Length, Parser.NumberOfChannels);

            int producedSamples = 0;

            for (int channel = 0; channel < channelsToProcess; channel++)
            {
                if (parsedData[channel] == null || parsedData[channel].Length == 0)
                    continue;

                double[] channelProcessedSamples = new double[parsedData[channel].Length];
                ApplyChannelProcessing(channel, parsedData[channel], channelProcessedSamples, parsedData[channel].Length);

                double[] channelFinalData;
                int finalCount;
                if (_resampler.IsEnabled)
                {
                    finalCount = _resampler.ProcessChannelData(channel, channelProcessedSamples, channelProcessedSamples.Length, out channelFinalData);
                    for (int i = 0; i < finalCount; i++)
                    {
                        if (!double.IsFinite(channelFinalData[i]))
                            channelFinalData[i] = 0.0;
                    }
                }
                else
                {
                    channelFinalData = channelProcessedSamples;
                    finalCount = channelProcessedSamples.Length;
                }

                ReceivedData[channel].AddRange(channelFinalData, finalCount);
                if (channel == 0)
                    producedSamples = finalCount;
            }

            if (producedSamples > 0)
            {
                Interlocked.Add(ref _totalSamples, producedSamples);
                Interlocked.Add(ref _totalFrames, producedSamples);
                UpdateStreamingMetrics(producedSamples);
            }
        }

        /// <summary>
        /// Updates the calculated sample rate based on incoming data
        /// Uses a moving average approach similar to StreamInfoPanel
        /// </summary>
        private void UpdateStreamingMetrics(int newSamples)
        {
            lock (_metricsLock)
            {
                // Accumulate every batch so the full window is measured, not just the last packet.
                _pendingSampleCount += newSamples;
                // _pendingBytesCount is updated in ReadUsbData before ProcessReceivedUsbData is called.

                DateTime currentTime = DateTime.Now;
                double dt = (currentTime - _lastSampleTime).TotalSeconds;

                if (dt >= 0.2)
                {
                    // Sample rate: per-channel samples/second with EMA smoothing
                    double instantRate = _pendingSampleCount / dt;
                    _pendingSampleCount = 0;

                    if (_calculatedSampleRate == 0.0)
                        _calculatedSampleRate = instantRate;
                    else
                        _calculatedSampleRate = 0.2 * instantRate + 0.8 * _calculatedSampleRate;

                    // Throughput: raw bytes / second (includes framing overhead)
                    _throughputKBps = _pendingBytesCount / 1024.0 / dt;
                    _pendingBytesCount = 0;

                    _lastSampleTime = currentTime;

                    OnPropertyChanged(nameof(SampleRate));
                    OnPropertyChanged(nameof(ThroughputKBps));
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

            // Unsubscribe from up/down sampling events
            _resampler.PropertyChanged -= OnResamplerPropertyChanged;

            _disposed = true;
            GC.SuppressFinalize(this);
        }

        void IDataStream.clearData()
        {
            clearData();
        }

        #region IResamplable Implementation

        public int ResamplingFactor
        {
            get { return _resampler.SamplingFactor; }
            set { _resampler.SamplingFactor = value; }
        }

        public double SampleRateMultiplier
        {
            get { return _resampler.SampleRateMultiplier; }
        }

        public bool IsResamplingEnabled
        {
            get { return _resampler.IsEnabled; }
        }

        public string ResamplingDescription
        {
            get { return _resampler.GetDescription(); }
        }

        #endregion

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

        // WinUSB pipe policy values (winusbio.h WINUSB_PIPE_POLICY enum)
        private const uint POLICY_AUTO_CLEAR_STALL       = 0x00000002;
        private const uint POLICY_PIPE_TRANSFER_TIMEOUT  = 0x00000003;
        private const uint POLICY_IGNORE_SHORT_PACKETS   = 0x00000004;
        private const uint POLICY_ALLOW_PARTIAL_READS    = 0x00000005;
        private const uint POLICY_RAW_IO                 = 0x00000007;

        /// <summary>
        /// Core SetupAPI enumeration. Returns the device-interface paths of every connected
        /// device exposing <paramref name="guid"/>. Single source of truth for device discovery —
        /// GetAvailableDevices / GetAvailableDevicePaths / ResolveDevicePath all delegate here.
        /// </summary>
        private static List<string> EnumerateDevicePaths(Guid guid)
        {
            List<string> results = new List<string>();

            IntPtr h = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (h == IntPtr.Zero || h.ToInt64() == -1)
                return results;

            try
            {
                SP_DEVICE_INTERFACE_DATA did = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
                for (uint i = 0; SetupDiEnumDeviceInterfaces(h, IntPtr.Zero, ref guid, i, ref did); i++)
                {
                    SP_DEVINFO_DATA devInfo = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };

                    // First call sizes the detail buffer; second call fills it.
                    SetupDiGetDeviceInterfaceDetail(h, ref did, IntPtr.Zero, 0, out int required, ref devInfo);

                    IntPtr detail = Marshal.AllocHGlobal(required);
                    try
                    {
                        // cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA: 8 on x64, 6 on x86.
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        if (SetupDiGetDeviceInterfaceDetail(h, ref did, detail, required, out required, ref devInfo))
                        {
                            // DevicePath string begins 4 bytes past cbSize in the marshalled struct.
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

            return results;
        }

        /// <summary>
        /// Enumerates connected PowerScope WinUSB devices as display strings for a ComboBox.
        /// Format: "FX2G3 PowerScope" (single device) or "FX2G3 PowerScope [instance]" (multiple).
        /// </summary>
        public static string[] GetAvailableDevices()
        {
            List<string> paths = EnumerateDevicePaths(new Guid(DeviceInterfaceGuid));

            // A path looks like \\?\USB#VID_04B4&PID_0081#<instance>#{guid}
            // Extract the instance ID segment (index 2) as a short discriminator.
            string[] displayNames = new string[paths.Count];
            for (int i = 0; i < paths.Count; i++)
            {
                string instance = string.Empty;
                string[] segments = paths[i].TrimStart('\\', '?').Split('#');
                if (segments.Length >= 3)
                    instance = segments[2]; // e.g. "7&49e5708&0&4"

                if (paths.Count == 1)
                    displayNames[i] = "FX2G3 PowerScope";
                else
                    displayNames[i] = $"FX2G3 PowerScope [{instance}]";
            }

            return displayNames;
        }

        /// <summary>
        /// Returns the raw device paths indexed identically to <see cref="GetAvailableDevices"/>.
        /// Used to resolve a selected display name back to a concrete device path.
        /// </summary>
        public static string[] GetAvailableDevicePaths()
        {
            return EnumerateDevicePaths(new Guid(DeviceInterfaceGuid)).ToArray();
        }

        /// <summary>
        /// Determines which connected device to open. Honors <see cref="SelectedDevicePath"/> when
        /// that exact device is still present; if the selection is gone but exactly one device is
        /// connected, falls back to it (same board on a different port). With multiple devices and
        /// no valid selection, returns null so the caller fails rather than opening the wrong board.
        /// </summary>
        private string ResolveDevicePath()
        {
            List<string> paths = EnumerateDevicePaths(_deviceGuid);
            if (paths.Count == 0)
                return null;

            if (!string.IsNullOrEmpty(SelectedDevicePath))
            {
                foreach (string p in paths)
                {
                    if (string.Equals(p, SelectedDevicePath, StringComparison.OrdinalIgnoreCase))
                        return p;
                }

                // Selected device not present. Disambiguate only when there is no ambiguity.
                if (paths.Count == 1)
                    return paths[0];
                return null;
            }

            return paths[0]; // no explicit selection — first available
        }

        private static (SafeFileHandle dev, IntPtr winusb) OpenPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                throw new UsbDeviceNotFoundException(DeviceInterfaceGuid);

            SafeFileHandle dev = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (dev.IsInvalid)
            {
                int err = Marshal.GetLastWin32Error();
                // ERROR_ACCESS_DENIED (5) / ERROR_SHARING_VIOLATION (32) => another process holds the device.
                if (err == 5 || err == 32)
                    throw new UsbDeviceInUseException(path, new System.ComponentModel.Win32Exception(err));
                throw new System.ComponentModel.Win32Exception(err, $"CreateFile failed for path '{path}' (Win32 error {err})");
            }

            if (!WinUsb_Initialize(dev, out IntPtr h))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

            // ALLOW_PARTIAL_READS: ReadPipe completes as soon as any data arrives,
            // even if fewer bytes than requested (required for short-packet streaming).
            // IGNORE_SHORT_PACKETS left at default (0/false): short packets (<MaxPacketSize)
            // complete the read immediately instead of being accumulated.
            // PIPE_TRANSFER_TIMEOUT left at default (0 = infinite): no timeout on bulk reads.
            // RAW_IO intentionally omitted: requires exact-multiple-of-MaxPacketSize buffers.
            uint one = 1;
            WinUsb_SetPipePolicy(h, PipeIn, POLICY_ALLOW_PARTIAL_READS, sizeof(uint), ref one);
            WinUsb_SetPipePolicy(h, PipeIn, POLICY_AUTO_CLEAR_STALL, sizeof(uint), ref one);

            return (dev, h);
        }

        private static void SendStart(IntPtr h)
        {
            WINUSB_SETUP_PACKET setup = new WINUSB_SETUP_PACKET
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

        private static void SendSetBufThreshold(IntPtr h, ushort threshold)
        {
            WINUSB_SETUP_PACKET setup = new WINUSB_SETUP_PACKET
            {
                RequestType = 0x40, // Host-to-device | Vendor | Recipient: Device
                Request = REQ_SET_BUF_THRESHOLD,
                Value = threshold,
                Index = 0,
                Length = 0
            };
            int transferred;
            if (!WinUsb_ControlTransfer(h, setup, null, 0, out transferred, IntPtr.Zero))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        /// <summary>
        /// Sends a 0xA3 SET_BAUD vendor control transfer to the FX2G3.
        /// The 32-bit baud rate is split across wValue (low 16 bits) and wIndex (high 16 bits)
        /// so no data stage is needed. Firmware reconstructs: baud = wValue | (wIndex &lt;&lt; 16).
        /// </summary>
        private static void SendSetBaudRate(IntPtr h, uint baudRate)
        {
            WINUSB_SETUP_PACKET setup = new WINUSB_SETUP_PACKET
            {
                RequestType = 0x40, // Host-to-device | Vendor | Device
                Request     = REQ_SET_BAUD,
                Value       = (ushort)(baudRate & 0xFFFF),
                Index       = (ushort)(baudRate >> 16),
                Length      = 0
            };
            int transferred;
            if (!WinUsb_ControlTransfer(h, setup, null, 0, out transferred, IntPtr.Zero))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        /// <summary>
        /// Sends a 0xA4 SET_INTERFACE vendor control transfer to the FX2G3.
        /// wValue 0 = UART, 1 = SPI. The firmware calls SwitchInterface() which
        /// reconfigures SCB1 and resets the streaming state.
        /// </summary>
        private static void SendSetInterface(IntPtr h, UsbInterfaceType iface)
        {
            ushort wValue;
            if (iface == UsbInterfaceType.SPI)
                wValue = 1;
            else
                wValue = 0; // UART (default); I2C not yet supported in firmware

            WINUSB_SETUP_PACKET setup = new WINUSB_SETUP_PACKET
            {
                RequestType = 0x40, // Host-to-device | Vendor | Recipient: Device
                Request     = REQ_SET_INTERFACE,
                Value       = wValue,
                Index       = 0,
                Length      = 0
            };
            int transferred;
            if (!WinUsb_ControlTransfer(h, setup, null, 0, out transferred, IntPtr.Zero))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        private static void SendStop(IntPtr h)
        {
            WINUSB_SETUP_PACKET setup = new WINUSB_SETUP_PACKET
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

        public UsbSourceSetting(string deviceGuid, string deviceName = "USB Device")
        {
            DeviceGuid = deviceGuid;
            DeviceName = deviceName;
        }
    }
}