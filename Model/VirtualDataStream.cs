using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace PowerScope.Model
{
    /// <summary>
    /// Represents a virtual data stream that performs operations on existing channel(s)
    /// WITHOUT creating duplicate ring buffers - computes on-demand when data is requested
    /// Supports mathematical operations (add, subtract, multiply, divide) and filtering
    /// Can operate on physical channels OR other virtual channels (chaining supported)
    /// Constants are supported via ConstantDataStream-backed channels
    /// </summary>
    public class VirtualDataStream : IDataStream, IChannelConfigurable
    {
        private readonly List<Channel> _sourceChannels;
        private readonly HashSet<IDataStream> _sourceStreams;
        private readonly VirtualChannelOperationType _operation;
        private bool _disposed = false;

        private ChannelSettings _virtualChannelSettings;
        private IDigitalFilter _virtualFilter;
        private readonly object _settingsLock = new object();

        private double[] _computeBuffer1;
        private double[] _computeBuffer2;
        private int _allocatedSize = 0;
        private readonly object _computeLock = new object();

        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler Disposing;

        public VirtualDataStream(Channel sourceChannel)
        {
            if (sourceChannel == null)
                throw new ArgumentNullException(nameof(sourceChannel));

            _sourceChannels = new List<Channel> { sourceChannel };
            _operation = VirtualChannelOperationType.Add;

            _sourceStreams = new HashSet<IDataStream>();
            if (sourceChannel.OwnerStream != null)
            {
                _sourceStreams.Add(sourceChannel.OwnerStream);
            }

            InitializeComputeBuffers();
            SubscribeToSourceStreams();
        }

        public VirtualDataStream(Channel channelA, Channel channelB, VirtualChannelOperationType operation)
        {
            if (channelA == null)
                throw new ArgumentNullException(nameof(channelA));
            if (channelB == null)
                throw new ArgumentNullException(nameof(channelB));

            _sourceChannels = new List<Channel> { channelA, channelB };
            _operation = operation;

            _sourceStreams = new HashSet<IDataStream>();
            foreach (Channel channel in _sourceChannels)
            {
                if (channel != null && channel.OwnerStream != null)
                {
                    _sourceStreams.Add(channel.OwnerStream);
                }
            }

            InitializeComputeBuffers();
            SubscribeToSourceStreams();
        }

        private void SubscribeToSourceStreams()
        {
            foreach (IDataStream stream in _sourceStreams)
            {
                stream.Disposing += OnSourceStreamDisposing;
            }
        }

        private void UnsubscribeFromSourceStreams()
        {
            foreach (IDataStream stream in _sourceStreams)
            {
                stream.Disposing -= OnSourceStreamDisposing;
            }
        }

        private void OnSourceStreamDisposing(object sender, EventArgs e)
        {
            Dispose();
        }

        private void InitializeComputeBuffers()
        {
            int initialSize = 10000;
            _computeBuffer1 = new double[initialSize];
            _computeBuffer2 = new double[initialSize];
            _allocatedSize = initialSize;
        }

        private void EnsureComputeBuffersAllocated(int requiredSize)
        {
            int maxAllowedSize = 50_000_000;
            int clampedSize = Math.Min(requiredSize, maxAllowedSize);

            if (clampedSize <= _allocatedSize)
                return;

            int newSize = (int)(clampedSize * 1.5);

            _computeBuffer1 = new double[newSize];
            _computeBuffer2 = new double[newSize];
            _allocatedSize = newSize;
        }

        #region IDataStream Implementation

        public string StatusMessage
        {
            get
            {
                bool allConnected = IsConnected;
                bool anyStreaming = IsStreaming;

                if (!allConnected)
                    return "Disconnected (Source channel not connected)";
                if (anyStreaming)
                    return "Streaming (Virtual)";
                return "Connected (Virtual)";
            }
        }

        public string StreamType
        {
            get
            {
                if (_sourceChannels.Count == 1)
                {
                    string sourceType = _sourceChannels[0].StreamType;
                    return $"Virtual ({sourceType})";
                }
                else
                {
                    string source1Type = _sourceChannels[0].StreamType;
                    string source2Type = _sourceChannels[1].StreamType;
                    return $"Virtual ({source1Type} {GetOperationSymbol()} {source2Type})";
                }
            }
        }

        public bool IsConnected
        {
            get
            {
                foreach (Channel channel in _sourceChannels)
                {
                    if (!channel.IsStreamConnected)
                        return false;
                }
                return true;
            }
        }

        public bool IsStreaming
        {
            get
            {
                foreach (Channel channel in _sourceChannels)
                {
                    if (channel.IsStreamStreaming)
                        return true;
                }
                return false;
            }
        }

        public long TotalSamples
        {
            get
            {
                for (int i = 0; i < _sourceChannels.Count; i++)
                {
                    if (_sourceChannels[i].OwnerStream != null)
                        return _sourceChannels[i].OwnerStream.TotalSamples;
                }
                return 0;
            }
        }

        public long TotalBits
        {
            get
            {
                for (int i = 0; i < _sourceChannels.Count; i++)
                {
                    if (_sourceChannels[i].OwnerStream != null)
                        return _sourceChannels[i].OwnerStream.TotalBits;
                }
                return 0;
            }
        }

        public int ChannelCount
        {
            get { return 1; }
        }

        public double SampleRate
        {
            get
            {
                for (int i = 0; i < _sourceChannels.Count; i++)
                {
                    if (_sourceChannels[i].OwnerStream != null)
                        return _sourceChannels[i].OwnerStream.SampleRate;
                }
                return 1000.0;
            }
        }

        public void Connect()
        {
        }

        public void Disconnect()
        {
        }

        public void StartStreaming()
        {
        }

        public void StopStreaming()
        {
        }

        public int CopyLatestTo(int channel, double[] destination, int n)
        {
            if (channel != 0)
                return 0;

            if (_disposed)
                return 0;

            if (destination == null || n <= 0)
                return 0;

            if (!IsConnected || !IsStreaming)
                return 0;

            lock (_computeLock)
            {
                int actualSamples;

                if (_sourceChannels.Count == 1)
                {
                    actualSamples = _sourceChannels[0].CopyLatestDataTo(destination, n);
                }
                else
                {
                    actualSamples = ComputeBinaryOperation(destination, n);
                }

                if (actualSamples <= 0)
                    return 0;

                ApplyVirtualChannelProcessing(destination, actualSamples);

                return actualSamples;
            }
        }

        private int ComputeBinaryOperation(double[] destination, int n)
        {
            EnsureComputeBuffersAllocated(n);

            int samples1 = _sourceChannels[0].CopyLatestDataTo(_computeBuffer1, n);
            int samples2 = _sourceChannels[1].CopyLatestDataTo(_computeBuffer2, n);

            int actualSamples = Math.Min(samples1, samples2);
            if (actualSamples <= 0)
                return 0;

            for (int i = 0; i < actualSamples; i++)
            {
                double sample1 = _computeBuffer1[i];
                double sample2 = _computeBuffer2[i];

                double result = _operation switch
                {
                    VirtualChannelOperationType.Add => sample1 + sample2,
                    VirtualChannelOperationType.Subtract => sample1 - sample2,
                    VirtualChannelOperationType.Multiply => sample1 * sample2,
                    VirtualChannelOperationType.Divide => Math.Abs(sample2) > 1e-10 ? sample1 / sample2 : 0.0,
                    _ => sample1
                };

                if (!double.IsFinite(result))
                    result = 0.0;

                destination[i] = result;
            }

            return actualSamples;
        }

        private void ApplyVirtualChannelProcessing(double[] data, int count)
        {
            ChannelSettings settings;
            IDigitalFilter filter;

            lock (_settingsLock)
            {
                settings = _virtualChannelSettings;
                filter = _virtualFilter;
            }

            if (settings == null)
                return;

            for (int i = 0; i < count; i++)
            {
                double processed = settings.Gain * (data[i] + settings.Offset);

                if (filter != null)
                {
                    processed = filter.Filter(processed);
                }

                if (!double.IsFinite(processed))
                {
                    processed = 0.0;
                }

                data[i] = processed;
            }
        }

        public void clearData()
        {
            lock (_settingsLock)
            {
                if (_virtualFilter != null)
                {
                    _virtualFilter.Reset();
                }
            }
        }

        #endregion

        #region IChannelConfigurable Implementation

        public void SetChannelSetting(int channelIndex, ChannelSettings settings)
        {
            if (channelIndex != 0)
                return;

            lock (_settingsLock)
            {
                _virtualChannelSettings = settings;

                IDigitalFilter newFilter;
                if (settings != null)
                {
                    newFilter = settings.Filter;
                }
                else
                {
                    newFilter = null;
                }

                if (_virtualFilter != newFilter)
                {
                    _virtualFilter = newFilter;
                    if (_virtualFilter != null)
                    {
                        _virtualFilter.Reset();
                    }
                }
            }
        }

        public void UpdateChannelSettings(IReadOnlyList<ChannelSettings> channelSettings)
        {
            if (channelSettings == null || channelSettings.Count == 0)
                return;

            SetChannelSetting(0, channelSettings[0]);
        }

        public void ResetChannelFilters()
        {
            lock (_settingsLock)
            {
                if (_virtualFilter != null)
                {
                    _virtualFilter.Reset();
                }
            }
        }

        #endregion

        #region Helper Methods

        private string GetOperationSymbol()
        {
            return _operation switch
            {
                VirtualChannelOperationType.Add => "+",
                VirtualChannelOperationType.Subtract => "-",
                VirtualChannelOperationType.Multiply => "×",
                VirtualChannelOperationType.Divide => "÷",
                _ => "?"
            };
        }

        public string GetOperationDescription()
        {
            if (_sourceChannels.Count == 1)
            {
                string sourceName = _sourceChannels[0].Label;
                return $"Virtual copy of {sourceName}";
            }
            else
            {
                string source1Name = _sourceChannels[0].Label;
                string source2Name = _sourceChannels[1].Label;
                string opSymbol = GetOperationSymbol();
                return $"{source1Name} {opSymbol} {source2Name}";
            }
        }

        public IReadOnlyList<Channel> GetSourceChannels()
        {
            return _sourceChannels.AsReadOnly();
        }

        public Channel GetPrimarySourceChannel()
        {
            if (_sourceChannels.Count > 0)
            {
                return _sourceChannels[0];
            }
            return null;
        }

        public Channel GetParentChannelA()
        {
            if (_sourceChannels.Count > 0)
            {
                return _sourceChannels[0];
            }
            return null;
        }

        public Channel GetParentChannelB()
        {
            if (_sourceChannels.Count > 1)
            {
                return _sourceChannels[1];
            }
            return null;
        }

        public bool IsBinaryOperation
        {
            get { return _sourceChannels.Count == 2; }
        }

        public VirtualChannelOperationType? OperationType
        {
            get 
            { 
                if (IsBinaryOperation)
                    return _operation;
                return null;
            }
        }

        #endregion

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            Disposing?.Invoke(this, EventArgs.Empty);

            UnsubscribeFromSourceStreams();

            lock (_settingsLock)
            {
                if (_virtualFilter != null)
                {
                    _virtualFilter.Reset();
                }
                _virtualChannelSettings = null;
            }

            _disposed = true;
        }
    }
}
