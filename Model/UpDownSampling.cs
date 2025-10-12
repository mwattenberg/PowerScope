using System;
using System.ComponentModel;

namespace PowerScope.Model
{
    /// <summary>
    /// Comprehensive up/down sampling processor that handles both interpolation and decimation
    /// Supports factors from 10^-9 to 10^+9 for extreme sampling rate changes
    /// </summary>
    public class UpDownSampling : INotifyPropertyChanged
    {
        private int _samplingFactor = 0;
        private double[] _sincKernel;
        private const int SincKernelHalfLength = 32; // Half-length of the sinc kernel
        
        // Upsampling state for continuous processing (to eliminate discontinuities)
        private class ChannelUpsamplingState
        {
            public double[] PreviousTail { get; set; }      // Tail from two samples ago
            public double[] PreviousBody { get; set; }     // Body from last sample (becomes tail)
            public int TailLength { get; set; }
            public int BodyLength { get; set; }
            public bool IsFirstBlock { get; set; }

            public ChannelUpsamplingState(int tailLength)
            {
                TailLength = tailLength;
                BodyLength = tailLength; // Body length same as tail for smooth transitions
                PreviousTail = new double[tailLength];
                PreviousBody = new double[BodyLength];
                Array.Clear(PreviousTail);
                Array.Clear(PreviousBody);
                IsFirstBlock = true;
            }
        }
        
        // Downsampling state for each channel
        private class ChannelDownsamplingState
        {
            public double[] DelayLine { get; set; }
            public int SampleCounter { get; set; }
            public int DelayLineIndex { get; set; }

            public ChannelDownsamplingState(int delayLineLength)
            {
                DelayLine = new double[delayLineLength];
                SampleCounter = 0;
                DelayLineIndex = 0;
            }
        }

        private ChannelUpsamplingState[] _upsamplingStates;
        private ChannelDownsamplingState[] _downsamplingStates;
        private readonly object _processLock = new object();

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Gets or sets the up/down sampling factor
        /// Positive values: upsampling by zero-padding and interpolation (+1 = 2x, +2 = 3x, etc.)
        /// Negative values: downsampling by sample skipping (-1 = keep every 2nd sample, -2 = keep every 3rd sample, etc.)
        /// 0: no change (bypass)
        /// Range: -9 to +9
        /// </summary>
        public int SamplingFactor
        {
            get { return _samplingFactor; }
            set
            {
                var clampedValue = Math.Max(-9, Math.Min(9, value));
                if (_samplingFactor != clampedValue)
                {
                    _samplingFactor = clampedValue;
                    OnPropertyChanged(nameof(SamplingFactor));
                    OnPropertyChanged(nameof(SampleRateMultiplier));
                    OnPropertyChanged(nameof(IsEnabled));

                    // Regenerate sinc kernel for upsampling
                    if (_samplingFactor > 0)
                    {
                        GenerateSincKernel();
                    }
                }
            }
        }

        /// <summary>
        /// Gets the actual sample rate multiplier based on the sampling factor
        /// For positive factors: multiplier = factor + 1 (e.g., +1 = 2x, +2 = 3x)
        /// For negative factors: multiplier = 1 / (|factor| + 1) (e.g., -1 = 1/2, -2 = 1/3)
        /// </summary>
        public double SampleRateMultiplier
        {
            get
            {
                if (_samplingFactor == 0) return 1.0;
                if (_samplingFactor > 0) return _samplingFactor + 1.0; // +1 = 2x, +2 = 3x, etc.
                else return 1.0 / (Math.Abs(_samplingFactor) + 1.0); // -1 = 1/2, -2 = 1/3, etc.
            }
        }

        /// <summary>
        /// Gets whether up/down sampling is currently enabled (factor != 0)
        /// </summary>
        public bool IsEnabled
        {
            get { return _samplingFactor != 0; }
        }

        /// <summary>
        /// Gets whether upsampling is active (factor > 0)
        /// </summary>
        public bool IsUpsampling
        {
            get { return _samplingFactor > 0; }
        }

        /// <summary>
        /// Gets whether downsampling is active (factor < 0)
        /// </summary>
        public bool IsDownsampling
        {
            get { return _samplingFactor < 0; }
        }

        /// <summary>
        /// Gets the interpolation factor for upsampling (factor + 1)
        /// </summary>
        public int InterpolationFactor
        {
            get
            {
                if (_samplingFactor <= 0) return 1;
                return _samplingFactor + 1; // +1 = 2x, +2 = 3x, etc.
            }
        }

        /// <summary>
        /// Gets the decimation factor for downsampling (|factor| + 1)
        /// </summary>
        public int DecimationFactor
        {
            get
            {
                if (_samplingFactor >= 0) return 1;
                return Math.Abs(_samplingFactor) + 1; // -1 = every 2nd, -2 = every 3rd, etc.
            }
        }

        public UpDownSampling()
        {
            GenerateSincKernel();
        }

        /// <summary>
        /// Initialize channel states for up/down sampling
        /// Must be called before processing data when using up/down sampling
        /// </summary>
        /// <param name="numberOfChannels">Number of channels to initialize</param>
        public void InitializeChannels(int numberOfChannels)
        {
            lock (_processLock)
            {
                if (_samplingFactor > 0) // Upsampling
                {
                    // For upsampling, we need to keep a tail of previous data to ensure continuity
                    // Use kernel half length + 1 to ensure proper overlap for all kernel coefficients
                    int tailLength = SincKernelHalfLength + 1; // Keep enough samples for smooth transition
                    _upsamplingStates = new ChannelUpsamplingState[numberOfChannels];
                    for (int i = 0; i < numberOfChannels; i++)
                    {
                        _upsamplingStates[i] = new ChannelUpsamplingState(tailLength);
                    }
                    _downsamplingStates = null;
                }
                else if (_samplingFactor < 0) // Downsampling
                {
                    int delayLineLength = SincKernelHalfLength * 2 + 1;
                    _downsamplingStates = new ChannelDownsamplingState[numberOfChannels];
                    for (int i = 0; i < numberOfChannels; i++)
                    {
                        _downsamplingStates[i] = new ChannelDownsamplingState(delayLineLength);
                    }
                    _upsamplingStates = null;
                }
                else // No sampling change
                {
                    _upsamplingStates = null;
                    _downsamplingStates = null;
                }
            }
        }

        /// <summary>
        /// Process data through up/down sampling
        /// </summary>
        /// <param name="inputData">Input sample arrays for each channel</param>
        /// <returns>Processed sample arrays for each channel</returns>
        public double[][] ProcessData(double[][] inputData)
        {
            if (!IsEnabled || inputData == null || inputData.Length == 0)
            {
                return inputData; // Bypass processing
            }

            lock (_processLock)
            {
                if (IsUpsampling)
                {
                    return ProcessUpsampling(inputData);
                }
                else if (IsDownsampling)
                {
                    return ProcessDownsampling(inputData);
                }
                else
                {
                    return inputData;
                }
            }
        }

        /// <summary>
        /// Process a single channel of data through up/down sampling
        /// </summary>
        /// <param name="channelIndex">Channel index (0-based)</param>
        /// <param name="inputData">Input samples for the channel</param>
        /// <returns>Processed samples for the channel</returns>
        public double[] ProcessChannelData(int channelIndex, double[] inputData)
        {
            if (!IsEnabled || inputData == null || inputData.Length == 0)
            {
                return inputData; // Bypass processing
            }

            lock (_processLock)
            {
                if (IsUpsampling)
                {
                    // Initialize upsampling states if not already done
                    if (_upsamplingStates == null)
                    {
                        InitializeChannels(Math.Max(1, channelIndex + 1));
                    }
                    
                    var state = (channelIndex < _upsamplingStates.Length) ? _upsamplingStates[channelIndex] : null;
                    return SincInterpolationContinuous(inputData, InterpolationFactor, state);
                }
                else if (IsDownsampling)
                {
                    if (_downsamplingStates != null && channelIndex < _downsamplingStates.Length)
                    {
                        return ProcessDownsamplingChannel(inputData, _downsamplingStates[channelIndex]);
                    }
                    else
                    {
                        // Fallback: simple decimation without anti-aliasing
                        return SimpleDecimation(inputData, DecimationFactor);
                    }
                }
                else
                {
                    return inputData;
                }
            }
        }

        #region Upsampling (Interpolation)

        /// <summary>
        /// Process upsampling for all channels
        /// </summary>
        private double[][] ProcessUpsampling(double[][] inputData)
        {
            int channelCount = inputData.Length;
            double[][] outputData = new double[channelCount][];
            int factor = InterpolationFactor;

            // Initialize upsampling states if not already done
            if (_upsamplingStates == null)
            {
                InitializeChannels(channelCount);
            }

            for (int channel = 0; channel < channelCount; channel++)
            {
                if (inputData[channel] != null && inputData[channel].Length > 0)
                {
                    outputData[channel] = SincInterpolationContinuous(inputData[channel], factor, 
                        channel < _upsamplingStates.Length ? _upsamplingStates[channel] : null);
                }
                else
                {
                    outputData[channel] = inputData[channel];
                }
            }

            return outputData;
        }

        /// <summary>
        /// Generates a windowed sinc kernel for interpolation
        /// Uses a Hamming window to reduce ringing artifacts
        /// </summary>
        private void GenerateSincKernel()
        {
            int kernelLength = 2 * SincKernelHalfLength;
            _sincKernel = new double[kernelLength];

            // Use cutoff frequency based on the actual interpolation factor
            int actualFactor = InterpolationFactor;
            double cutoffFrequency = 0.4 / actualFactor; // Conservative cutoff to prevent aliasing

            for (int i = 0; i < kernelLength; i++)
            {
                int n = i - SincKernelHalfLength; // Center the kernel

                double sincValue;
                if (n == 0)
                {
                    sincValue = 2.0 * cutoffFrequency;
                }
                else
                {
                    double x = 2.0 * Math.PI * cutoffFrequency * n;
                    sincValue = Math.Sin(x) / (Math.PI * n);
                }

                // Apply Hamming window to reduce ringing
                double window = 0.54 - 0.46 * Math.Cos(2.0 * Math.PI * i / (kernelLength - 1));
                _sincKernel[i] = sincValue * window;
            }

            // Normalize the kernel to ensure unity gain at DC
            double kernelSum = 0.0;
            for (int i = 0; i < kernelLength; i++)
            {
                kernelSum += _sincKernel[i];
            }

            if (Math.Abs(kernelSum) > 1e-10) // Avoid division by zero
            {
                for (int i = 0; i < kernelLength; i++)
                {
                    _sincKernel[i] /= kernelSum;
                }
            }
        }

        /// <summary>
        /// Applies sinc interpolation to input data with improved continuity
        /// Uses a three-buffer system: tail (from two samples ago) + body (from last sample) + head (from current sample)
        /// This eliminates all discontinuities by properly overlapping processing blocks
        /// </summary>
        /// <param name="inputData">Input samples to interpolate</param>
        /// <param name="factor">Interpolation factor</param>
        /// <param name="state">Upsampling state for the channel</param>
        /// <returns>Interpolated samples (factor x longer than input)</returns>
        private double[] SincInterpolationContinuous(double[] inputData, int factor, ChannelUpsamplingState state)
        {
            if (inputData == null || inputData.Length == 0 || _sincKernel == null || factor <= 1)
                return inputData;

            int inputLength = inputData.Length;
            int outputLength = inputLength * factor;
            
            // Handle first block case (no previous data)
            if (state == null || state.IsFirstBlock)
            {
                if (state != null)
                {
                    // Store the body for next iteration (take the tail portion of current input)
                    int bodyStartIndex = Math.Max(0, inputLength - state.BodyLength);
                    int actualBodyLength = Math.Min(state.BodyLength, inputLength);
                    Array.Copy(inputData, bodyStartIndex, state.PreviousBody, 0, actualBodyLength);
                    
                    // Clear any remaining body if input is shorter than body length
                    if (actualBodyLength < state.BodyLength)
                    {
                        Array.Clear(state.PreviousBody, actualBodyLength, state.BodyLength - actualBodyLength);
                    }
                    
                    state.IsFirstBlock = false;
                }
                
                // For first block, just do simple interpolation with padding
                return SimpleInterpolationWithPadding(inputData, factor);
            }

            // Prepare the three-buffer system: tail + body + head
            int headLength = Math.Min(state.TailLength, inputLength); // Take head from current input
            int tailLength = state.TailLength;
            int bodyLength = state.BodyLength;
            
            // Calculate total processing length
            int totalProcessingLength = tailLength + bodyLength + headLength;
            double[] processingBuffer = new double[totalProcessingLength];
            
            // Fill processing buffer: tail + body + head
            int bufferOffset = 0;
            
            // 1. Copy tail (from two samples ago)
            Array.Copy(state.PreviousTail, 0, processingBuffer, bufferOffset, tailLength);
            bufferOffset += tailLength;
            
            // 2. Copy body (from last sample)
            Array.Copy(state.PreviousBody, 0, processingBuffer, bufferOffset, bodyLength);
            bufferOffset += bodyLength;
            
            // 3. Copy head (from current sample)
            Array.Copy(inputData, 0, processingBuffer, bufferOffset, headLength);
            
            // Create zero-padded upsampled signal
            int upsampledLength = totalProcessingLength * factor;
            double[] upsampled = new double[upsampledLength];
            
            // Insert original samples at zero-padded positions
            for (int i = 0; i < totalProcessingLength; i++)
            {
                upsampled[i * factor] = processingBuffer[i] * factor; // Scale by factor to maintain energy
            }
            
            // Apply sinc interpolation filter
            double[] filtered = new double[upsampledLength];
            int kernelHalfLength = SincKernelHalfLength;
            
            for (int i = 0; i < upsampledLength; i++)
            {
                double sum = 0.0;
                
                for (int j = 0; j < _sincKernel.Length; j++)
                {
                    int sampleIndex = i - kernelHalfLength + j;
                    
                    if (sampleIndex >= 0 && sampleIndex < upsampledLength)
                    {
                        sum += upsampled[sampleIndex] * _sincKernel[j];
                    }
                }
                
                filtered[i] = sum;
            }
            
            // Extract the output portion (skip tail and body, take only the interpolated current data)
            int skipSamples = (tailLength + bodyLength) * factor;
            int extractLength = Math.Min(outputLength, filtered.Length - skipSamples);
            
            double[] output = new double[outputLength];
            if (extractLength > 0)
            {
                Array.Copy(filtered, skipSamples, output, 0, extractLength);
            }
            
            // If we need more samples (input was longer than head), process the remainder with simple interpolation
            if (extractLength < outputLength)
            {
                int remainingInputStart = headLength;
                int remainingInputLength = inputLength - headLength;
                
                if (remainingInputLength > 0)
                {
                    double[] remainingInput = new double[remainingInputLength];
                    Array.Copy(inputData, remainingInputStart, remainingInput, 0, remainingInputLength);
                    
                    double[] remainingOutput = SimpleInterpolationWithPadding(remainingInput, factor);
                    int remainingOutputLength = Math.Min(remainingOutput.Length, outputLength - extractLength);
                    
                    Array.Copy(remainingOutput, 0, output, extractLength, remainingOutputLength);
                }
            }
            
            // Update state for next iteration
            // Current body becomes the tail for next time
            Array.Copy(state.PreviousBody, 0, state.PreviousTail, 0, state.TailLength);
            
            // Extract new body from current input (tail portion becomes new body)
            int newBodyStartIndex = Math.Max(0, inputLength - state.BodyLength);
            int actualNewBodyLength = Math.Min(state.BodyLength, inputLength);
            Array.Clear(state.PreviousBody, 0, state.BodyLength); // Clear first
            Array.Copy(inputData, newBodyStartIndex, state.PreviousBody, 0, actualNewBodyLength);
            
            return output;
        }
        
        /// <summary>
        /// Simple interpolation with mirrored padding for edge cases
        /// Used for first block and remainder processing
        /// </summary>
        /// <param name="inputData">Input data to interpolate</param>
        /// <param name="factor">Interpolation factor</param>
        /// <returns>Interpolated data</returns>
        private double[] SimpleInterpolationWithPadding(double[] inputData, int factor)
        {
            if (inputData == null || inputData.Length == 0)
                return inputData;
                
            int inputLength = inputData.Length;
            int outputLength = inputLength * factor;
            
            // Create padded input (mirror boundaries)
            int padLength = SincKernelHalfLength;
            double[] paddedInput = new double[inputLength + 2 * padLength];
            
            // Mirror boundary conditions
            for (int i = 0; i < padLength; i++)
            {
                paddedInput[i] = inputData[Math.Min(padLength - i - 1, inputLength - 1)]; // Left boundary
                paddedInput[inputLength + padLength + i] = inputData[Math.Max(0, inputLength - i - 1)]; // Right boundary
            }
            
            // Copy original data
            Array.Copy(inputData, 0, paddedInput, padLength, inputLength);
            
            // Zero-pad and upsample
            int paddedOutputLength = paddedInput.Length * factor;
            double[] upsampled = new double[paddedOutputLength];
            
            for (int i = 0; i < paddedInput.Length; i++)
            {
                upsampled[i * factor] = paddedInput[i] * factor;
            }
            
            // Apply sinc filter
            double[] filtered = new double[paddedOutputLength];
            int kernelHalfLength = SincKernelHalfLength;
            
            for (int i = 0; i < paddedOutputLength; i++)
            {
                double sum = 0.0;
                
                for (int j = 0; j < _sincKernel.Length; j++)
                {
                    int sampleIndex = i - kernelHalfLength + j;
                    
                    if (sampleIndex >= 0 && sampleIndex < paddedOutputLength)
                    {
                        sum += upsampled[sampleIndex] * _sincKernel[j];
                    }
                }
                
                filtered[i] = sum;
            }
            
            // Extract unpadded portion
            int skipSamples = padLength * factor;
            double[] output = new double[outputLength];
            Array.Copy(filtered, skipSamples, output, 0, outputLength);
            
            return output;
        }

        #endregion

        #region Downsampling (Decimation)

        /// <summary>
        /// Process downsampling for all channels
        /// </summary>
        private double[][] ProcessDownsampling(double[][] inputData)
        {
            int channelCount = inputData.Length;
            double[][] outputData = new double[channelCount][];

            // Initialize channel states if not already done
            if (_downsamplingStates == null)
            {
                InitializeChannels(channelCount);
            }

            for (int channel = 0; channel < channelCount; channel++)
            {
                if (inputData[channel] != null && inputData[channel].Length > 0 && 
                    _downsamplingStates != null && channel < _downsamplingStates.Length)
                {
                    outputData[channel] = ProcessDownsamplingChannel(inputData[channel], _downsamplingStates[channel]);
                }
                else
                {
                    // Fallback: simple decimation
                    outputData[channel] = SimpleDecimation(inputData[channel], DecimationFactor);
                }
            }

            return outputData;
        }

        /// <summary>
        /// Process downsampling for a single channel with anti-aliasing filter
        /// </summary>
        private double[] ProcessDownsamplingChannel(double[] inputData, ChannelDownsamplingState channelState)
        {
            if (inputData == null || inputData.Length == 0)
                return inputData;

            int decimationFactor = DecimationFactor;
            var outputList = new System.Collections.Generic.List<double>();

            foreach (double sample in inputData)
            {
                // Add sample to delay line (circular buffer)
                channelState.DelayLine[channelState.DelayLineIndex] = sample;
                channelState.DelayLineIndex = (channelState.DelayLineIndex + 1) % channelState.DelayLine.Length;

                // Check if we should output a sample (every N samples where N is decimation factor)
                channelState.SampleCounter++;
                if (channelState.SampleCounter >= decimationFactor)
                {
                    channelState.SampleCounter = 0;

                    // Apply anti-aliasing filter (low-pass filter using sinc kernel)
                    double filteredSample = ApplyAntiAliasingFilter(channelState);
                    outputList.Add(filteredSample);
                }
            }

            return outputList.ToArray();
        }

        /// <summary>
        /// Apply anti-aliasing filter using the sinc kernel
        /// </summary>
        private double ApplyAntiAliasingFilter(ChannelDownsamplingState channelState)
        {
            double sum = 0.0;
            int kernelLength = _sincKernel.Length;
            int delayLineLength = channelState.DelayLine.Length;

            for (int i = 0; i < kernelLength; i++)
            {
                // Calculate delay line index (wrap around)
                int delayIndex = (channelState.DelayLineIndex - SincKernelHalfLength + i + delayLineLength) % delayLineLength;
                sum += channelState.DelayLine[delayIndex] * _sincKernel[i];
            }

            return sum;
        }

        /// <summary>
        /// Simple decimation without anti-aliasing (fallback method)
        /// </summary>
        private double[] SimpleDecimation(double[] inputData, int decimationFactor)
        {
            if (inputData == null || inputData.Length == 0 || decimationFactor <= 1)
                return inputData;

            int outputLength = inputData.Length / decimationFactor;
            double[] output = new double[outputLength];

            for (int i = 0; i < outputLength; i++)
            {
                output[i] = inputData[i * decimationFactor];
            }

            return output;
        }

        #endregion

        /// <summary>
        /// Reset all channel states (useful when starting new data stream)
        /// </summary>
        public void Reset()
        {
            lock (_processLock)
            {
                if (_downsamplingStates != null)
                {
                    foreach (var state in _downsamplingStates)
                    {
                        if (state?.DelayLine != null)
                        {
                            Array.Clear(state.DelayLine);
                            state.SampleCounter = 0;
                            state.DelayLineIndex = 0;
                        }
                    }
                }

                if (_upsamplingStates != null)
                {
                    foreach (var state in _upsamplingStates)
                    {
                        if (state != null)
                        {
                            if (state.PreviousTail != null)
                                Array.Clear(state.PreviousTail);
                            if (state.PreviousBody != null)
                                Array.Clear(state.PreviousBody);
                            state.IsFirstBlock = true;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Get a human-readable description of the current sampling configuration
        /// </summary>
        /// <returns>Description string</returns>
        public string GetDescription()
        {
            if (!IsEnabled)
                return "No sampling change (bypass)";

            if (IsUpsampling)
                return $"Upsampling by {InterpolationFactor}x (inserting {_samplingFactor} zero(s) between samples + sinc interpolation)";

            if (IsDownsampling)
                return $"Downsampling by {DecimationFactor}x (keeping every {DecimationFactor} sample with anti-aliasing filter)";

            return "Unknown sampling mode";
        }
    }
}