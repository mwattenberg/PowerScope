using System;
using System.ComponentModel;

namespace PowerScope.Model
{
    public class UpDownSampling : INotifyPropertyChanged
    {
        // Public constants/config
        private const int SincKernelHalfLength = 32; // Half-length; kernel size = 2*N + 1 (odd)

        // Sampling factor: -9..+9 (StreamSettings already clamps; we clamp again defensively) <source_id data="2" title="StreamSettings.cs" />
        private int _samplingFactor;
        private double[] _sincKernel;
        private int _kernelLen; // convenience
        private int _kernelTailLen; // = _kernelLen - 1

        // Per-channel streaming state
        private ChannelState[] _channels;

        private class ChannelState
        {
            // Tail of the raw input used for downsampling filter continuity
            public double[] DownTail;
            // Tail of the upsampled (zero-stuffed) stream for upsampling filter continuity
            public double[] UpTail;
            // Decimation phase across blocks (0..M-1)
            public int DecimPhase;

            public ChannelState(int kernelTailLen)
            {
                DownTail = new double[kernelTailLen];
                UpTail = new double[kernelTailLen];
                DecimPhase = 0;
            }

            public void Reset()
            {
                Array.Clear(DownTail, 0, DownTail.Length);
                Array.Clear(UpTail, 0, UpTail.Length);
                DecimPhase = 0;
            }
        }

        public UpDownSampling(int factor)
        {
            _samplingFactor = Math.Max(-9, Math.Min(9, factor)); // clamp <source_id data="2" title="StreamSettings.cs" />
            GenerateSincKernel();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public bool IsEnabled => _samplingFactor != 0;
        public bool IsUpsampling => _samplingFactor > 0;
        public bool IsDownsampling => _samplingFactor < 0;

        // Matches your existing mapping (+1 → 2x, +2 → 3x; -1 → 1/2, -2 → 1/3, etc.) <source_id data="1" title="UpDownSampling.cs" />
        public double SampleRateMultiplier
        {
            get
            {
                if (_samplingFactor == 0) return 1.0;
                if (_samplingFactor > 0) return _samplingFactor + 1.0;
                return 1.0 / (Math.Abs(_samplingFactor) + 1.0);
            }
        }

        public int SamplingFactor
        {
            get => _samplingFactor;
            set
            {
                int clamped = Math.Max(-9, Math.Min(9, value)); // defensive clamp <source_id data="2" title="StreamSettings.cs" />
                if (_samplingFactor != clamped)
                {
                    _samplingFactor = clamped;
                    GenerateSincKernel();
                    Reset(); // reset streaming state for new factor
                    OnPropertyChanged(nameof(SamplingFactor));
                }
            }
        }

        public void InitializeChannels(int channelCount)
        {
            if (channelCount <= 0)
            {
                _channels = Array.Empty<ChannelState>();
                return;
            }

            EnsureKernel();
            _channels = new ChannelState[channelCount];
            for (int i = 0; i < channelCount; i++)
            {
                _channels[i] = new ChannelState(_kernelTailLen);
            }
        }

        public void Reset()
        {
            if (_channels == null) return;
            foreach (var ch in _channels)
                ch?.Reset();
        }

        public string GetDescription()
        {
            if (!IsEnabled) return "Bypass (no up/down sampling)";
            if (IsUpsampling)
            {
                int L = _samplingFactor; // zeros between samples
                int M = L + 1;
                return $"Upsampling x{M} (insert {L} zeros + sinc filter)";
            }
            else
            {
                int M = Math.Abs(_samplingFactor) + 1;
                return $"Downsampling x{M} (sinc anti-alias + keep every {M}th)";
            }
        }

        // Single-array helper for consumers that don’t use channelized processing
        public double[] ProcessData(double[] inputData)
        {
            if (!IsEnabled || inputData == null || inputData.Length == 0)
                return inputData;

            if (_channels == null || _channels.Length == 0)
                InitializeChannels(1);

            return ProcessChannelData(0, inputData);
        }

        // Main API used by SerialDataStream <source_id data="3" title="SerialDataStream.cs" />
        public double[] ProcessChannelData(int channelIndex, double[] inputData)
        {
            if (_channels == null || channelIndex < 0 || channelIndex >= _channels.Length)
                InitializeChannels(Math.Max(channelIndex + 1, 1));

            if (inputData == null || inputData.Length == 0 || !IsEnabled)
                return inputData;

            EnsureKernel();
            var state = _channels[channelIndex];

            if (IsUpsampling)
                return ProcessUpsampling(inputData, state);
            else
                return ProcessDownsampling(inputData, state);
        }

        private void EnsureKernel()
        {
            if (_sincKernel == null || _sincKernel.Length == 0)
                GenerateSincKernel();
        }

        // Build a Hamming-windowed sinc low-pass kernel with cutoff tied to the effective factor M.
        // For upsampling by M: cutoff ≈ 0.5/M. For downsampling by M: cutoff ≈ 0.5/M.
        // We use 0.9 safety factor to reduce alias/ripple.
        private void GenerateSincKernel()
        {
            int M = Math.Abs(_samplingFactor) + 1; // effective factor (2x when +1, 1/2x when -1)
            double cutoff = 0.5 / M; // normalized to Nyquist=0.5
            cutoff *= 0.9; // conservative margin

            int N = SincKernelHalfLength;
            int len = 2 * N + 1; // odd length for symmetric linear-phase
            var h = new double[len];

            for (int i = 0; i < len; i++)
            {
                int n = i - N;
                double sinc;
                if (n == 0)
                {
                    sinc = 2.0 * cutoff;
                }
                else
                {
                    double x = 2.0 * Math.PI * cutoff * n;
                    sinc = Math.Sin(x) / (Math.PI * n);
                }

                // Hamming window
                double w = 0.54 - 0.46 * Math.Cos(2.0 * Math.PI * i / (len - 1));
                h[i] = sinc * w;
            }

            // Normalize to unity DC gain
            double sum = 0.0;
            for (int i = 0; i < len; i++) sum += h[i];
            if (sum != 0.0)
            {
                for (int i = 0; i < len; i++) h[i] /= sum;
            }

            _sincKernel = h;
            _kernelLen = len;
            _kernelTailLen = len - 1;
        }

        // Upsampling: insert L zeros then filter with FIR.
        // L = _samplingFactor (zeros between samples), M = L + 1 (final multiplier)
        private double[] ProcessUpsampling(double[] input, ChannelState state)
        {
            int L = _samplingFactor;
            int M = L + 1;

            // Build upsampled buffer for this block, prefixed by previous upsampled tail for FIR continuity.
            int upBlockLen = input.Length * M;
            int totalUpLen = state.UpTail.Length + upBlockLen;
            var up = new double[totalUpLen];

            // Copy previous tail
            Array.Copy(state.UpTail, 0, up, 0, state.UpTail.Length);

            // Zero-stuff: place each input sample every M steps
            int writeBase = state.UpTail.Length;
            for (int n = 0; n < input.Length; n++)
            {
                up[writeBase + n * M] = input[n] * M;
            }

            // FIR filtering over the upsampled stream
            var y = new double[totalUpLen];
            ConvolveFIRStreaming(up, y, _sincKernel);

            // Update tail for next block: last (kernelLen-1) of upsampled sequence
            int tailCopy = Math.Min(state.UpTail.Length, totalUpLen);
            Array.Copy(up, totalUpLen - tailCopy, state.UpTail, 0, tailCopy);

            // Return the filtered samples corresponding to the new upsampled portion
            var outBlock = new double[upBlockLen];
            Array.Copy(y, state.UpTail.Length, outBlock, 0, upBlockLen);
            return outBlock;
        }

        // Downsampling: filter first with FIR, then keep every M-th sample
        // M = |_samplingFactor| + 1
        private double[] ProcessDownsampling(double[] input, ChannelState state)
        {
            int M = Math.Abs(_samplingFactor) + 1;

            // Raw buffer for this block with previous raw tail for FIR continuity
            int totalRawLen = state.DownTail.Length + input.Length;
            var raw = new double[totalRawLen];

            Array.Copy(state.DownTail, 0, raw, 0, state.DownTail.Length);
            Array.Copy(input, 0, raw, state.DownTail.Length, input.Length);

            // Filter raw
            var y = new double[totalRawLen];
            ConvolveFIRStreaming(raw, y, _sincKernel);

            // Update raw tail for next block
            int tailCopy = Math.Min(state.DownTail.Length, totalRawLen);
            Array.Copy(raw, totalRawLen - tailCopy, state.DownTail, 0, tailCopy);

            // Now decimate the NEW part of y (skip the pre-pended tail portion)
            int newFilteredLen = totalRawLen - state.DownTail.Length;
            int startIndex = state.DownTail.Length;

            // Estimate output length
            int kept = 0;
            int phase = state.DecimPhase;
            for (int i = 0; i < newFilteredLen; i++)
            {
                if (phase == 0) kept++;
                phase = (phase + 1) % M;
            }

            var outBlock = new double[kept];
            int outIdx = 0;
            phase = state.DecimPhase;

            for (int i = 0; i < newFilteredLen; i++)
            {
                if (phase == 0)
                {
                    outBlock[outIdx++] = y[startIndex + i];
                }
                phase = (phase + 1) % M;
            }

            state.DecimPhase = phase;
            return outBlock;
        }

        // Simple FIR with causal convolution using provided input buffer (already includes needed tail)
        private static void ConvolveFIRStreaming(double[] x, double[] y, double[] h)
        {
            int len = x.Length;
            int kLen = h.Length;

            for (int i = 0; i < len; i++)
            {
                double acc = 0.0;
                // h[0] aligns with x[i], h<source_id data="1" title="UpDownSampling.cs" /> with x[i-1], ...
                int xIdx = i;
                for (int k = 0; k < kLen; k++, xIdx--)
                {
                    if (xIdx < 0) break;
                    acc += h[k] * x[xIdx];
                }
                y[i] = acc;
            }
        }
    }
}