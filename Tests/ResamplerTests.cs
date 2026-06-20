using PowerScope.Model;
using Xunit;

namespace PowerScope.Tests
{
    /// <summary>
    /// Cross-checks Resampler's vectorized FIR path against a faithful copy of the original
    /// pre-vectorization scalar algorithm, fed multiple consecutive variably-sized blocks so any
    /// tail-continuity regression across block boundaries would show up as a mismatch.
    /// </summary>
    public class ResamplerTests
    {
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(-1)]
        [InlineData(-2)]
        public void ProcessChannelData_MatchesReferenceImplementation_AcrossBlockBoundaries(int factor)
        {
            double[] kernel = GenerateReferenceKernel(factor, out int kernelLen);

            Resampler sampling = new Resampler(factor);
            sampling.InitializeChannels(1);

            double[] referenceUpTail = new double[kernelLen - 1];
            double[] referenceDownTail = new double[kernelLen - 1];
            int referenceDecimPhase = 0;

            int sampleRate = 1000;
            double frequency = 37.0;
            int[] blockSizes = { 17, 256, 3, 512, 64, 1 };

            List<double> actualOutput = new List<double>();
            List<double> referenceOutput = new List<double>();

            int globalSampleIndex = 0;
            foreach (int blockSize in blockSizes)
            {
                double[] block = new double[blockSize];
                for (int i = 0; i < blockSize; i++)
                {
                    double t = (globalSampleIndex + i) / (double)sampleRate;
                    block[i] = Math.Sin(2.0 * Math.PI * frequency * t);
                }
                globalSampleIndex += blockSize;

                double[] actual = sampling.ProcessChannelData(0, block);
                actualOutput.AddRange(actual);

                double[] reference;
                if (factor > 0)
                {
                    reference = ReferenceUpsample(block, kernel, factor, ref referenceUpTail);
                }
                else
                {
                    reference = ReferenceDownsample(block, kernel, Math.Abs(factor) + 1, ref referenceDownTail, ref referenceDecimPhase);
                }
                referenceOutput.AddRange(reference);
            }

            Assert.Equal(referenceOutput.Count, actualOutput.Count);
            for (int i = 0; i < referenceOutput.Count; i++)
            {
                double difference = Math.Abs(referenceOutput[i] - actualOutput[i]);
                Assert.True(difference < 1e-9, $"Mismatch at index {i}: expected {referenceOutput[i]}, got {actualOutput[i]}");
            }
        }

        private static double[] GenerateReferenceKernel(int samplingFactor, out int kernelLen)
        {
            const int N = 32; // matches Resampler.SincKernelHalfLength
            int M = Math.Abs(samplingFactor) + 1;
            double cutoff = 0.5 / M;
            cutoff *= 0.9;

            int len = 2 * N + 1;
            double[] h = new double[len];
            for (int i = 0; i < len; i++)
            {
                int n = i - N;
                double sinc;
                if (n == 0)
                    sinc = 2.0 * cutoff;
                else
                {
                    double x = 2.0 * Math.PI * cutoff * n;
                    sinc = Math.Sin(x) / (Math.PI * n);
                }
                double w = 0.42 - 0.5 * Math.Cos(2.0 * Math.PI * i / (len - 1)) + 0.08 * Math.Cos(4.0 * Math.PI * i / (len - 1));
                h[i] = sinc * w;
            }

            double sum = 0.0;
            for (int i = 0; i < len; i++)
                sum += h[i];
            if (sum != 0.0)
            {
                for (int i = 0; i < len; i++)
                    h[i] /= sum;
            }

            kernelLen = len;
            return h;
        }

        private static double[] ReferenceUpsample(double[] input, double[] h, int L, ref double[] upTail)
        {
            int M = L + 1;
            int upBlockLen = input.Length * M;
            int totalUpLen = upTail.Length + upBlockLen;
            double[] up = new double[totalUpLen];
            Array.Copy(upTail, 0, up, 0, upTail.Length);

            int writeBase = upTail.Length;
            for (int n = 0; n < input.Length; n++)
                up[writeBase + n * M] = input[n] * M;

            double[] y = new double[totalUpLen];
            ConvolveReference(up, y, h);

            int tailCopy = Math.Min(upTail.Length, totalUpLen);
            double[] newTail = new double[upTail.Length];
            Array.Copy(up, totalUpLen - tailCopy, newTail, 0, tailCopy);

            double[] outBlock = new double[upBlockLen];
            Array.Copy(y, upTail.Length, outBlock, 0, upBlockLen);

            upTail = newTail;
            return outBlock;
        }

        private static double[] ReferenceDownsample(double[] input, double[] h, int M, ref double[] downTail, ref int decimPhase)
        {
            int totalRawLen = downTail.Length + input.Length;
            double[] raw = new double[totalRawLen];
            Array.Copy(downTail, 0, raw, 0, downTail.Length);
            Array.Copy(input, 0, raw, downTail.Length, input.Length);

            double[] y = new double[totalRawLen];
            ConvolveReference(raw, y, h);

            int tailCopy = Math.Min(downTail.Length, totalRawLen);
            double[] newTail = new double[downTail.Length];
            Array.Copy(raw, totalRawLen - tailCopy, newTail, 0, tailCopy);

            int newFilteredLen = totalRawLen - downTail.Length;
            int startIndex = downTail.Length;

            int kept = 0;
            int phase = decimPhase;
            for (int i = 0; i < newFilteredLen; i++)
            {
                if (phase == 0)
                    kept++;
                phase = (phase + 1) % M;
            }

            double[] outBlock = new double[kept];
            int outIdx = 0;
            phase = decimPhase;
            for (int i = 0; i < newFilteredLen; i++)
            {
                if (phase == 0)
                    outBlock[outIdx++] = y[startIndex + i];
                phase = (phase + 1) % M;
            }

            downTail = newTail;
            decimPhase = phase;
            return outBlock;
        }

        // Faithful copy of the original (pre-vectorization) scalar convolution, kept only as a
        // cross-check reference here - not the production implementation.
        private static void ConvolveReference(double[] x, double[] y, double[] h)
        {
            int len = x.Length;
            int kLen = h.Length;
            for (int i = 0; i < len; i++)
            {
                double acc = 0.0;
                int xIdx = i;
                for (int k = 0; k < kLen; k++, xIdx--)
                {
                    if (xIdx < 0)
                        break;
                    acc += h[k] * x[xIdx];
                }
                y[i] = acc;
            }
        }
    }
}
