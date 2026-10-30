using System.ComponentModel;

namespace PowerScope.Model
{
    public class DemoDataStream : IDataStream, IChannelConfigurable, IBufferResizable, IResamplable
    {
        private bool _disposed = false;
        private bool _isConnected = false;
        private bool _isStreaming = false;
        private string _statusMessage;
        private Timer _dataGenerationTimer;
        
        public DemoSettings DemoSettings { get; init; }
        public long TotalSamples { get; private set; }
        public long TotalBits { get; private set; }
        private RingBuffer<double>[] ReceivedData { get; set; }
        private readonly object _dataLock = new object();
        private double _timeAccumulator = 0;
        private Random _random = new Random();

        // Reusable [channel][sample] generation buffer, allocated once per streaming session in
        // StartStreaming (the chunk size is fixed for the session). Avoids allocating fresh arrays
        // on every timer tick. Only written on the timer thread, guarded by _dataLock.
        private double[][] _generationBuffer;

        // Channel-specific processing
        private ChannelSettings[] _channelSettings;
        private IDigitalFilter[] _channelFilters;
        private readonly object _channelConfigLock = new object();

        // Up/Down sampling
        private readonly Resampler _resampler;

        public int ChannelCount { get; }

        /// <summary>
        /// Sample rate of the demo data stream in samples per second (Hz)
        /// </summary>
        public double SampleRate 
        { 
            get 
            { 
                double baseSampleRate = DemoSettings.SampleRate;
                if (_resampler.IsEnabled)
                {
                    return baseSampleRate * _resampler.SampleRateMultiplier;
                }
                return baseSampleRate;
            } 
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
            get { return "Demo"; }
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

        public DemoDataStream(DemoSettings demoSettings)
        {
            DemoSettings = demoSettings;
            ChannelCount = demoSettings.NumberOfChannels;
            
            // Initialize up/down sampling (0 = bypass; overwritten by StreamSettings on creation)
            _resampler = new Resampler(0);
            _resampler.PropertyChanged += OnResamplerPropertyChanged;
            
            // Initialize ring buffers for each channel
            int ringBufferSize = Math.Max(500000, demoSettings.SampleRate * 10); // 10 seconds of data
            InitializeRingBuffers(ringBufferSize);
            
            // Initialize channel processing arrays
            _channelSettings = new ChannelSettings[ChannelCount];
            _channelFilters = new IDigitalFilter[ChannelCount];
            
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
                    _timeAccumulator = 0;
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
            double processed = settings.Gain * (rawSample + settings.Offset);
            
            // Apply filter if configured
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
            IsConnected = true;
            StatusMessage = "Connected (Demo Mode)";
        }

        public void Disconnect()
        {
            StopStreaming();
            IsConnected = false;
            StatusMessage = "Disconnected";
        }

        public void StartStreaming()
        {
            if (!_isConnected || _isStreaming)
                return;

            IsStreaming = true;
            StatusMessage = "Streaming (Demo Mode)";
            
            // Reset filters when starting streaming
            ResetChannelFilters();
            
            // Initialize up/down sampling for current channel configuration
            _resampler.InitializeChannels(ChannelCount);
            _resampler.Reset();
            
            // Calculate timer interval to generate data at the specified sample rate
            // Generate data in chunks of ~100 samples at a time for smooth streaming
            int samplesPerChunk = Math.Max(1, DemoSettings.SampleRate / 100);
            int timerInterval = Math.Max(1, (samplesPerChunk * 1000) / DemoSettings.SampleRate);

            // Allocate the reusable generation buffer for this session's fixed chunk size.
            _generationBuffer = new double[ChannelCount][];
            for (int channel = 0; channel < ChannelCount; channel++)
            {
                _generationBuffer[channel] = new double[samplesPerChunk];
            }

            _dataGenerationTimer = new Timer(GenerateData, samplesPerChunk, 0, timerInterval);
        }

        public void StopStreaming()
        {
            if (!_isStreaming)
                return;
                
            IsStreaming = false;
            _dataGenerationTimer?.Dispose();
            _dataGenerationTimer = null;
            StatusMessage = "Stopped";
        }

        private void GenerateData(object state)
        {
            if (!IsStreaming)
                return;

            int samplesPerChunk = (int)state;

            lock (_dataLock)
            {
                double[][] buffer = _generationBuffer;
                int producedSamples = samplesPerChunk;

                // Generate raw samples and apply channel processing for all channels in parallel,
                // writing into the reusable per-channel buffers (each channel owns its own row).
                Parallel.For(0, ChannelCount, channel =>
                {
                    double[] dest = buffer[channel];
                    for (int sample = 0; sample < samplesPerChunk; sample++)
                    {
                        double time = _timeAccumulator + (double)sample / DemoSettings.SampleRate;
                        double rawSample = GenerateSampleForChannel(channel, time);
                        dest[sample] = ApplyChannelProcessing(channel, rawSample);
                    }
                });

                // Update time accumulator based on original sample rate
                _timeAccumulator += (double)samplesPerChunk / DemoSettings.SampleRate;

                // Push to ring buffers. Both the common path and optional up/down sampling reuse
                // buffers (Resampler's zero-allocation overload), so this stays allocation-free.
                for (int channel = 0; channel < ChannelCount; channel++)
                {
                    if (_resampler.IsEnabled)
                    {
                        double[] resampled;
                        int resampledCount;
                        try
                        {
                            // Zero-allocation: reads buffer[channel][0..samplesPerChunk) and returns a
                            // reused per-channel buffer consumed before the next channel's call.
                            resampledCount = _resampler.ProcessChannelData(channel, buffer[channel], samplesPerChunk, out resampled);
                            for (int i = 0; i < resampledCount; i++)
                            {
                                if (!double.IsFinite(resampled[i]))
                                    resampled[i] = 0.0; // Replace NaN/Infinity with zero
                            }
                        }
                        catch (Exception)
                        {
                            resampled = buffer[channel]; // Fall back to processed samples
                            resampledCount = samplesPerChunk;
                        }

                        ReceivedData[channel].AddRange(resampled, resampledCount);
                        if (channel == 0)
                            producedSamples = resampledCount;
                    }
                    else
                    {
                        ReceivedData[channel].AddRange(buffer[channel], samplesPerChunk);
                    }
                }

                // Update statistics based on final data (after up/down sampling)
                TotalSamples += producedSamples;
                TotalBits += producedSamples * ChannelCount * 16; // Assume 16 bits per sample
            }
        }

        private double GenerateSampleForChannel(int channel, double time)
        {
            double baseFrequency = 60.0 + channel * 0.5; // Different frequency for each channel
            double amplitude = 1000; // Amplitude to make signals visible

            return DemoSettings.SignalType switch
            {
                "Sine Wave" => amplitude * Math.Sin(2 * Math.PI * baseFrequency * time),
                "Square Wave" => amplitude * Math.Sign(Math.Sin(2 * Math.PI * baseFrequency * time)),
                "Triangle Wave" => amplitude * (2.0 / Math.PI) * Math.Asin(Math.Sin(2 * Math.PI * baseFrequency * time)),
                "Random Noise" => amplitude * (_random.NextDouble() - 0.5) * 2,
                "Mixed Signals" => GenerateMixedSignal(channel, time, amplitude),
                "Chirp Signal" => GenerateChirpSignal(channel, time, amplitude),
                "Tones" => GenerateTonesSignal(channel, time, amplitude),
                "sin(x)/x" => GenerateSinXOverXSignal(channel, time, amplitude),
                _ => amplitude * Math.Sin(2 * Math.PI * baseFrequency * time)
            };
        }

        private double GenerateMixedSignal(int channel, double time, double amplitude)
        {
            // Generate different signal types for different channels
            return (channel % 4) switch
            {
                0 => amplitude * Math.Sin(2 * Math.PI * (1.0 + channel * 0.5) * time),
                1 => amplitude * Math.Sign(Math.Sin(2 * Math.PI * (1.0 + channel * 0.5) * time)),
                2 => amplitude * (2.0 / Math.PI) * Math.Asin(Math.Sin(2 * Math.PI * (1.0 + channel * 0.5) * time)),
                3 => amplitude * (_random.NextDouble() - 0.5) * 2,
                _ => amplitude * Math.Sin(2 * Math.PI * (1.0 + channel * 0.5) * time)
            };
        }

        /// <summary>
        /// Generates a chirp signal that sweeps from 10Hz to 10kHz over 3 seconds
        /// /// </summary>
        /// <param name="channel">Channel index (adds slight frequency offset per channel)</param>
        /// <param name="time">Current time in seconds</param>
        /// <param name="amplitude">Signal amplitude</param>
        /// <returns>Chirp signal sample</returns>
        private double GenerateChirpSignal(int channel, double time, double amplitude)
        {
            // Chirp parameters
            const double chirpDuration = 6.0; // 6 seconds
            const double startFrequency = 10.0; // 10 Hz
            const double endFrequency = 10000.0; // 10 kHz
            
            // Add slight frequency offset per channel to make them distinguishable
            double channelFreqOffset = channel * 100.0; // 100 Hz offset per channel
            double adjustedStartFreq = startFrequency + channelFreqOffset;
            double adjustedEndFreq = endFrequency + channelFreqOffset;
            
            // Calculate the time within the current chirp cycle
            double cycleTime = time % chirpDuration;
            
            // Linear frequency sweep: f(t) = f0 + (f1 - f0) * t / T
            double instantaneousFreq = adjustedStartFreq + (adjustedEndFreq - adjustedStartFreq) * (cycleTime / chirpDuration);
            
            // Calculate the phase for the chirp signal
            // For a linear chirp: ?(t) = 2? * [f0 * t + (f1 - f0) * tï¿½ / (2 * T)]
            double phase = 2 * Math.PI * (adjustedStartFreq * cycleTime + 
                          (adjustedEndFreq - adjustedStartFreq) * cycleTime * cycleTime / (2 * chirpDuration));
            
            // Generate the chirp signal
            return amplitude * Math.Sin(phase);
        }

        /// <summary>
        /// Generates a test signal with 1kHz main tone plus two side tones for FFT testing
        /// Channel 0: 1kHz + side tones at 750Hz and 1250Hz (half amplitude)
        /// Higher channels: Side tones get progressively closer to 1kHz main tone
        /// </summary>
        /// <param name="channel">Channel index (affects side tone placement)</param>
        /// <param name="time">Current time in seconds</param>
        /// <param name="amplitude">Main tone amplitude (1000)</param>
        /// <returns>Composite signal with main tone plus side tones</returns>
        private double GenerateTonesSignal(int channel, double time, double amplitude)
        {
            // Main tone at 1kHz with full amplitude
            const double mainFrequency = 1000.0; // 1kHz
            double mainTone = amplitude * Math.Sin(2 * Math.PI * mainFrequency * time);
            
            // Calculate side tone frequencies based on channel
            // Channel 0: ï¿½250Hz offset (750Hz and 1250Hz)
            // Higher channels: Progressively smaller offsets approaching 1kHz
            double baseOffset = 250.0; // Base offset of 250Hz for channel 0
            double offsetReduction = Math.Min(channel * 40.0, 250.0); // Reduce offset by 40Hz per channel, max 200Hz reduction
            double actualOffset = Math.Max(baseOffset - offsetReduction, 10.0); // Minimum 10Hz offset to keep tones distinguishable
            
            // Side tone frequencies
            double lowerSideTone = mainFrequency - actualOffset;
            double upperSideTone = mainFrequency + actualOffset;
            
            // Side tones with half amplitude
            double sideToneAmplitude = amplitude * 0.5;
            double lowerTone = sideToneAmplitude * Math.Sin(2 * Math.PI * lowerSideTone * time);
            double upperTone = sideToneAmplitude * Math.Sin(2 * Math.PI * upperSideTone * time);
            
            // Combine all tones
            return mainTone + lowerTone + upperTone;
        }

        /// <summary>
        /// Generates a 400Hz sine wave for testing sin(x)/x interpolation
        /// This frequency is just below the Nyquist rate for 1000Hz sampling (Nyquist = 500Hz)
        /// Each channel gets a slight frequency offset to make them distinguishable
        /// </summary>
        /// <param name="channel">Channel index (adds slight frequency offset per channel)</param>
        /// <param name="time">Current time in seconds</param>
        /// <param name="amplitude">Signal amplitude</param>
        /// <returns>400Hz sine wave sample for sin(x)/x interpolation testing</returns>
        private double GenerateSinXOverXSignal(int channel, double time, double amplitude)
        {
            // Base frequency at 400Hz - just below Nyquist for 1000Hz sample rate
            const double baseFrequency = 350.0; // 400 Hz
            
            // Add slight frequency offset per channel to make them distinguishable
            // Keep offsets small to stay well below Nyquist frequency
            double channelFreqOffset = channel * 10.0; // 10 Hz offset per channel
            double actualFrequency = baseFrequency + channelFreqOffset;
            
            // Ensure we don't exceed a reasonable limit (stay below 480Hz to maintain margin from Nyquist)
            actualFrequency = Math.Min(actualFrequency, 480.0);
            
            // Generate sine wave at the target frequency
            return amplitude * Math.Sin(2 * Math.PI * actualFrequency * time);
        }

        public int CopyLatestTo(int channel, double[] destination, int n)
        {
            if (channel < 0 || channel >= ReceivedData.Length)
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
                _timeAccumulator = 0;
            }
            
            // Reset filters and up/down sampling when clearing data
            ResetChannelFilters();
            _resampler?.Reset();
        }

        public void Dispose()
        {
            if (_disposed)
                return;
                
            // Raise Disposing event BEFORE disposing to notify virtual channels
            Disposing?.Invoke(this, EventArgs.Empty);
      
            if (_isStreaming)
                StopStreaming();
            
            _dataGenerationTimer?.Dispose();
       
            if (ReceivedData != null)
            {
                foreach (RingBuffer<double> ringBuffer in ReceivedData)
                {
                    ringBuffer?.Clear();
                }
            }
            
            // Unsubscribe from up/down sampling events
            if (_resampler != null)
            {
                _resampler.PropertyChanged -= OnResamplerPropertyChanged;
            }
            
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        #endregion


    }

    public class DemoSettings
    {
        public int NumberOfChannels { get; init; }
        public int SampleRate { get; init; }
        public string SignalType { get; init; }

        public DemoSettings(int numberOfChannels, int sampleRate, string signalType)
        {
            NumberOfChannels = numberOfChannels;
            SampleRate = sampleRate;
            SignalType = signalType;
        }
    }
}