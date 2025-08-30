using System;
using System.Threading;
using System.Threading.Tasks;

namespace SerialPlotDN_WPF.Model
{
    public class DemoDataStream : IDataStream
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

        public int ChannelCount { get; }

        public string StatusMessage
        {
            get { return _statusMessage; }
            private set { _statusMessage = value; }
        }

        public string StreamType
        {
            get { return "Demo"; }
        }

        public bool IsConnected
        {
            get { return _isConnected; }
            private set { _isConnected = value; }
        }

        public bool IsStreaming
        {
            get { return _isStreaming; }
            private set { _isStreaming = value; }
        }

        public DemoDataStream(DemoSettings demoSettings)
        {
            DemoSettings = demoSettings;
            ChannelCount = demoSettings.NumberOfChannels;
            
            // Initialize ring buffers for each channel
            int ringBufferSize = Math.Max(500000, demoSettings.SampleRate * 10); // 10 seconds of data
            ReceivedData = new RingBuffer<double>[ChannelCount];
            for (int i = 0; i < ChannelCount; i++)
            {
                ReceivedData[i] = new RingBuffer<double>(ringBufferSize);
            }
            
            _statusMessage = "Disconnected";
        }

        public void Connect()
        {
            _isConnected = true;
            _statusMessage = "Connected (Demo Mode)";
        }

        public void Disconnect()
        {
            StopStreaming();
            _isConnected = false;
            _statusMessage = "Disconnected";
        }

        public void StartStreaming()
        {
            if (!_isConnected || _isStreaming)
                return;

            _isStreaming = true;
            _statusMessage = "Streaming (Demo Mode)";
            
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
                
            _isStreaming = false;
            _dataGenerationTimer?.Dispose();
            _dataGenerationTimer = null;
            _statusMessage = "Stopped";
        }

        private void GenerateData(object state)
        {
            if (!_isStreaming)
                return;

            int samplesPerChunk = (int)state;
            double[][] newData = new double[ChannelCount][];
            
            lock (_dataLock)
            {
                for (int channel = 0; channel < ChannelCount; channel++)
                {
                    newData[channel] = new double[samplesPerChunk];
                    for (int sample = 0; sample < samplesPerChunk; sample++)
                    {
                        double time = _timeAccumulator + (double)sample / DemoSettings.SampleRate;
                        newData[channel][sample] = GenerateSampleForChannel(channel, time);
                    }
                }
                
                // Update time accumulator
                _timeAccumulator += (double)samplesPerChunk / DemoSettings.SampleRate;
                
                // Add data to ring buffers
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