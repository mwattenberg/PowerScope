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
