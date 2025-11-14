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
        private readonly List<IOperandSource> _sourceOperands;
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

            _sourceOperands = new List<IOperandSource> { new ChannelOperand(sourceChannel) };
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
        public VirtualDataStream(IOperandSource operandA, IOperandSource operandB, VirtualChannelOperationType operation)
        {
            if (operandA == null)
                throw new ArgumentNullException(nameof(operandA));
            if (operandB == null)
                throw new ArgumentNullException(nameof(operandB));

            _sourceOperands = new List<IOperandSource> { operandA, operandB };
            _operation = operation;

            // Track unique owner streams from channel operands
            _sourceStreams = new HashSet<IDataStream>();
            foreach (var operand in _sourceOperands)
            {
                if (!operand.IsConstant && operand.Channel?.OwnerStream != null)
                {
                    _sourceStreams.Add(operand.Channel.OwnerStream);
                }
            }

            InitializeComputeBuffers();
            SubscribeToSourceStreams();
        }

        /// <summary>
        /// Legacy constructor for backward compatibility - converts channels to operands
        /// </summary>
        [Obsolete("Use VirtualDataStream(IOperandSource, IOperandSource, VirtualChannelOperationType) instead")]
        public VirtualDataStream(Channel sourceChannel1, Channel sourceChannel2, VirtualChannelOperationType operation)
            : this(new ChannelOperand(sourceChannel1), new ChannelOperand(sourceChannel2), operation)
        {
        }

        /// <summary>
        /// Subscribe to Disposing events from all source streams
        /// When any source stream disposes, this virtual stream must dispose too
        /// </summary>
        private void SubscribeToSourceStreams()
        {
            foreach (var stream in _sourceStreams)
            {
                stream.Disposing += OnSourceStreamDisposing;
            }
        }

        /// <summary>
        /// Unsubscribe from all source stream Disposing events
        /// </summary>
        private void UnsubscribeFromSourceStreams()
        {
            foreach (var stream in _sourceStreams)
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
            // Pre-allocate compute buffers to avoid per-call allocations
            int maxBufferSize = 100000; // Reasonable max for temporary computation
            _computeBuffer1 = new double[maxBufferSize];
            _computeBuffer2 = new double[maxBufferSize];
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
                    string sourceType = _sourceOperands[0].IsConstant ? "Constant" : _sourceOperands[0].Channel.StreamType;
                    return $"Virtual ({sourceType})";
                }
                else
                {
                    // Show operation between source types
                    string source1Type = _sourceOperands[0].IsConstant ? "Constant" : _sourceOperands[0].Channel.StreamType;
                    string source2Type = _sourceOperands[1].IsConstant ? "Constant" : _sourceOperands[1].Channel.StreamType;
                    return $"Virtual ({source1Type} {GetOperationSymbol()} {source2Type})";
                }
            }
        }

        public bool IsConnected
        {
            get
            {
                // Virtual stream is "connected" if all channel operands are connected
                foreach (var operand in _sourceOperands)
                {
                    if (!operand.IsConstant && !operand.Channel.IsStreamConnected)
                        return false;
                }
                return true;
            }
        }

        public bool IsStreaming
        {
            get
            {
                // Virtual stream is "streaming" if any channel operand is streaming
                foreach (var operand in _sourceOperands)
                {
                    if (!operand.IsConstant && operand.Channel.IsStreamStreaming)
                        return true;
                }
                return false;
            }
        }

        public long TotalSamples
        {
            get
            {
                // Return total samples from first channel operand
                // (all sources should have same sample count for valid operations)
                for (int i = 0; i < _sourceOperands.Count; i++)
                {
                    if (!_sourceOperands[i].IsConstant && _sourceOperands[i].Channel.OwnerStream != null)
                        return _sourceOperands[i].Channel.OwnerStream.TotalSamples;
                }
                return 0;
            }
        }

        public long TotalBits
        {
            get
            {
                // Virtual streams don't generate new bits, return source bits from first channel operand
                for (int i = 0; i < _sourceOperands.Count; i++)
                {
                    if (!_sourceOperands[i].IsConstant && _sourceOperands[i].Channel.OwnerStream != null)
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
                // Use sample rate from first channel operand
                for (int i = 0; i < _sourceOperands.Count; i++)
                {
                    if (!_sourceOperands[i].IsConstant)
                    {
                        if (_sourceOperands[i].Channel.OwnerStream != null)
                            return _sourceOperands[i].Channel.OwnerStream.SampleRate;
                    }
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
        /// Reads from source operands (channels or constants), applies operation, applies virtual channel processing
        /// </summary>
        public int CopyLatestTo(int channel, double[] destination, int n)
        {
            if (channel != 0) // Virtual streams only have channel 0
                return 0;

            if (_disposed)
                return 0;

            if (destination == null || n <= 0)
                return 0;

            // Check if source channels are available
            if (!IsConnected || !IsStreaming)
                return 0;

            lock (_computeLock)
            {
                // Step 1: Get data from source operand(s)
                int actualSamples;

                if (_sourceOperands.Count == 1)
                {
                    // Single source: copy directly then apply virtual processing
                    if (_sourceOperands[0].IsConstant)
                    {
                        // Fill destination with constant value
                        actualSamples = FillWithConstant(destination, n, _sourceOperands[0].ConstantValue);
                    }
                    else
                    {
                        actualSamples = _sourceOperands[0].Channel.CopyLatestDataTo(destination, n);
                    }
                }
                else
                {
                    // Two operands: perform mathematical operation
                    actualSamples = ComputeBinaryOperation(destination, n);
                }

                if (actualSamples <= 0)
                    return 0;

                // Step 2: Apply virtual channel processing (gain, offset, filtering)
                ApplyVirtualChannelProcessing(destination, actualSamples);

                return actualSamples;
            }
        }

        /// <summary>
        /// Fills destination array with constant value
        /// </summary>
        private int FillWithConstant(double[] destination, int n, double value)
        {
            int samplesToFill = Math.Min(n, destination.Length);

            for (int i = 0; i < samplesToFill; i++)
            {
                destination[i] = value;
            }

            return samplesToFill;
        }

        /// <summary>
        /// Computes binary operation between two source operands (channels and/or constants)
        /// </summary>
        private int ComputeBinaryOperation(double[] destination, int n)
        {
            // Ensure compute buffers are large enough
            int requiredSize = Math.Min(n, _computeBuffer1.Length);

            // Get data from both operands
            int samples1 = GetOperandData(_sourceOperands[0], _computeBuffer1, requiredSize);
            int samples2 = GetOperandData(_sourceOperands[1], _computeBuffer2, requiredSize);

            // Use the minimum sample count (both sources must have data)
            int actualSamples = Math.Min(samples1, samples2);
            if (actualSamples <= 0)
                return 0;

            // Perform the operation sample-by-sample
            for (int i = 0; i < actualSamples; i++)
            {
                double sample1 = _computeBuffer1[i];
                double sample2 = _computeBuffer2[i];

                destination[i] = _operation switch
                {
                    VirtualChannelOperationType.Add => sample1 + sample2,
                    VirtualChannelOperationType.Subtract => sample1 - sample2,
                    VirtualChannelOperationType.Multiply => sample1 * sample2,
                    VirtualChannelOperationType.Divide => Math.Abs(sample2) > 1e-10 ? sample1 / sample2 : 0.0,
                    _ => sample1 // Fallback to copy
                };

                // Safety check for invalid values
                if (!double.IsFinite(destination[i]))
                {
                    destination[i] = 0.0;
                }
            }

            return actualSamples;
        }

        /// <summary>
        /// Gets data from an operand source (channel or constant)
        /// </summary>
        private int GetOperandData(IOperandSource operand, double[] destination, int n)
        {
            if (operand.IsConstant)
            {
                return FillWithConstant(destination, n, operand.ConstantValue);
            }
            else
            {
                return operand.Channel.CopyLatestDataTo(destination, n);
            }
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
                _virtualFilter?.Reset();
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
                var newFilter = settings?.Filter;
                if (_virtualFilter != newFilter)
                {
                    _virtualFilter = newFilter;
                    _virtualFilter?.Reset();
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
                _virtualFilter?.Reset();
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
                string sourceName = _sourceOperands[0].IsConstant
             ? _sourceOperands[0].ConstantValue.ToString("G")
                    : _sourceOperands[0].Channel.Label;
                return $"Virtual copy of {sourceName}";
            }
            else
            {
                string source1Name = _sourceOperands[0].IsConstant
                       ? _sourceOperands[0].ConstantValue.ToString("G")
             : _sourceOperands[0].Channel.Label;
                string source2Name = _sourceOperands[1].IsConstant
               ? _sourceOperands[1].ConstantValue.ToString("G")
    : _sourceOperands[1].Channel.Label;
                string opSymbol = GetOperationSymbol();
                return $"{source1Name} {opSymbol} {source2Name}";
            }
        }

        /// <summary>
        /// Gets all source channels (useful for UI display and dependency tracking)
        /// Returns only channel operands, not constant operands
        /// </summary>
        public IReadOnlyList<Channel> GetSourceChannels()
        {
            var channels = new List<Channel>();
            foreach (var operand in _sourceOperands)
            {
                if (!operand.IsConstant && operand.Channel != null)
                {
                    channels.Add(operand.Channel);
                }
            }
            return channels.AsReadOnly();
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
                _virtualFilter?.Reset();
                _virtualChannelSettings = null;
            }

            _disposed = true;
        }
    }
}
