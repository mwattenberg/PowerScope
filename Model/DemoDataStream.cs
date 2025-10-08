using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace PowerScope.Model
{
    public class DemoDataStream : IDataStream, IChannelConfigurable, IBufferResizable
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

        // Channel-specific processing
        private ChannelSettings[] _channelSettings;
        private IDigitalFilter[] _channelFilters;
        private readonly object _channelConfigLock = new object();

        public int ChannelCount { get; }

        /// <summary>
        /// Sample rate of the demo data stream in samples per second (Hz)
        /// </summary>
        public double SampleRate 
        { 
            get { return DemoSettings.SampleRate; } 
        }

        public event PropertyChangedEventHandler PropertyChanged;

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
            
            // Initialize ring buffers for each channel
            int ringBufferSize = Math.Max(500000, demoSettings.SampleRate * 10); // 10 seconds of data
            InitializeRingBuffers(ringBufferSize);
            
            // Initialize channel processing arrays
            _channelSettings = new ChannelSettings[ChannelCount];
            _channelFilters = new IDigitalFilter[ChannelCount];
            
            StatusMessage = "Disconnected";
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
            double processed = rawSample * settings.Gain + settings.Offset;
            
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
            
            // Calculate timer interval to generate data at the specified sample rate
            // Generate data in chunks of ~100 samples at a time for smooth streaming
            int samplesPerChunk = Math.Max(1, DemoSettings.SampleRate / 100);
            int timerInterval = Math.Max(1, (samplesPerChunk * 1000) / DemoSettings.SampleRate);
            
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
            double[][] newData = new double[ChannelCount][];
            
            lock (_dataLock)
            {
                // Pre-allocate arrays
                for (int channel = 0; channel < ChannelCount; channel++)
                {
                    newData[channel] = new double[samplesPerChunk];
                }
                
                // Generate and process data for all channels in parallel
                Parallel.For(0, ChannelCount, channel =>
                {
                    for (int sample = 0; sample < samplesPerChunk; sample++)
                    {
                        double time = _timeAccumulator + (double)sample / DemoSettings.SampleRate;
                        double rawSample = GenerateSampleForChannel(channel, time);
                        
                        // Apply channel-specific processing (gain, offset, filtering)
                        double processedSample = ApplyChannelProcessing(channel, rawSample);
                        newData[channel][sample] = processedSample;
                    }
                });
                
                // Update time accumulator
                _timeAccumulator += (double)samplesPerChunk / DemoSettings.SampleRate;
                
                // Add processed data to ring buffers
                for (int channel = 0; channel < ChannelCount; channel++)
                {
                    ReceivedData[channel].AddRange(newData[channel]);
                }
                
                // Update statistics
                TotalSamples += samplesPerChunk;
                TotalBits += samplesPerChunk * ChannelCount * 16; // Assume 16 bits per sample
            }
        }

        private double GenerateSampleForChannel(int channel, double time)
        {
            double baseFrequency = 1.0 + channel * 0.5; // Different frequency for each channel
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
        /// </summary>
        /// <param name="channel">Channel index (adds slight frequency offset per channel)</param>
        /// <param name="time">Current time in seconds</param>
        /// <param name="amplitude">Signal amplitude</param>
        /// <returns>Chirp signal sample</returns>
        private double GenerateChirpSignal(int channel, double time, double amplitude)
        {
            // Chirp parameters
            const double chirpDuration = 3.0; // 3 seconds
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
            // For a linear chirp: ?(t) = 2? * [f0 * t + (f1 - f0) * t² / (2 * T)]
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
            // Channel 0: ±250Hz offset (750Hz and 1250Hz)
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
            
            // Reset filters when clearing data
            ResetChannelFilters();
        }

        public void Dispose()
        {
            if (_disposed)
                return;
                
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