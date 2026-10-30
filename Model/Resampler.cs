using System.ComponentModel;
using System.Numerics;

namespace PowerScope.Model
{
    public class Resampler : INotifyPropertyChanged
    {
        // Public constants/config
        private const int SincKernelHalfLength = 32; // Half-length; kernel size = 2*N + 1 (odd)

        // Sampling factor: -9..+9 (StreamSettings already clamps; we clamp again defensively) <source_id data="2" title="StreamSettings.cs" />
        private int _samplingFactor;
        private double[] _sincKernel;
        // Same taps as _sincKernel but in reverse order, so that y[i] = sum_j hReversed[j] * x[i-kernelTailLen+j]
        // becomes a dot product of two contiguous, increasing-index slices - the layout SIMD wants.
        private double[] _sincKernelReversed;
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

            // Reusable per-channel scratch, grown on demand and never freed in steady state:
            //   Work     — zero-stuffed (up) / tail-prefixed (raw) input for the FIR
            //   Filtered — FIR output (y)
            //   Out      — the right-sized result handed back to the caller
            // Reused across blocks so steady-state resampling allocates nothing.
            public double[] Work = Array.Empty<double>();
            public double[] Filtered = Array.Empty<double>();
            public double[] Out = Array.Empty<double>();

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

        public Resampler(int factor)
        {
            _samplingFactor = Math.Max(-9, Math.Min(9, factor)); // clamp <source_id data="2" title="StreamSettings.cs" />
            GenerateSincKernel();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public bool IsEnabled => _samplingFactor != 0;
        public bool IsUpsampling => _samplingFactor > 0;
        public bool IsDownsampling => _samplingFactor < 0;

        public double SampleRateMultiplier => FactorToMultiplier(_samplingFactor);

        /// <summary>
        /// Canonical mapping from a resampling factor to the sample-rate multiplier it represents.
        /// Encoding (single source of truth for the whole app):
        ///   0  → ×1   (bypass, no resampling)
        ///   +n → ×(n+1)        (upsample: +1 → ×2, +2 → ×3, …)
        ///   −n → ÷(n+1) = 1/(n+1)  (downsample: −1 → ½, −2 → ⅓, …)
        /// </summary>
        public static double FactorToMultiplier(int factor)
        {
            if (factor == 0) return 1.0;
            if (factor > 0) return factor + 1.0;
            return 1.0 / (Math.Abs(factor) + 1.0);
        }

        /// <summary>
        /// Short human-readable label for a resampling factor, e.g. "1x" (bypass), "2x" (upsample ×2),
        /// "1/2" (downsample ÷2). Same encoding as <see cref="FactorToMultiplier"/>; used by both the
        /// stream-config slider readout and <see cref="GetDescription"/> so the two cannot drift.
        /// </summary>
        public static string FactorToLabel(int factor)
        {
            if (factor == 0) return "1x";
            if (factor > 0) return $"{factor + 1}x";
            return $"1/{Math.Abs(factor) + 1}";
        }

        /// <summary>
        /// Resampling factor, range −9..+9. This is an offset from bypass, NOT a multiplier:
        /// <c>0</c> = no resampling, <c>+n</c> = upsample ×(n+1), <c>−n</c> = downsample ÷(n+1).
        /// (So the "off" value is 0, even though the UI renders it as "1x".)
        /// </summary>
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
                return $"Upsampling {FactorToLabel(_samplingFactor)} (insert {L} zeros + sinc filter)";
            }
            else
            {
                int M = Math.Abs(_samplingFactor) + 1;
                return $"Downsampling to {FactorToLabel(_samplingFactor)} rate (sinc anti-alias + keep every {M}th)";
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

        // Allocating convenience overload (returns a right-sized array). Kept for the unit tests and
        // any non-hot-path caller; the acquisition hot path uses the zero-allocation overload below.
        public double[] ProcessChannelData(int channelIndex, double[] inputData)
        {
            if (inputData == null || inputData.Length == 0 || !IsEnabled)
                return inputData;

            int n = ProcessChannelData(channelIndex, inputData, inputData.Length, out double[] reused);
            double[] result = new double[n];
            Array.Copy(reused, 0, result, 0, n);
            return result;
        }

        /// <summary>
        /// Zero-allocation channelized resampling for the acquisition hot path.
        ///
        /// Reads <paramref name="inputCount"/> samples from <paramref name="input"/> and writes the
        /// resampled result into a buffer owned by this Resampler, handed back via <paramref name="output"/>.
        /// That buffer is reused on the next call for the same channel, so the caller must consume
        /// <c>output[0..return)</c> (e.g. push to the ring buffer) before calling again for that channel.
        /// The internal scratch/output buffers only grow when a larger block arrives, so steady-state
        /// resampling allocates nothing.
        /// </summary>
        /// <returns>Number of valid samples written to <paramref name="output"/>.</returns>
        public int ProcessChannelData(int channelIndex, double[] input, int inputCount, out double[] output)
        {
            if (_channels == null || channelIndex < 0 || channelIndex >= _channels.Length)
                InitializeChannels(Math.Max(channelIndex + 1, 1));

            if (input == null || inputCount <= 0 || !IsEnabled)
            {
                output = input;
                return inputCount <= 0 ? 0 : inputCount;
            }

            EnsureKernel();
            ChannelState state = _channels[channelIndex];

            if (IsUpsampling)
                return ProcessUpsampling(input, inputCount, state, out output);
            else
                return ProcessDownsampling(input, inputCount, state, out output);
        }

        // Grows (never shrinks) a reusable scratch buffer to at least <paramref name="needed"/> elements.
        private static double[] EnsureCapacity(double[] buffer, int needed)
        {
            if (buffer == null || buffer.Length < needed)
                return new double[needed];
            return buffer;
        }

        private void EnsureKernel()
        {
            if (_sincKernel == null || _sincKernel.Length == 0)
                GenerateSincKernel();
        }

        // Build a Hanning-windowed sinc low-pass kernel with cutoff tied to the effective factor M.
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

                double w = 0.42 - 0.5 * Math.Cos(2.0 * Math.PI * i / (len - 1)) + 0.08 * Math.Cos(4.0 * Math.PI * i / (len - 1)); //Blackman
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

            double[] hReversed = new double[len];
            for (int i = 0; i < len; i++)
                hReversed[i] = h[len - 1 - i];
            _sincKernelReversed = hReversed;
        }

        // Upsampling: insert L zeros then filter with FIR.
        // L = _samplingFactor (zeros between samples), M = L + 1 (final multiplier)
        private int ProcessUpsampling(double[] input, int inputCount, ChannelState state, out double[] output)
        {
            int L = _samplingFactor;
            int M = L + 1;

            // Build upsampled buffer for this block, prefixed by previous upsampled tail for FIR continuity.
            int upBlockLen = inputCount * M;
            int totalUpLen = state.UpTail.Length + upBlockLen;

            // Reusable zero-stuffed buffer (grow-only). Must be cleared first: upsampling relies on the
            // gaps between inserted samples being zero, and the buffer carries stale data from prior blocks.
            state.Work = EnsureCapacity(state.Work, totalUpLen);
            double[] up = state.Work;
            Array.Clear(up, 0, totalUpLen);

            // Copy previous tail
            Array.Copy(state.UpTail, 0, up, 0, state.UpTail.Length);

            // Zero-stuff: place each input sample every M steps
            int writeBase = state.UpTail.Length;
            for (int n = 0; n < inputCount; n++)
            {
                up[writeBase + n * M] = input[n] * M;
            }

            // FIR filtering over the upsampled stream
            state.Filtered = EnsureCapacity(state.Filtered, totalUpLen);
            double[] y = state.Filtered;
            ConvolveFIRSteadyState(up, y, _sincKernelReversed, totalUpLen);

            // Update tail for next block: last (kernelLen-1) of upsampled sequence
            int tailCopy = Math.Min(state.UpTail.Length, totalUpLen);
            Array.Copy(up, totalUpLen - tailCopy, state.UpTail, 0, tailCopy);

            // Return the filtered samples corresponding to the new upsampled portion
            state.Out = EnsureCapacity(state.Out, upBlockLen);
            Array.Copy(y, state.UpTail.Length, state.Out, 0, upBlockLen);
            output = state.Out;
            return upBlockLen;
        }

        // Downsampling: filter first with FIR, then keep every M-th sample
        // M = |_samplingFactor| + 1
        private int ProcessDownsampling(double[] input, int inputCount, ChannelState state, out double[] output)
        {
            int M = Math.Abs(_samplingFactor) + 1;

            // Raw buffer for this block with previous raw tail for FIR continuity (grow-only reuse).
            int totalRawLen = state.DownTail.Length + inputCount;
            state.Work = EnsureCapacity(state.Work, totalRawLen);
            double[] raw = state.Work;

            Array.Copy(state.DownTail, 0, raw, 0, state.DownTail.Length);
            Array.Copy(input, 0, raw, state.DownTail.Length, inputCount);

            // Filter raw
            state.Filtered = EnsureCapacity(state.Filtered, totalRawLen);
            double[] y = state.Filtered;
            ConvolveFIRSteadyState(raw, y, _sincKernelReversed, totalRawLen);

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

            state.Out = EnsureCapacity(state.Out, kept);
            double[] outBlock = state.Out;
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
            output = state.Out;
            return kept;
        }

        /// <summary>
        /// FIR convolution over x, writing only the indices that callers actually read.
        ///
        /// x is always [tail (kernelTailLen samples) | new data]. Callers only ever read
        /// y[kernelTailLen..] - the tail-prefix outputs y[0..kernelTailLen-2] are never used for
        /// the result and never feed the next block's tail (that comes from the raw/zero-stuffed
        /// input, not from y), so they are not computed at all. Every index this function does
        /// write has a full, always-valid kernel-length window behind it (guaranteed by the
        /// caller-supplied tail), so the inner loop never needs a bounds check and is a fixed-width
        /// dot product - straightforward to vectorize.
        /// </summary>
        private static void ConvolveFIRSteadyState(double[] x, double[] y, double[] hReversed, int len)
        {
            int kLen = hReversed.Length;
            int startIndex = kLen - 1;
            int vectorSize = Vector<double>.Count;
            int vectorBound = kLen - (kLen % vectorSize);

            for (int i = startIndex; i < len; i++)
            {
                int baseIndex = i - kLen + 1;
                double acc = 0.0;

                int j = 0;
                for (; j < vectorBound; j += vectorSize)
                {
                    Vector<double> hVec = new Vector<double>(hReversed, j);
                    Vector<double> xVec = new Vector<double>(x, baseIndex + j);
                    acc += Vector.Dot(hVec, xVec);
                }

                for (; j < kLen; j++)
                {
                    acc += hReversed[j] * x[baseIndex + j];
                }

                y[i] = acc;
            }
        }
    }
}