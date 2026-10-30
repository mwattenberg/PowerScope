using System.Buffers.Binary;

namespace PowerScope.Model
{
    public class DataParser
    {
        private readonly int _numberOfBytesPerSample;
        private readonly int _numberOfBytesPerChannel;
        private readonly bool _usesFraming;

        public enum ParserMode { ASCII, Binary }
        public enum BinaryFormat { int16_t, uint16_t, int32_t, uint32_t, float_t }
        public ParserMode Mode { get; init; }
        public int NumberOfChannels { get; init; }
        public BinaryFormat Format { get; init; }
        public byte[] FrameStart { get; init; }
        public char FrameEnd { get; init; }
        public char Separator { get; init; }

        /// <summary>
        /// Number of raw bytes that make up one complete sample across all channels
        /// (binary modes only). Callers use this to size the pre-allocated output buffers
        /// for <see cref="ParseInto"/>. Zero for ASCII parsers, where byte-per-sample has no
        /// fixed meaning.
        /// </summary>
        public int BytesPerSample
        {
            get { return _numberOfBytesPerSample; }
        }

        public DataParser(int numberOfChannels, char frameEnd, char separator)
        {
            Mode = ParserMode.ASCII;
            NumberOfChannels = numberOfChannels;
            FrameEnd = frameEnd;
            Separator = separator;
        }

        public DataParser(BinaryFormat format, int numberOfChannels, byte[] frameStart)
        {
            Mode = ParserMode.Binary;
            NumberOfChannels = numberOfChannels;
            Format = format;
            FrameStart = frameStart;

            _numberOfBytesPerChannel = GetBytesPerChannel(format);
            _numberOfBytesPerSample = NumberOfChannels * _numberOfBytesPerChannel;
            _usesFraming = frameStart != null && frameStart.Length > 0;
        }

        public DataParser(BinaryFormat format, int numberOfChannels)
        {
            Mode = ParserMode.Binary;
            NumberOfChannels = numberOfChannels;
            Format = format;

            _numberOfBytesPerChannel = GetBytesPerChannel(format);
            _numberOfBytesPerSample = NumberOfChannels * _numberOfBytesPerChannel;
            _usesFraming = false;
        }

        private int GetBytesPerChannel(BinaryFormat format)
        {
            int bytesPerChannel = format switch
            {
                BinaryFormat.int16_t => sizeof(short),
                BinaryFormat.uint16_t => sizeof(ushort),
                BinaryFormat.int32_t => sizeof(int),
                BinaryFormat.uint32_t => sizeof(uint),
                BinaryFormat.float_t => sizeof(float),
                _ => throw new ArgumentException("Unsupported binary format")
            };

            return bytesPerChannel;
        }

        public ParsedData ParseData(Span<byte> data)
        {
            if (Mode == ParserMode.ASCII)
            {
                string textData = System.Text.Encoding.UTF8.GetString(data);
                return ParseAsciiData(textData);
            }

            if (_usesFraming)
                return ParseBinaryWithFrameStart(data);
            else
                return ParseSimpleBinary(data);
        }

        /// <summary>
        /// Zero-allocation parse path for the high-throughput acquisition loop (binary modes only).
        ///
        /// Instead of allocating a fresh double[][] and residue byte[] on every call — which at
        /// 3 MBaud means continuous garbage on the read thread — this fills caller-owned buffers
        /// in place. Callers (SerialDataStream, USBDataStream) allocate <paramref name="output"/> and
        /// <paramref name="residueBuffer"/> once at startup and reuse them on every read.
        ///
        /// Sizing contract (caller's responsibility):
        ///   output.Length          == NumberOfChannels
        ///   output[ch].Length      >= data.Length / BytesPerSample   (worst case: simple binary, no residue)
        ///   residueBuffer.Length    >= data.Length                    (residue can be the whole buffer when
        ///                                                              no complete frame is found)
        ///
        /// On return:
        ///   output[ch][0 .. returnValue)        — the valid samples for channel ch this call
        ///   residueBuffer[0 .. residueLength)   — trailing bytes that did not form a complete sample/frame;
        ///                                          the caller copies these to the front of its working buffer
        ///                                          before the next read (same role as the old ParsedData.Residue)
        ///
        /// ASCII is intentionally NOT supported here. ASCII throughput is never the bottleneck (anyone
        /// choosing ASCII has already traded away throughput), so it stays on the allocating ParseData()
        /// path rather than complicating this method. Calling with Mode == ASCII throws.
        /// </summary>
        /// <param name="data">Raw bytes for this read cycle, with any prior residue already prepended.</param>
        /// <param name="output">Caller-owned [channel][sample] output matrix; must be pre-sized (see contract).</param>
        /// <param name="residueBuffer">Caller-owned buffer receiving the incomplete trailing bytes.</param>
        /// <param name="residueLength">Number of valid bytes written to <paramref name="residueBuffer"/>.</param>
        /// <returns>Number of complete samples written per channel.</returns>
        public int ParseInto(Span<byte> data, double[][] output, byte[] residueBuffer, out int residueLength)
        {
            if (Mode == ParserMode.ASCII)
                throw new InvalidOperationException("ParseInto supports binary modes only; use ParseData for ASCII.");

            if (_usesFraming)
                return ParseBinaryWithFrameStartInto(data, output, residueBuffer, out residueLength);
            else
                return ParseSimpleBinaryInto(data, output, residueBuffer, out residueLength);
        }

        private int ParseSimpleBinaryInto(Span<byte> data, double[][] output, byte[] residueBuffer, out int residueLength)
        {
            int numberOfLines = data.Length / _numberOfBytesPerSample;

            for (int currentLine = 0; currentLine < numberOfLines; currentLine++)
            {
                for (int channel = 0; channel < NumberOfChannels; channel++)
                {
                    int offset = channel * _numberOfBytesPerChannel + currentLine * _numberOfBytesPerSample;
                    output[channel][currentLine] = ReadBinaryValue(data, offset);
                }
            }

            // Trailing bytes that did not complete a full sample become residue for the next read.
            int processedBytes = numberOfLines * _numberOfBytesPerSample;
            residueLength = data.Length - processedBytes;
            if (residueLength > 0)
                data.Slice(processedBytes, residueLength).CopyTo(residueBuffer);

            return numberOfLines;
        }

        private int ParseBinaryWithFrameStartInto(Span<byte> data, double[][] output, byte[] residueBuffer, out int residueLength)
        {
            int sequenceLength = FrameStart.Length + _numberOfBytesPerSample;
            int lineCount = 0;
            int lastSequenceEnd = 0;

            // Scan for frame starts and decode each complete sequence directly into the output buffers.
            // Unlike ParseData's framed path this keeps no List<int> of offsets — we write samples as we find them.
            int i = 0;
            while (i <= data.Length - sequenceLength)
            {
                if (IsFrameStart(data, i))
                {
                    int dataStart = i + FrameStart.Length;
                    for (int channel = 0; channel < NumberOfChannels; channel++)
                    {
                        int channelDataStart = dataStart + (channel * _numberOfBytesPerChannel);
                        output[channel][lineCount] = ReadBinaryValue(data, channelDataStart);
                    }

                    lineCount++;
                    i += sequenceLength;
                    lastSequenceEnd = i;
                }
                else
                {
                    i++;
                }
            }

            // Residue is everything after the last complete sequence; if none was found the whole
            // buffer is residue (a frame start may still be completed by the next read).
            if (lineCount > 0)
                residueLength = data.Length - lastSequenceEnd;
            else
                residueLength = data.Length;

            if (residueLength > 0)
                data.Slice(data.Length - residueLength, residueLength).CopyTo(residueBuffer);

            return lineCount;
        }

        private ParsedData ParseSimpleBinary(Span<byte> data)
        {
            int numberOfLines = data.Length / _numberOfBytesPerSample;
            double[][] numbers = new double[NumberOfChannels][];

            // Initialize arrays for all channels
            for (int i = 0; i < NumberOfChannels; i++)
            {
                numbers[i] = new double[numberOfLines];
            }

            for (int currentLine = 0; currentLine < numberOfLines; currentLine++)
            {
                for (int channel = 0; channel < NumberOfChannels; channel++)
                {
                    int offset = channel * _numberOfBytesPerChannel + currentLine * _numberOfBytesPerSample;
                    numbers[channel][currentLine] = ReadBinaryValue(data, offset);
                }
            }

            // Calculate residue
            int processedBytes = numberOfLines * _numberOfBytesPerSample;
            byte[] residue = null;
            if (processedBytes < data.Length)
                residue = data.Slice(processedBytes).ToArray();

            return new ParsedData(numbers, residue);
        }

        private ParsedData ParseBinaryWithFrameStart(Span<byte> data)
        {
            List<int> sequences = FindCompleteSequences(data);
            int numberOfLines = sequences.Count;
            double[][] numbers = new double[NumberOfChannels][];

            // Initialize arrays for all channels
            for (int i = 0; i < NumberOfChannels; i++)
            {
                numbers[i] = new double[numberOfLines];
            }

            // Process each complete sequence
            for (int seqIndex = 0; seqIndex < sequences.Count; seqIndex++)
            {
                int sequenceStart = sequences[seqIndex];
                int dataStart = sequenceStart + FrameStart.Length;

                for (int channel = 0; channel < NumberOfChannels; channel++)
                {
                    int channelDataStart = dataStart + (channel * _numberOfBytesPerChannel);
                    numbers[channel][seqIndex] = ReadBinaryValue(data, channelDataStart);
                }
            }

            // Calculate residue - data after the last complete sequence
            byte[] residue = null;
            if (sequences.Count > 0)
            {
                int lastSequenceEnd = sequences[sequences.Count - 1] + FrameStart.Length + _numberOfBytesPerSample;
                if (lastSequenceEnd < data.Length)
                    residue = data.Slice(lastSequenceEnd).ToArray();
            }
            else if (data.Length > 0)
            {
                residue = data.ToArray();
            }

            return new ParsedData(numbers, residue);
        }

        private double ReadBinaryValue(Span<byte> data, int offset)
        {
            Span<byte> slice = data.Slice(offset, _numberOfBytesPerChannel);

            double value = Format switch
            {
                BinaryFormat.int16_t => BinaryPrimitives.ReadInt16LittleEndian(slice),
                BinaryFormat.uint16_t => BinaryPrimitives.ReadUInt16LittleEndian(slice),
                BinaryFormat.int32_t => BinaryPrimitives.ReadInt32LittleEndian(slice),
                BinaryFormat.uint32_t => BinaryPrimitives.ReadUInt32LittleEndian(slice),
                BinaryFormat.float_t => BinaryPrimitives.ReadSingleLittleEndian(slice),
                _ => 0
            };

            return value;
        }

        private List<int> FindCompleteSequences(Span<byte> data)
        {
            List<int> sequences = new List<int>();
            int sequenceLength = FrameStart.Length + _numberOfBytesPerSample;

            // Search for frame start patterns
            for (int i = 0; i <= data.Length - sequenceLength; i++)
            {
                if (IsFrameStart(data, i))
                {
                    sequences.Add(i);
                    i += sequenceLength - 1;
                }
            }

            return sequences;
        }

        private bool IsFrameStart(Span<byte> data, int position)
        {
            for (int i = 0; i < FrameStart.Length; i++)
            {
                if (data[position + i] != FrameStart[i])
                    return false;
            }
            return true;
        }

        private ParsedData ParseAsciiData(string textData)
        {
            string[] lines = textData.Split(FrameEnd, StringSplitOptions.RemoveEmptyEntries);

            // Pre-allocate channel-based arrays directly
            double[][] channelData = new double[NumberOfChannels][];
            for (int ch = 0; ch < NumberOfChannels; ch++)
            {
                channelData[ch] = new double[lines.Length];
            }

            int validLineCount = 0;
            string residueText = "";

            foreach (string line in lines)
            {
                string[] values = line.Trim().Split(Separator, StringSplitOptions.RemoveEmptyEntries);

                if (values.Length >= NumberOfChannels)
                {
                    bool validLine = true;

                    for (int i = 0; i < NumberOfChannels; i++)
                    {
                        if (!double.TryParse(values[i].Trim(), out double parsedValue))
                        {
                            validLine = false;
                            break;
                        }
                        channelData[i][validLineCount] = parsedValue;
                    }

                    if (validLine)
                        validLineCount++;
                }
                else if (values.Length > 0)
                {
                    residueText = line;
                }
            }

            // Trim arrays to actual valid line count
            if (validLineCount < lines.Length)
            {
                for (int ch = 0; ch < NumberOfChannels; ch++)
                {
                    Array.Resize(ref channelData[ch], validLineCount);
                }
            }

            // Convert residue back to bytes
            byte[] residue = null;
            if (!string.IsNullOrEmpty(residueText))
                residue = System.Text.Encoding.UTF8.GetBytes(residueText);

            return new ParsedData(channelData, residue);
        }
    }

    public class ParsedData
    {
        public double[][] Data { get; init; }
        public byte[] Residue { get; init; }

        public ParsedData(double[][] data, byte[] residue)
        {
            Data = data;
            Residue = residue;
        }
    }
}
