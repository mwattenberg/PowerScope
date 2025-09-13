using System.Buffers.Binary;

namespace SerialPlotDN_WPF.Model
{
    public class DataParser
    {
        private readonly int numberOfBytePerSample;
        private readonly int numberOfBytesPerChannel;


        public enum ParserMode { ASCII, Binary }
        public enum BinaryFormat { int16_t, uint16_t, int32_t, uint32_t, float_t }
        public ParserMode Mode { get; init; }
        public int NumberOfChannels { get; init; }
        public BinaryFormat format { get; init; }
        public byte[] FrameStart { get; init; }
        public char FrameEnd { get; init; }
        public char Separator { get; init; }

        public DataParser(int numberOfChannels, char frameEnd, char separator)
        {
            this.Mode = ParserMode.ASCII;
            this.NumberOfChannels = numberOfChannels;
            this.FrameEnd = frameEnd;
            this.Separator = separator;
        }

        public DataParser(BinaryFormat format, int numberOfChannels, byte[] frameStart)
        {
            this.Mode = ParserMode.Binary;
            this.NumberOfChannels = numberOfChannels;
            this.format = format;
            this.FrameStart = frameStart;

            this.numberOfBytesPerChannel = format switch
            {
                BinaryFormat.int16_t => sizeof(short),
                BinaryFormat.uint16_t => sizeof(ushort),
                BinaryFormat.int32_t => sizeof(int),
                BinaryFormat.uint32_t => sizeof(uint),
                BinaryFormat.float_t => sizeof(float),
                _ => throw new ArgumentException("Unsupported binary format")
            };
            this.numberOfBytePerSample = NumberOfChannels * numberOfBytesPerChannel;
        }

        public DataParser(BinaryFormat format, int numberOfChannels)
        {
            this.Mode = ParserMode.Binary;
            this.NumberOfChannels = numberOfChannels;
            this.format = format;

            this.numberOfBytesPerChannel = format switch
            {
                BinaryFormat.int16_t => sizeof(short),
                BinaryFormat.uint16_t => sizeof(ushort),
                BinaryFormat.int32_t => sizeof(int),
                BinaryFormat.uint32_t => sizeof(uint),
                BinaryFormat.float_t => sizeof(float),
                _ => throw new ArgumentException("Unsupported binary format")
            };
            this.numberOfBytePerSample = NumberOfChannels * numberOfBytesPerChannel;
        }


        public ParsedData ParseData(Span<byte> data)
        {
            double[][] numbers = new double[NumberOfChannels][];
            byte[]? residue = null;

            switch (Mode)
            {
                case ParserMode.ASCII:
                    // Parse ASCII data (text format with separators)
                    string textData = System.Text.Encoding.UTF8.GetString(data);
                    return ParseAsciiData(textData);

                case ParserMode.Binary:
                    // Handle both simple binary and binary with frame start
                    if (FrameStart != null && FrameStart.Length > 0)
                    {
                        // Binary with frame start (custom framing)
                        return ParseBinaryWithFrameStart(data);
                    }
                    else
                    {
                        // Simple binary (no framing)
                        return ParseSimpleBinary(data);
                    }

                default:
                    throw new Exception("Unsupported parsing mode");
            }
        }

        private ParsedData ParseSimpleBinary(Span<byte> data)
        {
            double[][] numbers = new double[NumberOfChannels][];
            byte[]? residue = null;

            int numberOfLines = data.Length / numberOfBytePerSample;

            // Initialize arrays for all channels
            for (int i = 0; i < NumberOfChannels; i++)
            {
                numbers[i] = new double[numberOfLines];
            }

            for (int currentLine = 0; currentLine < numberOfLines; currentLine++)
            {
                for (int channel = 0; channel < NumberOfChannels; channel++)
                {
                    switch (format)
                    {
                        case BinaryFormat.int16_t:
                            numbers[channel][currentLine] = BinaryPrimitives.ReadInt16LittleEndian(
                                data.Slice(channel * numberOfBytesPerChannel + currentLine * numberOfBytePerSample));
                            break;
                        case BinaryFormat.uint16_t:
                            numbers[channel][currentLine] = BinaryPrimitives.ReadUInt16LittleEndian(
                                data.Slice(channel * numberOfBytesPerChannel + currentLine * numberOfBytePerSample));
                            numbers[channel][currentLine] = numbers[channel][currentLine] % 4095;
                            break;
                        case BinaryFormat.int32_t:
                            numbers[channel][currentLine] = BinaryPrimitives.ReadInt32LittleEndian(
                                data.Slice(channel * numberOfBytesPerChannel + currentLine * numberOfBytePerSample));
                            break;
                        case BinaryFormat.uint32_t:
                            numbers[channel][currentLine] = BinaryPrimitives.ReadUInt32LittleEndian(
                                data.Slice(channel * numberOfBytesPerChannel + currentLine * numberOfBytePerSample));
                            break;
                        case BinaryFormat.float_t:
                            numbers[channel][currentLine] = BinaryPrimitives.ReadSingleLittleEndian(
                                data.Slice(channel * numberOfBytesPerChannel + currentLine * numberOfBytePerSample));
                            break;
                        default:
                            numbers[channel][currentLine] = 0;
                            break;
                    }
                }
            }

            return new ParsedData(numbers, residue);
        }

        private ParsedData ParseBinaryWithFrameStart(Span<byte> data)
        {
            double[][] numbers = new double[NumberOfChannels][];
            byte[]? residue = null;

            var sequences = FindCompleteSequences(data);
            int numberOfLines = sequences.Count;

            // Initialize arrays for all channels
            for (int i = 0; i < NumberOfChannels; i++)
            {
                numbers[i] = new double[numberOfLines];
            }

            // Process each complete sequence
            for (int seqIndex = 0; seqIndex < sequences.Count; seqIndex++)
            {
                var sequenceStart = sequences[seqIndex];
                // Skip the frame start bytes to get to the data
                var dataStart = sequenceStart + FrameStart.Length;

                for (int channel = 0; channel < NumberOfChannels; channel++)
                {
                    var channelDataStart = dataStart + (channel * numberOfBytesPerChannel);
                    
                    switch (format)
                    {
                        case BinaryFormat.int16_t:
                            numbers[channel][seqIndex] = BinaryPrimitives.ReadInt16LittleEndian(
                                data.Slice(channelDataStart, numberOfBytesPerChannel));
                            break;
                        case BinaryFormat.uint16_t:
                            numbers[channel][seqIndex] = BinaryPrimitives.ReadUInt16LittleEndian(
                                data.Slice(channelDataStart, numberOfBytesPerChannel));
                            break;
                        case BinaryFormat.int32_t:
                            numbers[channel][seqIndex] = BinaryPrimitives.ReadInt32LittleEndian(
                                data.Slice(channelDataStart, numberOfBytesPerChannel));
                            break;
                        case BinaryFormat.uint32_t:
                            numbers[channel][seqIndex] = BinaryPrimitives.ReadUInt32LittleEndian(
                                data.Slice(channelDataStart, numberOfBytesPerChannel));
                            break;
                        case BinaryFormat.float_t:
                            numbers[channel][seqIndex] = BinaryPrimitives.ReadSingleLittleEndian(
                                data.Slice(channelDataStart, numberOfBytesPerChannel));
                            break;
                    }
                }
            }

            // Calculate residue - data after the last complete sequence
            if (sequences.Count > 0)
            {
                var lastSequenceEnd = sequences[^1] + FrameStart.Length + numberOfBytePerSample;
                if (lastSequenceEnd < data.Length)
                {
                    residue = data.Slice(lastSequenceEnd).ToArray();
                }
            }
            else if (data.Length > 0)
            {
                // No complete sequences found, all data becomes residue
                residue = data.ToArray();
            }

            return new ParsedData(numbers, residue);
        }

        private List<int> FindCompleteSequences(Span<byte> data)
        {
            var sequences = new List<int>();
            int sequenceLength = FrameStart.Length + numberOfBytePerSample;
            
            // Search for frame start patterns
            for (int i = 0; i <= data.Length - sequenceLength; i++)
            {
                if (IsFrameStart(data, i))
                {
                    sequences.Add(i);
                    // Skip ahead by the sequence length to avoid overlapping detections
                    i += sequenceLength - 1;
                }
            }
            
            return sequences;
        }

        private bool IsFrameStart(Span<byte> data, int position)
        {
            if (position + FrameStart.Length > data.Length)
                return false;
                
            for (int i = 0; i < FrameStart.Length; i++)
            {
                if (data[position + i] != FrameStart[i])
                    return false;
            }
            return true;
        }

        private ParsedData ParseAsciiData(string textData)
        {
            var lines = textData.Split(FrameEnd, StringSplitOptions.RemoveEmptyEntries);
            var numbers = new List<double[]>();
            string residueText = "";

            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                string[] values = line.Trim().Split(Separator, StringSplitOptions.RemoveEmptyEntries);
                
                if (values.Length >= NumberOfChannels)
                {
                    double[] lineData = new double[NumberOfChannels];
                    bool validLine = true;
                    
                    for (int i = 0; i < NumberOfChannels; i++)
                    {
                        if (!double.TryParse(values[i].Trim(), out lineData[i]))
                        {
                            validLine = false;
                            break;
                        }
                    }
                    
                    if (validLine)
                    {
                        numbers.Add(lineData);
                    }
                }
                else if (values.Length > 0)
                {
                    // Incomplete line - treat as residue
                    residueText = line;
                }
            }

            // Convert to channel-based arrays
            double[][] channelData = new double[NumberOfChannels][];
            for (int ch = 0; ch < NumberOfChannels; ch++)
            {
                channelData[ch] = new double[numbers.Count];
                for (int sample = 0; sample < numbers.Count; sample++)
                {
                    channelData[ch][sample] = numbers[sample][ch];
                }
            }

            // Convert residue back to bytes
            byte[] residue = string.IsNullOrEmpty(residueText) ? null : System.Text.Encoding.UTF8.GetBytes(residueText);

            return new ParsedData(channelData, residue);
        }
    }

    public class ParsedData
    {
        public double[][] Data { get; init; }
        public byte[]? Residue { get; init; }
        public ParsedData(double[][] data, byte[] residue)
        {
            this.Data = data;
            this.Residue = residue;
        }
    }
}
