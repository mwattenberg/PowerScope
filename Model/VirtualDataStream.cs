using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace PowerScope.Model
{
    /// <summary>
    /// Represents a virtual data stream that performs operations on existing channel(s)
    /// WITHOUT creating duplicate ring buffers - computes on-demand when data is requested
    /// Supports mathematical operations (add, subtract, multiply, divide) and filtering
    /// Can operate on physical channels OR other virtual channels (chaining supported)
    /// Also supports constant values as operands
    /// </summary>
    public class VirtualDataStream : IDataStream, IChannelConfigurable
    {
        private readonly List<IVirtualSource> _sourceOperands;
        private readonly HashSet<IDataStream> _sourceStreams; // Track unique source streams
        private readonly VirtualChannelOperationType _operation;
        private bool _disposed = false;

        // Virtual channel settings (gain, offset, filtering)
        private ChannelSettings _virtualChannelSettings;
        private IDigitalFilter _virtualFilter;
        private readonly object _settingsLock = new object();

        // Temporary buffers for computation (reused to avoid allocations)
        private double[] _computeBuffer1;
        private double[] _computeBuffer2;
        private int _allocatedSize = 0;  // Track currently allocated size
        private readonly object _computeLock = new object();

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Raised when this virtual data stream is being disposed
        /// Allows cascading disposal to dependent virtual channels
        /// </summary>
        public event EventHandler Disposing;

        /// <summary>
        /// Creates a virtual data stream from a single source channel (for filtering/transformation)
        /// </summary>
        public VirtualDataStream(Channel sourceChannel)
        {
            if (sourceChannel == null)
                throw new ArgumentNullException(nameof(sourceChannel));

            _sourceOperands = new List<IVirtualSource> { new ChannelOperand(sourceChannel) };
            _operation = VirtualChannelOperationType.Add; // Default, not used for single source

            // Track unique owner streams
            _sourceStreams = new HashSet<IDataStream>();
            if (sourceChannel.OwnerStream != null)
            {
                _sourceStreams.Add(sourceChannel.OwnerStream);
            }

            InitializeComputeBuffers();
            SubscribeToSourceStreams();
        }

        /// <summary>
        /// Creates a virtual data stream from two source operands (channels or constants) with a mathematical operation
        /// </summary>
        public VirtualDataStream(IVirtualSource operandA, IVirtualSource operandB, VirtualChannelOperationType operation)
        {
            if (operandA == null)
                throw new ArgumentNullException(nameof(operandA));
            if (operandB == null)
                throw new ArgumentNullException(nameof(operandB));

            _sourceOperands = new List<IVirtualSource> { operandA, operandB };
            _operation = operation;

            _sourceStreams = new HashSet<IDataStream>();
            foreach (IVirtualSource operand in _sourceOperands)
            {
                if (operand.Channel != null)
                {
                    if (operand.Channel.OwnerStream != null)
                    {
                        _sourceStreams.Add(operand.Channel.OwnerStream);
                    }
                }
            }

            InitializeComputeBuffers();
            SubscribeToSourceStreams();
        }

        /// <summary>
        /// Subscribe to Disposing events from all source streams
        /// When any source stream disposes, this virtual stream must dispose too
        /// </summary>
        private void SubscribeToSourceStreams()
        {
            foreach (IDataStream stream in _sourceStreams)
            {
                stream.Disposing += OnSourceStreamDisposing;
            }
        }

        /// <summary>
        /// Unsubscribe from all source stream Disposing events
        /// </summary>
        private void UnsubscribeFromSourceStreams()
        {
            foreach (IDataStream stream in _sourceStreams)
            {
                stream.Disposing -= OnSourceStreamDisposing;
            }
        }

        /// <summary>
        /// Called when any source stream is being disposed
        /// This virtual stream must dispose itself to prevent accessing dead streams
        /// </summary>
        private void OnSourceStreamDisposing(object sender, EventArgs e)
        {
            // Source stream is dying - we must dispose ourselves
            Dispose();
        }

        private void InitializeComputeBuffers()
        {
            // Start with a small initial allocation - grow on demand
            int initialSize = 10000;
            _computeBuffer1 = new double[initialSize];
            _computeBuffer2 = new double[initialSize];
            _allocatedSize = initialSize;
        }

        /// <summary>
        /// Ensures compute buffers are large enough for the requested size
        /// Grows buffers lazily only when needed, avoiding unnecessary allocations
        /// Maximum allocation is capped at 50 million samples to prevent excessive memory use
        /// </summary>
        private void EnsureComputeBuffersAllocated(int requiredSize)
        {
            // Cap maximum size at 50 million samples
            int maxAllowedSize = 50_000_000;
            int clampedSize = Math.Min(requiredSize, maxAllowedSize);

            // Check if buffers are already large enough
            if (clampedSize <= _allocatedSize)
                return;

            // Allocate with 50% overhead to amortize allocation costs
            // This minimizes reallocations while being memory-conscious
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
                // Virtual stream status depends on source channels
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
                if (_sourceOperands.Count == 1)
                {
                    string sourceType = _sourceOperands[0].Channel.StreamType;
                    return $"Virtual ({sourceType})";
                }
                else
                {
                    string source1Type = _sourceOperands[0].Channel.StreamType;
                    string source2Type = _sourceOperands[1].Channel.StreamType;
                    return $"Virtual ({source1Type} {GetOperationSymbol()} {source2Type})";
                }
            }
        }

        public bool IsConnected
        {
            get
            {
                foreach (IVirtualSource operand in _sourceOperands)
                {
                    if (!operand.Channel.IsStreamConnected)
                        return false;
                }
                return true;
            }
        }

        public bool IsStreaming
        {
            get
            {
                foreach (IVirtualSource operand in _sourceOperands)
                {
                    if (operand.Channel.IsStreamStreaming)
                        return true;
                }
                return false;
            }
        }

        public long TotalSamples
        {
            get
            {
                for (int i = 0; i < _sourceOperands.Count; i++)
                {
                    if (_sourceOperands[i].Channel.OwnerStream != null)
                        return _sourceOperands[i].Channel.OwnerStream.TotalSamples;
                }
                return 0;
            }
        }

        public long TotalBits
        {
            get
            {
                for (int i = 0; i < _sourceOperands.Count; i++)
                {
                    if (_sourceOperands[i].Channel.OwnerStream != null)
                        return _sourceOperands[i].Channel.OwnerStream.TotalBits;
                }
                return 0;
            }
        }

        public int ChannelCount
        {
            get
            {
                // Virtual streams always have 1 output channel
                return 1;
            }
        }

        public double SampleRate
        {
            get
            {
                for (int i = 0; i < _sourceOperands.Count; i++)
                {
                    if (_sourceOperands[i].Channel.OwnerStream != null)
                        return _sourceOperands[i].Channel.OwnerStream.SampleRate;
                }
                return 1000.0; // Default fallback
            }
        }

        public void Connect()
        {
            // Virtual streams don't connect - they depend on source channels
            // No-op
        }

        public void Disconnect()
        {
            // Virtual streams don't disconnect
            // No-op
        }

        public void StartStreaming()
        {
            // Virtual streams don't start streaming - they follow source channels
            // No-op
        }

        public void StopStreaming()
        {
            // Virtual streams don't stop streaming
            // No-op
        }

        /// <summary>
        /// The core method: computes virtual channel data ON-DEMAND without storing in ring buffer
        /// Reads from source operands, applies operation, applies virtual channel processing
        /// </summary>
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

                if (_sourceOperands.Count == 1)
                {
                    actualSamples = _sourceOperands[0].Channel.CopyLatestDataTo(destination, n);
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

        /// <summary>
        /// Computes binary operation between two source operands
        /// All operands are now channels (constants wrapped in ConstantDataStream)
        /// </summary>
        private int ComputeBinaryOperation(double[] destination, int n)
        {
            EnsureComputeBuffersAllocated(n);

            int samples1 = _sourceOperands[0].Channel.CopyLatestDataTo(_computeBuffer1, n);
            int samples2 = _sourceOperands[1].Channel.CopyLatestDataTo(_computeBuffer2, n);

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

        /// <summary>
        /// Applies virtual channel processing: gain, offset, and filtering
        /// This allows filtering on top of already-filtered source channels!
        /// </summary>
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

            // Apply gain and offset, then filtering
            for (int i = 0; i < count; i++)
            {
                // Apply gain and offset
                double processed = settings.Gain * (data[i] + settings.Offset);

                // Apply filter if configured
                if (filter != null)
                {
                    processed = filter.Filter(processed);
                }

                // Safety check
                if (!double.IsFinite(processed))
                {
                    processed = 0.0;
                }

                data[i] = processed;
            }
        }

        public void clearData()
        {
            // Virtual streams don't own data - clear filters only
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
            if (channelIndex != 0) // Virtual streams only have channel 0
                return;

            lock (_settingsLock)
            {
                _virtualChannelSettings = settings;

                // Update filter reference and reset if filter type changed
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

        /// <summary>
        /// Gets description of the virtual channel operation
        /// </summary>
        public string GetOperationDescription()
        {
            if (_sourceOperands.Count == 1)
            {
                string sourceName = _sourceOperands[0].Channel.Label;
                return $"Virtual copy of {sourceName}";
            }
            else
            {
                string source1Name = _sourceOperands[0].Channel.Label;
                string source2Name = _sourceOperands[1].Channel.Label;
                string opSymbol = GetOperationSymbol();
                return $"{source1Name} {opSymbol} {source2Name}";
            }
        }

        /// <summary>
        /// Gets all source channels (useful for UI display and dependency tracking)
        /// </summary>
        public IReadOnlyList<Channel> GetSourceChannels()
        {
            List<Channel> channels = new List<Channel>();
            foreach (IVirtualSource operand in _sourceOperands)
            {
                if (operand.Channel != null)
                {
                    channels.Add(operand.Channel);
                }
            }
            return channels.AsReadOnly();
        }

        /// <summary>
        /// Gets the primary (first) source channel for this virtual stream
        /// Useful for inheriting color and display properties from the source
        /// </summary>
        public Channel GetPrimarySourceChannel()
        {
            if (_sourceOperands.Count > 0 && _sourceOperands[0].Channel != null)
            {
                return _sourceOperands[0].Channel;
            }
            return null;
        }

        /// <summary>
        /// Gets the first parent channel (Parent A) for this virtual stream
        /// </summary>
        public Channel GetParentChannelA()
        {
            if (_sourceOperands.Count > 0 && _sourceOperands[0].Channel != null)
            {
                return _sourceOperands[0].Channel;
            }
            return null;
        }

        /// <summary>
        /// Gets the second parent channel (Parent B) for this virtual stream
        /// Returns null if no second operand exists or single-source virtual
        /// </summary>
        public Channel GetParentChannelB()
        {
            if (_sourceOperands.Count > 1 && _sourceOperands[1].Channel != null)
            {
                return _sourceOperands[1].Channel;
            }
            return null;
        }

        /// <summary>
        /// Checks if this virtual channel has two parents (binary operation)
        /// </summary>
        public bool IsBinaryOperation
        {
            get { return _sourceOperands.Count == 2; }
        }

        /// <summary>
        /// Gets the operation type for binary operations
        /// Returns null for single-source virtual channels
        /// </summary>
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

            // Notify dependents FIRST - this allows cascading disposal
            Disposing?.Invoke(this, EventArgs.Empty);

            // Unsubscribe from source streams
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
