using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Threading.Tasks;
using System.Linq;
using System.Text;

namespace PowerScope.Model
{
    /// <summary>
    /// Result of file parsing operations
    /// </summary>
    public class FileParseResult
    {
        public bool IsSuccess { get; private set; }
        public string ErrorMessage { get; private set; }
        public FileHeader Header { get; private set; }
        public double[][] Data { get; private set; }

        private FileParseResult(bool isSuccess, string errorMessage, FileHeader header, double[][] data)
        {
            IsSuccess = isSuccess;
            ErrorMessage = errorMessage;
            Header = header;
            Data = data;
        }

        public static FileParseResult Success(FileHeader header, double[][] data)
        {
            return new FileParseResult(true, null, header, data);
        }

        public static FileParseResult Error(string errorMessage)
        {
            return new FileParseResult(false, errorMessage, null, null);
        }
    }

    /// <summary>
    /// Standard file header structure for PowerScope files
    /// Contains all metadata needed for reading and writing files
    /// </summary>
    public class FileHeader
    {
        public string FilePath { get; set; }
        public string FileVersion { get; set; } = FileIOManager.CURRENT_VERSION;
        public double SampleRate { get; set; } = 1000.0;
        public char Delimiter { get; set; } = ',';
        public List<string> ChannelLabels { get; set; } = new List<string>();
        public bool HasHeader { get; set; } = true;
        public long TotalSamples { get; set; }
        public DateTime RecordingStartTime { get; set; } = DateTime.Now;
        public string ParseStatus { get; set; }

        /// <summary>
        /// Number of channels in the file
        /// </summary>
        public int ChannelCount => ChannelLabels?.Count ?? 0;

        /// <summary>
        /// Create FileHeader from channel collection (for writing)
        /// </summary>
        public static FileHeader FromChannels(IEnumerable<Channel> channels, double sampleRate)
        {
            var channelList = channels.ToList();
            return new FileHeader
            {
                SampleRate = sampleRate,
                ChannelLabels = channelList.Select(ch => ch.Label).ToList(),
                RecordingStartTime = DateTime.Now
            };
        }
    }

    /// <summary>
    /// Centralized file I/O manager for PowerScope file operations
    /// Simplified implementation with clear separation of reading and writing
    /// </summary>
    public static class FileIOManager
    {
        public const string CURRENT_VERSION = "V1.0";
        
        #region File Reading

        /// <summary>
        /// Parse file and return header + data
        /// </summary>
        public static FileParseResult ParseFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                    return FileParseResult.Error("File not found");

                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length == 0)
                    return FileParseResult.Error("File is empty");

                // Read all lines
                string[] lines = File.ReadAllLines(filePath);
                if (lines.Length == 0)
                    return FileParseResult.Error("File is empty");

                // Parse header
                var header = ReadFileHeader(lines, filePath);
                if (header == null)
                    return FileParseResult.Error("Unable to parse file header");

                // Always load data - remove artificial size restrictions
                double[][] data = ReadFileData(lines, header);

                return FileParseResult.Success(header, data);
            }
            catch (Exception ex)
            {
                return FileParseResult.Error($"Error parsing file: {ex.Message}");
            }
        }

        /// <summary>
        /// Read and parse file header from lines using unified approach
        /// </summary>
        public static FileHeader ReadFileHeader(string[] lines, string filePath)
        {
            var header = new FileHeader { FilePath = filePath };
            var channelLabels = new List<string>();
            
            // Step 1: Parse optional PowerScope header (# comments)
            ParsePowerScopeHeader(lines, header);
            
            // Step 2: Find first non-comment line
            string firstDataLine = null;
            int firstDataLineIndex = -1;
            
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (!line.StartsWith("#") && !string.IsNullOrWhiteSpace(line))
                {
                    firstDataLine = line.Trim();
                    firstDataLineIndex = i;
                    break;
                }
            }
            
            if (firstDataLine == null)
            {
                header.ParseStatus = "No data found in file";
                return null;
            }
            
            // Step 3: Detect delimiter and parse columns
            header.Delimiter = DetectDelimiterChar(firstDataLine);
            string[] columns = firstDataLine.Split(header.Delimiter, StringSplitOptions.RemoveEmptyEntries);
            
            // Step 4: Determine if first line is column labels or data
            bool firstLineIsLabels = columns.Any(col => !IsNumericValue(col.Trim()));
            
            if (firstLineIsLabels)
            {
                // Use provided labels
                foreach (string col in columns)
                {
                    string cleanCol = col.Trim();
                    if (!string.IsNullOrEmpty(cleanCol))
                    {
                        channelLabels.Add(cleanCol);
                    }
                }
                header.HasHeader = true;
            }
            else
            {
                // Generate default labels (CH1, CH2, etc.)
                for (int i = 0; i < columns.Length; i++)
                {
                    channelLabels.Add($"CH{i + 1}");
                }
                header.HasHeader = false;
            }
            
            header.ChannelLabels = channelLabels;
            
            // Step 5: Count total data samples
            header.TotalSamples = CountDataSamples(lines, firstDataLineIndex, firstLineIsLabels);
            
            // Step 6: Generate status message
            string versionInfo = !string.IsNullOrEmpty(header.FileVersion) && header.FileVersion != "Unknown" 
                ? $"(Version: {header.FileVersion})" 
                : "(Generic CSV)";
                
            header.ParseStatus = $"Found {channelLabels.Count} channels, {header.TotalSamples:N0} samples at {header.SampleRate} Hz {versionInfo}";
            
            return channelLabels.Count > 0 ? header : null;
        }

        /// <summary>
        /// Convenience overload that reads the file and parses the header
        /// </summary>
        public static FileHeader ReadFileHeader(string filePath)
        {
            if (!File.Exists(filePath))
                return null;
                
            string[] lines = File.ReadAllLines(filePath);
            return ReadFileHeader(lines, filePath);
        }

        /// <summary>
        /// Parse PowerScope header lines (# comments) to extract metadata
        /// </summary>
        private static void ParsePowerScopeHeader(string[] lines, FileHeader header)
        {
            header.FileVersion = "Unknown"; // Default
            
            foreach (string line in lines)
            {
                if (!line.StartsWith("#"))
                    break; // No more header lines
                    
                // Parse PowerScope Version
                if (line.Contains("PowerScope Version") && line.Contains(":"))
                {
                    header.FileVersion = line.Split(':')[1].Trim();
                }
                // Parse Sample Rate (most important field)
                else if (line.Contains("Sample Rate") && line.Contains(":"))
                {
                    string rateStr = line.Split(':')[1].Trim().Split(' ')[0];
                    if (double.TryParse(rateStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double rate) && rate > 0)
                    {
                        header.SampleRate = rate;
                    }
                }
                // Parse Recording Started (nice-to-have)
                else if (line.Contains("Recording Started") && line.Contains(":"))
                {
                    string dateStr = line.Split(':', 2)[1].Trim();
                    if (DateTime.TryParse(dateStr, out DateTime recordingTime))
                    {
                        header.RecordingStartTime = recordingTime;
                    }
                }
            }
        }

        /// <summary>
        /// Detect and return the delimiter character from the first data line
        /// </summary>
        private static char DetectDelimiterChar(string line)
        {
            if (line.Contains(",")) return ',';
            if (line.Contains("\t")) return '\t';
            if (line.Contains(";")) return ';';
            if (line.Contains(" ")) return ' ';
            return ','; // Default fallback
        }

        /// <summary>
        /// Check if a string represents a numeric value
        /// </summary>
        private static bool IsNumericValue(string value)
        {
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
        }

        /// <summary>
        /// Count total data samples in the file
        /// </summary>
        private static long CountDataSamples(string[] lines, int firstDataLineIndex, bool firstLineIsLabels)
        {
            long count = 0;
            bool skipFirstDataLine = firstLineIsLabels;
            
            for (int i = firstDataLineIndex; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.StartsWith("#") || string.IsNullOrWhiteSpace(line))
                    continue;
                    
                if (skipFirstDataLine)
                {
                    skipFirstDataLine = false;
                    continue; // Skip the label row
                }
                
                count++;
            }
            
            return count;
        }

        /// <summary>
        /// Read data from file lines using header information
        /// Simplified unified approach for both PowerScope and generic CSV files
        /// </summary>
        public static double[][] ReadFileData(string[] lines, FileHeader header)
        {
            // Step 1: Collect all data lines (skip comments)
            var dataLines = lines.Where(l => !l.StartsWith("#") && !string.IsNullOrWhiteSpace(l)).ToArray();
            
            if (dataLines.Length == 0)
                return new double[header.ChannelCount][];
                
            // Step 2: Skip label row if present
            int startIndex = header.HasHeader ? 1 : 0;
            int actualDataLines = dataLines.Length - startIndex;
            
            if (actualDataLines <= 0)
                return new double[header.ChannelCount][];
                
            // Step 3: Initialize data arrays
            var data = new double[header.ChannelCount][];
            for (int i = 0; i < header.ChannelCount; i++)
                data[i] = new double[actualDataLines];
            
            // Step 4: Parse each data line
            for (int row = 0; row < actualDataLines; row++)
            {
                string[] values = dataLines[startIndex + row].Split(header.Delimiter, StringSplitOptions.RemoveEmptyEntries);
                
                for (int col = 0; col < header.ChannelCount; col++)
                {
                    if (col < values.Length && 
                        double.TryParse(values[col], NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                    {
                        data[col][row] = value;
                    }
                    else
                    {
                        data[col][row] = 0.0; // Default for missing or invalid values
                    }
                }
            }

            return data;
        }

        #endregion

        #region File Writing

        /// <summary>
        /// Write PowerScope format file header from FileHeader object
        /// </summary>
        public static void WriteFileHeader(StreamWriter writer, FileHeader header)
        {
            writer.WriteLine($"# PowerScope Version: {header.FileVersion}");
            writer.WriteLine($"# Sample Rate (Hz): {header.SampleRate.ToString(CultureInfo.InvariantCulture)}");
            writer.WriteLine($"# Recording Started: {header.RecordingStartTime:yyyy-MM-dd HH:mm:ss}");
            writer.WriteLine($"# Total Channels: {header.ChannelCount}");
            writer.WriteLine("# Data Format: Raw values only, samples are sequential and equally spaced");
            writer.WriteLine("# Channel Information:");

            for (int i = 0; i < header.ChannelLabels.Count; i++)
            {
                writer.WriteLine($"# {header.ChannelLabels[i]}: Stream=Unknown, Index={i}");
            }

            writer.WriteLine("#");
            writer.WriteLine(string.Join(header.Delimiter, header.ChannelLabels));
        }

        /// <summary>
        /// Write PowerScope format file header for channel collection (convenience method)
        /// </summary>
        public static void WriteFileHeader(StreamWriter writer, IEnumerable<Channel> channels, double sampleRate)
        {
            var header = FileHeader.FromChannels(channels, sampleRate);
            
            // Add detailed channel information for V1 format
            var channelList = channels.ToList();
            writer.WriteLine($"# PowerScope Version: {header.FileVersion}");
            writer.WriteLine($"# Sample Rate (Hz): {sampleRate.ToString(CultureInfo.InvariantCulture)}");
            writer.WriteLine($"# Recording Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            writer.WriteLine($"# Total Channels: {channelList.Count}");
            writer.WriteLine("# Data Format: Raw values only, samples are sequential and equally spaced");
            writer.WriteLine("# Channel Information:");

            for (int i = 0; i < channelList.Count; i++)
            {
                var ch = channelList[i];
                writer.WriteLine($"# {ch.Label}: Stream={ch.StreamType}, Index={ch.LocalChannelIndex}");
            }

            writer.WriteLine("#");
            writer.WriteLine(string.Join(",", channelList.Select(ch => ch.Label)));
        }

        /// <summary>
        /// Append data rows to file (for recording)
        /// </summary>
        public static void AppendDataRows(StreamWriter writer, double[][] channelData)
        {
            if (channelData.Length == 0) return;

            int sampleCount = channelData[0].Length;
            int channelCount = channelData.Length;
            
            for (int sample = 0; sample < sampleCount; sample++)
            {
                var values = new string[channelCount];
                for (int channel = 0; channel < channelCount; channel++)
                {
                    values[channel] = channelData[channel][sample].ToString("F6", CultureInfo.InvariantCulture);
                }
                writer.WriteLine(string.Join(",", values));
            }
        }

        #endregion
    }
}