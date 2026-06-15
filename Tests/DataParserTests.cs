using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using PowerScope.Model;
using Xunit;

namespace PowerScope.Tests
{
    /// <summary>
    /// Tests the zero-allocation binary parse path (DataParser.ParseInto) added for the
    /// high-throughput acquisition loop, plus the RingBuffer.AddRange(array, count) overload it
    /// relies on.
    ///
    /// The most valuable tests here are the "parity" ones: ParseInto is pinned to the older,
    /// already-trusted ParseData by feeding both identical bytes and asserting identical output.
    /// ParseData's binary path is otherwise unused in production now — it is kept deliberately as
    /// the oracle for these tests, so any future divergence in the fast path is caught immediately.
    /// </summary>
    public class DataParserTests
    {
        // ---- helpers -------------------------------------------------------

        // Simple binary layout: samples are line-major and channel-interleaved within a line:
        //   line0[ch0] line0[ch1] line1[ch0] line1[ch1] ...
        private static byte[] EncodeSimpleInt16(short[][] lines, int extraTrailingBytes)
        {
            int channels = lines.Length > 0 ? lines[0].Length : 0;
            byte[] buffer = new byte[(lines.Length * channels * sizeof(short)) + extraTrailingBytes];

            int offset = 0;
            foreach (short[] line in lines)
            {
                foreach (short value in line)
                {
                    BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(offset), value);
                    offset += sizeof(short);
                }
            }

            // Fill any trailing partial-sample bytes with a recognizable pattern.
            for (int i = 0; i < extraTrailingBytes; i++)
                buffer[offset + i] = (byte)(0xA0 + i);

            return buffer;
        }

        // Framed binary layout: each sample is preceded by the frame-start marker:
        //   [frameStart] line[ch0] line[ch1] ... repeated per line.
        private static byte[] EncodeFramedInt16(byte[] frameStart, short[][] lines, int extraTrailingBytes)
        {
            int channels = lines.Length > 0 ? lines[0].Length : 0;
            int sequenceLength = frameStart.Length + (channels * sizeof(short));
            byte[] buffer = new byte[(lines.Length * sequenceLength) + extraTrailingBytes];

            int offset = 0;
            foreach (short[] line in lines)
            {
                Array.Copy(frameStart, 0, buffer, offset, frameStart.Length);
                offset += frameStart.Length;
                foreach (short value in line)
                {
                    BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(offset), value);
                    offset += sizeof(short);
                }
            }

            for (int i = 0; i < extraTrailingBytes; i++)
                buffer[offset + i] = (byte)(0xA0 + i);

            return buffer;
        }

        // Runs ParseData (oracle) and ParseInto over the same bytes and asserts identical output.
        private static void AssertParityWithParseData(DataParser parser, byte[] data)
        {
            ParsedData expected = parser.ParseData(data.AsSpan());

            int channels = parser.NumberOfChannels;
            int maxSamples = (data.Length / parser.BytesPerSample) + 1;
            double[][] output = new double[channels][];
            for (int c = 0; c < channels; c++)
                output[c] = new double[maxSamples];
            byte[] residue = new byte[data.Length + 1];

            int count = parser.ParseInto(data.AsSpan(), output, residue, out int residueLength);

            // Same number of decoded samples per channel.
            int expectedCount = expected.Data[0].Length;
            Assert.Equal(expectedCount, count);

            // Same sample values.
            for (int c = 0; c < channels; c++)
            {
                for (int i = 0; i < count; i++)
                    Assert.Equal(expected.Data[c][i], output[c][i]);
            }

            // Same residue bytes.
            int expectedResidueLength = expected.Residue == null ? 0 : expected.Residue.Length;
            Assert.Equal(expectedResidueLength, residueLength);
            for (int i = 0; i < expectedResidueLength; i++)
                Assert.Equal(expected.Residue[i], residue[i]);
        }

        // ---- ParseInto: decode correctness --------------------------------

        [Fact]
        public void ParseInto_SimpleBinary_DecodesExpectedValues()
        {
            DataParser parser = new DataParser(DataParser.BinaryFormat.int16_t, 2);
            short[][] lines = new short[][]
            {
                new short[] { 10, -20 },
                new short[] { 30, -40 },
                new short[] { 1000, -1000 }
            };
            byte[] data = EncodeSimpleInt16(lines, 0);

            double[][] output = new double[2][] { new double[16], new double[16] };
            byte[] residue = new byte[64];

            int count = parser.ParseInto(data.AsSpan(), output, residue, out int residueLength);

            Assert.Equal(3, count);
            Assert.Equal(0, residueLength);
            Assert.Equal(10.0, output[0][0]);
            Assert.Equal(-20.0, output[1][0]);
            Assert.Equal(1000.0, output[0][2]);
            Assert.Equal(-1000.0, output[1][2]);
        }

        [Theory]
        [InlineData(5, 0)]  // exact, no residue
        [InlineData(5, 1)]  // one trailing byte
        [InlineData(5, 3)]  // partial trailing sample (4-byte sample, 3 bytes present)
        [InlineData(1, 2)]
        [InlineData(0, 3)]  // no complete sample at all
        public void ParseInto_SimpleBinary_MatchesParseData(int lineCount, int extraBytes)
        {
            DataParser parser = new DataParser(DataParser.BinaryFormat.int16_t, 2);
            short[][] lines = new short[lineCount][];
            for (int i = 0; i < lineCount; i++)
                lines[i] = new short[] { (short)(i + 1), (short)(-(i + 1)) };

            byte[] data = EncodeSimpleInt16(lines, extraBytes);

            AssertParityWithParseData(parser, data);
        }

        [Theory]
        [InlineData(4, 0)]
        [InlineData(4, 2)]
        [InlineData(1, 5)]  // trailing bytes shorter than a full frame
        public void ParseInto_FramedBinary_MatchesParseData(int lineCount, int extraBytes)
        {
            byte[] frameStart = new byte[] { 0xAA, 0x55 };
            DataParser parser = new DataParser(DataParser.BinaryFormat.int16_t, 2, frameStart);

            short[][] lines = new short[lineCount][];
            for (int i = 0; i < lineCount; i++)
                lines[i] = new short[] { (short)(100 + i), (short)(200 + i) };

            byte[] data = EncodeFramedInt16(frameStart, lines, extraBytes);

            AssertParityWithParseData(parser, data);
        }

        // ---- ParseInto: residue carried across reads ----------------------

        [Fact]
        public void ParseInto_CarriesResidueAcrossReads()
        {
            // Emulate what SerialDataStream/USBDataStream do: a working buffer that prepends the
            // residue from the previous read before parsing the next chunk. A sample split across
            // the read boundary must be reassembled.
            DataParser parser = new DataParser(DataParser.BinaryFormat.int16_t, 2);
            short[][] lines = new short[][]
            {
                new short[] { 1, 2 },
                new short[] { 3, 4 },
                new short[] { 5, 6 },
                new short[] { 7, 8 },
                new short[] { 9, 10 }
            };
            byte[] all = EncodeSimpleInt16(lines, 0); // 5 lines * 2 ch * 2 bytes = 20 bytes

            // Split mid-sample: 15 bytes (3 full lines + 3 bytes of the 4th) then the remaining 5.
            byte[] chunk1 = new byte[15];
            byte[] chunk2 = new byte[all.Length - 15];
            Array.Copy(all, 0, chunk1, 0, chunk1.Length);
            Array.Copy(all, 15, chunk2, 0, chunk2.Length);

            byte[] working = new byte[64];
            double[][] output = new double[2][] { new double[64], new double[64] };
            byte[] residue = new byte[64];
            int residueLength = 0;
            List<double>[] collected = new List<double>[2] { new List<double>(), new List<double>() };

            foreach (byte[] chunk in new byte[][] { chunk1, chunk2 })
            {
                Array.Copy(residue, 0, working, 0, residueLength);
                Array.Copy(chunk, 0, working, residueLength, chunk.Length);
                int total = residueLength + chunk.Length;

                int count = parser.ParseInto(working.AsSpan(0, total), output, residue, out residueLength);
                for (int c = 0; c < 2; c++)
                {
                    for (int i = 0; i < count; i++)
                        collected[c].Add(output[c][i]);
                }
            }

            Assert.Equal(0, residueLength); // everything consumed by the end
            Assert.Equal(5, collected[0].Count);
            for (int line = 0; line < lines.Length; line++)
            {
                Assert.Equal((double)lines[line][0], collected[0][line]);
                Assert.Equal((double)lines[line][1], collected[1][line]);
            }
        }

        [Fact]
        public void ParseInto_ThrowsForAsciiParser()
        {
            DataParser parser = new DataParser(2, '\n', ',');
            double[][] output = new double[2][] { new double[16], new double[16] };
            byte[] residue = new byte[16];
            byte[] data = new byte[] { 0x31, 0x2C, 0x32, 0x0A };

            Assert.Throws<InvalidOperationException>(
                () => parser.ParseInto(data.AsSpan(), output, residue, out _));
        }
    }

    /// <summary>
    /// Tests the RingBuffer.AddRange(T[] source, int count) overload used by the zero-allocation
    /// acquisition path, where the producer fills a reusable buffer larger than the live sample run.
    /// </summary>
    public class RingBufferTests
    {
        [Fact]
        public void AddRange_CopiesExactlyCountElements()
        {
            RingBuffer<double> buffer = new RingBuffer<double>(10);
            double[] source = new double[] { 1, 2, 3, 4, 5 };

            buffer.AddRange(source, 3); // only first 3 are live

            Assert.Equal(3, buffer.Count);
            double[] destination = new double[3];
            int copied = buffer.CopyLatestTo(destination, 3);
            Assert.Equal(3, copied);
            Assert.Equal(new double[] { 1, 2, 3 }, destination);
        }

        [Fact]
        public void AddRange_WrapsAndKeepsLatestWhenOverCapacity()
        {
            RingBuffer<double> buffer = new RingBuffer<double>(4);

            buffer.AddRange(new double[] { 1, 2, 3, 4, 5, 6 }, 6); // overflows capacity 4

            Assert.Equal(4, buffer.Count);
            double[] destination = new double[4];
            buffer.CopyLatestTo(destination, 4);
            Assert.Equal(new double[] { 3, 4, 5, 6 }, destination); // oldest two discarded
        }
    }
}
