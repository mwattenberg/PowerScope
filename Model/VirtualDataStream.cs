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
            foreach (IOperandSource operand in _sourceOperands)
            {
                if (!operand.IsConstant)
                {
                    if (operand.Channel != null)
                    {
                        if (operand.Channel.OwnerStream != null)
                        {
                            _sourceStreams.Add(operand.Channel.OwnerStream);
                        }
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
                    string sourceType;
                    if (_sourceOperands[0].IsConstant)
                    {
                        sourceType = "Constant";
                    }
                    else
                    {
                        sourceType = _sourceOperands[0].Channel.StreamType;
                    }
                    return $"Virtual ({sourceType})";
                }
                else
                {
                    // Show operation between source types
                    string source1Type;
                    if (_sourceOperands[0].IsConstant)
                    {
                        source1Type = "Constant";
                    }
                    else
                    {
                        source1Type = _sourceOperands[0].Channel.StreamType;
                    }

                    string source2Type;
                    if (_sourceOperands[1].IsConstant)
                    {
                        source2Type = "Constant";
                    }
                    else
                    {
                        source2Type = _sourceOperands[1].Channel.StreamType;
                    }

                    return $"Virtual ({source1Type} {GetOperationSymbol()} {source2Type})";
                }
            }
        }

        public bool IsConnected
        {
            get
            {
                // Virtual stream is "connected" if all channel operands are connected
                foreach (IOperandSource operand in _sourceOperands)
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
                foreach (IOperandSource operand in _sourceOperands)
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
                        double constantValue = _sourceOperands[0].ConstantValue;
                        int samplesToFill = Math.Min(n, destination.Length);
                        for (int i = 0; i < samplesToFill; i++)
                        {
                            destination[i] = constantValue;
                        }
                        actualSamples = samplesToFill;
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
        /// Computes binary operation between two source operands (channels and/or constants)
        /// Optimized to handle constant operands inline without buffer allocation
        /// </summary>
        private int ComputeBinaryOperation(double[] destination, int n)
        {
            // Ensure compute buffers are large enough
            int requiredSize = Math.Min(n, _computeBuffer1.Length);

            // Optimize for constants - detect which operands are constants
            bool operand1IsConstant = _sourceOperands[0].IsConstant;
            bool operand2IsConstant = _sourceOperands[1].IsConstant;

            // Get constant values (if applicable) and fetch channel data
            double constant1 = operand1IsConstant ? _sourceOperands[0].ConstantValue : 0.0;
            double constant2 = operand2IsConstant ? _sourceOperands[1].ConstantValue : 0.0;

            int samples1 = operand1IsConstant ? requiredSize : _sourceOperands[0].Channel.CopyLatestDataTo(_computeBuffer1, requiredSize);
            int samples2 = operand2IsConstant ? requiredSize : _sourceOperands[1].Channel.CopyLatestDataTo(_computeBuffer2, requiredSize);

            // Use the minimum sample count (both sources must have data)
            int actualSamples = Math.Min(samples1, samples2);
            if (actualSamples <= 0)
                return 0;

            // Perform the operation sample-by-sample with optimized paths for constants
            if (operand1IsConstant && operand2IsConstant)
            {
                // Both constant - compute once and fill entire destination
                double result = _operation switch
                {
                    VirtualChannelOperationType.Add => constant1 + constant2,
                    VirtualChannelOperationType.Subtract => constant1 - constant2,
                    VirtualChannelOperationType.Multiply => constant1 * constant2,
                    VirtualChannelOperationType.Divide => Math.Abs(constant2) > 1e-10 ? constant1 / constant2 : 0.0,
                    _ => constant1
                };

                if (!double.IsFinite(result))
                    result = 0.0;

                for (int i = 0; i < actualSamples; i++)
                    destination[i] = result;
            }
            else if (operand1IsConstant)
            {
                // Operand 1 is constant, operand 2 is channel - avoid buffer for constant1
                for (int i = 0; i < actualSamples; i++)
                {
                    double sample2 = _computeBuffer2[i];

                    destination[i] = _operation switch
                    {
                        VirtualChannelOperationType.Add => constant1 + sample2,
                        VirtualChannelOperationType.Subtract => constant1 - sample2,
                        VirtualChannelOperationType.Multiply => constant1 * sample2,
                        VirtualChannelOperationType.Divide => Math.Abs(sample2) > 1e-10 ? constant1 / sample2 : 0.0,
                        _ => constant1
                    };

                    if (!double.IsFinite(destination[i]))
                        destination[i] = 0.0;
                }
            }
            else if (operand2IsConstant)
            {
                // Operand 1 is channel, operand 2 is constant - avoid buffer for constant2
                for (int i = 0; i < actualSamples; i++)
                {
                    double sample1 = _computeBuffer1[i];

                    destination[i] = _operation switch
                    {
                        VirtualChannelOperationType.Add => sample1 + constant2,
                        VirtualChannelOperationType.Subtract => sample1 - constant2,
                        VirtualChannelOperationType.Multiply => sample1 * constant2,
                        VirtualChannelOperationType.Divide => Math.Abs(constant2) > 1e-10 ? sample1 / constant2 : 0.0,
                        _ => sample1
                    };

                    if (!double.IsFinite(destination[i]))
                        destination[i] = 0.0;
                }
            }
            else
            {
                // Both are channels - standard operation path
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
                        _ => sample1
                    };

                    if (!double.IsFinite(destination[i]))
                        destination[i] = 0.0;
                }
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
                string sourceName;
                if (_sourceOperands[0].IsConstant)
                {
                    sourceName = _sourceOperands[0].ConstantValue.ToString("G");
                }
                else
                {
                    sourceName = _sourceOperands[0].Channel.Label;
                }
                return $"Virtual copy of {sourceName}";
            }
            else
            {
                string source1Name;
                if (_sourceOperands[0].IsConstant)
                {
                    source1Name = _sourceOperands[0].ConstantValue.ToString("G");
                }
                else
                {
                    source1Name = _sourceOperands[0].Channel.Label;
                }

                string source2Name;
                if (_sourceOperands[1].IsConstant)
                {
                    source2Name = _sourceOperands[1].ConstantValue.ToString("G");
                }
                else
                {
                    source2Name = _sourceOperands[1].Channel.Label;
                }

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
            List<Channel> channels = new List<Channel>();
            foreach (IOperandSource operand in _sourceOperands)
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
