using System.Text;
using System.IO.Ports;
using System.Diagnostics;
using System.ComponentModel;

namespace PowerScope.Model
{
    /// <summary>
    /// Exception thrown when a serial port is not found on the system
    /// </summary>
    public class PortNotFoundException : Exception
    {
        public string PortName { get; }

        public PortNotFoundException(string portName)
            : base($"Port '{portName}' was not found on this system")
        {
            PortName = portName;
        }

        public PortNotFoundException(string portName, Exception innerException)
            : base($"Port '{portName}' was not found on this system", innerException)
        {
            PortName = portName;
        }
    }

    /// <summary>
    /// Exception thrown when a serial port is already in use by another application
    /// </summary>
    public class PortAlreadyInUseException : Exception
    {
        public string PortName { get; }

        public PortAlreadyInUseException(string portName)
            : base($"Port '{portName}' is already in use by another application")
        {
            PortName = portName;
        }

        public PortAlreadyInUseException(string portName, Exception innerException)
            : base($"Port '{portName}' is already in use by another application", innerException)
        {
            PortName = portName;
        }
    }

    public class SerialDataStream : IDataStream, IChannelConfigurable, IBufferResizable, IResamplable
    {
        private readonly SerialPort _port;
        private readonly byte[] _readBuffer;
        private readonly byte[] _workingBuffer;

        // Zero-allocation binary parse path (see DataParser.ParseInto).
        // These are allocated once and reused on every read; only used when the parser is in a
        // binary mode. ASCII parsers fall back to the allocating DataParser.ParseData path.
        //   _parseOutput[channel][0.._parsedSampleCount)  — samples decoded by the last ParseInto call
        //   _processedSamples[0.._parsedSampleCount)       — gain/offset/filter scratch, reused per channel
        //   _residueBuffer[0.._residueLength)              — incomplete trailing bytes carried to the next read
        // All three are written only on the background read thread.
        private readonly bool _useFastBinaryPath;
        private readonly double[][] _parseOutput;
        private readonly double[] _processedSamples;
        private readonly byte[] _residueBuffer;
        private int _residueLength;

        private Thread _readSerialPortThread;
        private bool _disposed = false;
        private bool _isConnected;
        private bool _isStreaming;
        private string _statusMessage;

        // Sample rate calculation using high-precision timing
        private long _totalSamplesAtLastCalculation = 0;
        private readonly Stopwatch _sampleRateStopwatch = new Stopwatch();
        private double _calculatedSampleRate = 0.0;
        private readonly object _sampleRateCalculationLock = new object();

        // Exponential low pass filter parameters for sample rate smoothing
        // _sampleRateFilterAlpha is used to smooth out moment-to-moment inconsistencies in the calculated data rate,
        // providing a stable sample rate value to the UI. Default 0.1 provides moderate smoothing (0.01 = heavy, 1.0 = none).
        private const double DefaultFilterAlpha = 0.1;
        private double _sampleRateFilterAlpha = DefaultFilterAlpha;

        // Up/Down sampling
        private readonly Resampler _resampler;

        public SourceSetting SourceSetting { get; init; }
        public long TotalSamples { get; private set; }
        public long TotalBits { get; private set; }
        private RingBuffer<double>[] ReceivedData { get; set; }
        public DataParser Parser { get; init; }
        public int SerialPortUpdateRateHz { get; set; } = 300;

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
        /// Sample rate of the serial data stream in samples per second (Hz)
        /// Calculated dynamically based on received data rate with exponential low pass filtering
        /// When up/down sampling is enabled, this reflects the final sample rate after processing
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
            get { return "Serial"; }
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

        public SerialDataStream(SourceSetting source, DataParser dataParser)
        {
            SourceSetting = source;
            Parser = dataParser;
            ValidatePortExists(source.PortName);

            // Initialize up/down sampling
            _resampler = new Resampler(0); // 0 = bypass; overwritten by StreamSettings on creation
            _resampler.PropertyChanged += OnResamplerPropertyChanged;

            _port = new SerialPort(source.PortName)
            {
                ReadBufferSize = 4096,
                WriteBufferSize = 2048,
                BaudRate = source.BaudRate,
                DataBits = source.DataBits,
                Parity = source.Parity,
                Encoding = Encoding.ASCII,
                ReadTimeout = 100, // Reduced for responsiveness
                WriteTimeout = 1000,
                Handshake = Handshake.None,
                DtrEnable = false,
                RtsEnable = false
            };

            switch (source.StopBits)
            {
                case 2:
                    _port.StopBits = StopBits.Two;
                    break;
                case 1:
                default:
                    _port.StopBits = StopBits.One;
                    break;
            }

            // Initialize with smart default buffer size
            // PlotManager will call SetBufferSize() later with the actual PlotSettings.BufferSize
            int defaultBufferSize = Math.Max(500000, source.BaudRate / 5);
            InitializeRingBuffers(defaultBufferSize);

            // Initialize channel processing arrays
            _channelSettings = new ChannelSettings[dataParser.NumberOfChannels];
            _channelFilters = new IDigitalFilter[dataParser.NumberOfChannels];

            // Larger buffers for better performance
            _readBuffer = new byte[16384]; // Fixed 16KB chunks
            _workingBuffer = new byte[32768]; // Fixed 32KB working space

            // Residue can be at most one full working buffer (when no complete frame is found).
            _residueBuffer = new byte[_workingBuffer.Length];
            _residueLength = 0;

            // Pre-allocate the zero-allocation parse buffers for binary parsers. Worst case is one
            // full working buffer of the smallest sample, so size to (working buffer / bytes-per-sample).
            _useFastBinaryPath = Parser.Mode == DataParser.ParserMode.Binary;
            if (_useFastBinaryPath)
            {
                int maxSamplesPerBatch = (_workingBuffer.Length / Parser.BytesPerSample) + 1;
                _parseOutput = new double[ChannelCount][];
                for (int channel = 0; channel < ChannelCount; channel++)
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

        #region IBufferResizable Implementation

        public int BufferSize
        {
            get
            {
                lock (_channelConfigLock)
                {
                    if (ReceivedData == null || ReceivedData[0] == null)
                        return 0;
                    return ReceivedData[0].Capacity;
                }
            }
            set
            {
                if (value <= 0)
                    return;

                lock (_channelConfigLock)
                {
                    // Clear existing data
                    if (ReceivedData != null)
                    {
                        foreach (RingBuffer<double> buffer in ReceivedData)
                        {
                            buffer?.Clear();
                        }

                    }

                    // Recreate ring buffers with new size - no need to stop/restart streaming
                    InitializeRingBuffers(value);

                    // Reset statistics
                    TotalSamples = 0;
                    TotalBits = 0;
                }

                // Notify that buffer size changed
                OnPropertyChanged(nameof(BufferSize));
            }
        }

        private void InitializeRingBuffers(int bufferSize)
        {
            ReceivedData = new RingBuffer<double>[ChannelCount];
            for (int i = 0; i < ChannelCount; i++)
            {
                ReceivedData[i] = new RingBuffer<double>(bufferSize);
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

        private static void ValidatePortExists(string portName)
        {
            if (string.IsNullOrWhiteSpace(portName))
            {
                string actualPortName = portName;
                if (actualPortName == null)
                    actualPortName = "null";
                throw new PortNotFoundException(actualPortName);
            }
            string[] availablePorts = SerialPort.GetPortNames();
            bool portExists = false;
            foreach (string port in availablePorts)
            {
                if (string.Equals(port, portName, StringComparison.OrdinalIgnoreCase))
                {
                    portExists = true;
                    break;
                }
            }
            if (!portExists)
            {
                throw new PortNotFoundException(portName);
            }
        }

        public void Connect()
        {
            try
            {
                _port.Open();
                IsConnected = true;
                StatusMessage = "Connected";
            }
            catch (UnauthorizedAccessException)
            {
                if (_port != null)
                    _port.Dispose();
                StatusMessage = "Port already in use";
                IsConnected = false;
                //throw new PortAlreadyInUseException(SourceSetting.PortName, ex);
            }
            catch (ArgumentException)
            {
                if (_port != null)
                    _port.Dispose();
                StatusMessage = "Port not found";
                IsConnected = false;
                //throw new PortNotFoundException(SourceSetting.PortName, ex);
            }
            catch (InvalidOperationException)
            {
                if (_port != null)
                    _port.Dispose();
                StatusMessage = "Port already in use";
                IsConnected = false;
                //throw new PortAlreadyInUseException(SourceSetting.PortName, ex);
            }
            catch (System.IO.IOException)
            {
                if (_port != null)
                    _port.Dispose();
                StatusMessage = "Could not find port";
                IsConnected = false;
                //throw new PortAlreadyInUseException(SourceSetting.PortName, ex);
            }
        }

        public void Disconnect()
        {
            StopStreaming();
            try
            {
                if (_port != null && _port.IsOpen)
                    _port.Close();
            }
            catch (Exception)
            {
                // Port might already be closed or in error state
            }
            finally
            {
                IsConnected = false;
                if (_statusMessage != "Disconnected")
                {
                    StatusMessage = "Disconnected";
                }
            }
        }

        public void StartStreaming()
        {
            if (!_isConnected || _isStreaming)
                return;

            // Verify port is still open before starting
            try
            {
                if (_port == null || !_port.IsOpen)
                {
                    StatusMessage = "Cannot start streaming: Port not connected";
                    IsConnected = false;
                    return;
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Cannot start streaming: {ex.Message}";
                IsConnected = false;
                return;
            }

            IsStreaming = true;

            // Reset filters when starting streaming
            ResetChannelFilters();

            // Initialize up/down sampling for current channel configuration
            _resampler.InitializeChannels(ChannelCount);
            _resampler.Reset();

            if (_readSerialPortThread != null && _readSerialPortThread.IsAlive)
            {
                IsStreaming = false;
                _readSerialPortThread.Join(1000);
            }

            _readSerialPortThread = new Thread(ReadSerialData) { IsBackground = true };
            _readSerialPortThread.Start();

            StatusMessage = "Streaming";
        }

        public void StopStreaming()
        {
            if (!_isStreaming)
                return;
            IsStreaming = false;
            if (_readSerialPortThread != null && _readSerialPortThread.IsAlive)
                _readSerialPortThread.Join(1000);

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
            TotalSamples = 0;
            TotalBits = 0;
            _residueLength = 0;

            // Reset sample rate calculation when clearing data
            lock (_sampleRateCalculationLock)
            {
                _calculatedSampleRate = 0.0;
                _sampleRateStopwatch.Reset();
                _totalSamplesAtLastCalculation = 0;
            }

            // Reset filters and up/down sampling when clearing data
            ResetChannelFilters();
            _resampler.Reset();
        }

        private void ReadSerialData()
        {
            int minBytesThreshold = 64;
            int maxReadSize = _readBuffer.Length;
            int consecutiveErrorCount = 0;
            const int maxConsecutiveErrors = 5; // Allow some errors before giving up

            while (_isStreaming && _port.IsOpen)
            {
                try
                {
                    // Check if port is still physically connected
                    if (!_port.IsOpen)
                    {
                        HandleRuntimeDisconnection("Port closed unexpectedly");
                        break;
                    }

                    int bytesAvailable = _port.BytesToRead;
                    if (bytesAvailable >= minBytesThreshold)
                    {
                        int bytesToRead = Math.Min(bytesAvailable, maxReadSize);
                        int bytesRead = _port.Read(_readBuffer, 0, bytesToRead);

                        if (bytesRead > 0)
                        {
                            TotalBits += bytesRead * 8;
                            ProcessReceivedData(_readBuffer, bytesRead, _workingBuffer);
                            consecutiveErrorCount = 0; // Reset error counter on successful read
                        }
                    }
                    else
                    {
                        Thread.Sleep(Math.Max(1, 1000 / SerialPortUpdateRateHz));
                    }
                }
                catch (TimeoutException)
                {
                    // Timeout is normal, just continue
                    continue;
                }
                catch (InvalidOperationException ex)
                {
                    // Port was closed or became invalid - always fatal
                    HandleRuntimeDisconnection($"Port operation failed: {ex.Message}");
                    break;
                }
                catch (System.IO.IOException ex)
                {
                    // Cable disconnected, device removed, or I/O error
                    consecutiveErrorCount++;
                    if (consecutiveErrorCount >= maxConsecutiveErrors)
                    {
                        // Too many I/O errors - disconnect properly
                        HandleRuntimeDisconnection($"I/O error (cable disconnected?): {ex.Message}");
                        break;
                    }
                    else
                    {
                        Thread.Sleep(100); // Brief pause before retry
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    // Port access was lost (another application took over) - always fatal
                    HandleRuntimeDisconnection($"Port access lost: {ex.Message}");
                    break;
                }
                catch (ArgumentException ex)
                {
                    // Invalid arguments indicate a programming error or corrupted state - fatal
                    HandleRuntimeDisconnection($"Argument error: {ex.Message}");
                    break;
                }
                catch (Exception ex)
                {
                    // Unexpected error
                    consecutiveErrorCount++;
                    if (consecutiveErrorCount >= maxConsecutiveErrors)
                    {
                        // Too many unexpected errors - disconnect properly
                        HandleRuntimeDisconnection($"Unexpected error: {ex.Message}");
                        break;
                    }
                    else
                    {
                        Thread.Sleep(50); // Brief pause before retry
                    }
                }
            }
        }

        /// <summary>
        /// Handles runtime disconnection events (cable unplug, device removal, etc.)
        /// Updates connection status and notifies UI through property change events
        /// </summary>
        /// <param name="reason">Human-readable reason for the disconnection</param>
        private void HandleRuntimeDisconnection(string reason)
        {
            // Update status first
            StatusMessage = $"Disconnected: {reason}";

            // Stop streaming
            IsStreaming = false;

            // Close the port safely
            if (_port != null && _port.IsOpen)
            {
                try
                {
                    _port.Close();
                }
                catch
                {
                    // Port might already be closed, ignore errors
                }
            }

            // Update connection status
            IsConnected = false;
        }

        private void ProcessReceivedData(byte[] readBuffer, int bytesRead, byte[] workingBuffer)
        {
            int totalDataLength = bytesRead;
            int workingBufferOffset = 0;

            // Prepend the unparsed tail from the previous read so samples/frames spanning a read
            // boundary are reassembled before parsing. Residue lives in _residueBuffer[0.._residueLength).
            if (_residueLength > 0)
            {
                if (_residueLength + bytesRead > workingBuffer.Length)
                {
                    // Residue + new data would overflow the working buffer — drop residue and resync.
                    _residueLength = 0;
                }
                else
                {
                    Array.Copy(_residueBuffer, 0, workingBuffer, 0, _residueLength);
                    workingBufferOffset = _residueLength;
                    totalDataLength += _residueLength;
                }
            }

            // Copy new data to working buffer, after any prepended residue
            Array.Copy(readBuffer, 0, workingBuffer, workingBufferOffset, bytesRead);

            if (_useFastBinaryPath)
            {
                // Zero-allocation path: ParseInto fills _parseOutput and refills _residueBuffer in place.
                int sampleCount = Parser.ParseInto(
                    workingBuffer.AsSpan(0, totalDataLength),
                    _parseOutput,
                    _residueBuffer,
                    out _residueLength);

                // Safety: a runaway residue (more than half the buffer) indicates a format mismatch.
                if (_residueLength > workingBuffer.Length / 2)
                    _residueLength = 0;

                if (sampleCount > 0)
                    AddProcessedToRingBuffers(sampleCount);
            }
            else
            {
                // Legacy allocating path for ASCII (not throughput-critical). ParseData returns a
                // freshly allocated double[][] and residue byte[]; copy the residue into our reusable
                // buffer so the prepend logic above stays uniform across both paths.
                ParsedData parsedData = Parser.ParseData(workingBuffer.AsSpan(0, totalDataLength));

                _residueLength = 0;
                if (parsedData.Residue != null &&
                    parsedData.Residue.Length > 0 &&
                    parsedData.Residue.Length <= workingBuffer.Length / 2)
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
            TotalSamples += producedSamples;
            UpdateSampleRateCalculation(producedSamples);
        }

        /// <summary>
        /// Legacy allocating consumer used only by the ASCII path. Mirrors <see cref="AddProcessedToRingBuffers"/>
        /// but takes the freshly allocated double[][] from <see cref="DataParser.ParseData"/>.
        /// </summary>
        private void AddDataToRingBuffers(double[][] parsedData)
        {
            int channelsToProcess = parsedData.Length;
            if (channelsToProcess > Parser.NumberOfChannels)
                channelsToProcess = Parser.NumberOfChannels;

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
                TotalSamples += producedSamples;
                UpdateSampleRateCalculation(producedSamples);
            }
        }

        /// <summary>
        /// Updates the calculated sample rate based on incoming data
        /// Uses high-precision Stopwatch timing and exponential low pass filter for smooth, accurate sample rate calculation
        /// </summary>
        private void UpdateSampleRateCalculation(int newSamples)
        {
            lock (_sampleRateCalculationLock)
            {
                // Start the stopwatch on first sample
                if (!_sampleRateStopwatch.IsRunning)
                {
                    _sampleRateStopwatch.Start();
                    _totalSamplesAtLastCalculation = TotalSamples;
                    return; // Need at least two data points for rate calculation
                }

                double elapsedSeconds = _sampleRateStopwatch.Elapsed.TotalSeconds;

                // Calculate sample rate every 500ms for responsive but stable updates
                if (elapsedSeconds >= 0.5)
                {
                    long samplesSinceLastCalculation = TotalSamples - _totalSamplesAtLastCalculation;
                    double instantaneousSampleRate = samplesSinceLastCalculation / elapsedSeconds;

                    // Apply exponential low pass filter for smooth sample rate
                    if (_calculatedSampleRate == 0.0)
                    {
                        // First calculation - initialize filter
                        _calculatedSampleRate = instantaneousSampleRate;
                    }
                    else
                    {
                        // Exponential low pass filter: y[n] = α * x[n] + (1 - α) * y[n-1]
                        // Where α is the filter alpha (smoothing factor)
                        _calculatedSampleRate = _sampleRateFilterAlpha * instantaneousSampleRate +
                                              (1.0 - _sampleRateFilterAlpha) * _calculatedSampleRate;
                    }

                    // Reset for next calculation period
                    _totalSamplesAtLastCalculation = TotalSamples;
                    _sampleRateStopwatch.Restart();

                    // Notify property changed for real-time updates
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
            if (_port != null && _port.IsOpen)
                _port.Close();
            if (_port != null)
                _port.Dispose();
            if (ReceivedData != null)
            {
                foreach (RingBuffer<double> ringBuffer in ReceivedData)
                {
                    if (ringBuffer != null)
                        ringBuffer.Clear();
                }
            }

            // Unsubscribe from up/down sampling events
            _resampler.PropertyChanged -= OnResamplerPropertyChanged;

            _residueLength = 0;
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
    }

    public class SourceSetting
    {
        public enum DataSource { Serial, Audio };
        public string PortName { get; init; }
        public int BaudRate { get; init; }
        public int DataBits { get; init; }
        public int StopBits { get; init; }
        public Parity Parity { get; init; }
        public DataSource Source { get; init; }
        public string AudioDeviceName { get; init; }

        public SourceSetting(string portName, int baudRate, int dataBits, int stopBits, Parity parity)
        {
            PortName = portName;
            BaudRate = baudRate;
            Parity = parity;
            DataBits = dataBits;
            StopBits = stopBits;
            AudioDeviceName = null;
            Source = DataSource.Serial;
        }

        public SourceSetting(string DeviceName)
        {
            PortName = "";
            BaudRate = 0;
            DataBits = 8;
            AudioDeviceName = DeviceName;
            Source = DataSource.Audio;
        }
    }
}
