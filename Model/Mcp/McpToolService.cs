using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PowerScope.Model.Mcp
{
    /// <summary>
    /// Thrown by tool implementations for expected, user-correctable errors
    /// (bad arguments, unknown channel, ...). The MCP server reports these as
    /// tool results with isError=true instead of JSON-RPC protocol errors.
    /// </summary>
    public class McpToolException : Exception
    {
        public McpToolException(string message) : base(message) { }
    }

    /// <summary>
    /// Implements the MCP tools exposed by PowerScope. Each tool returns a
    /// JsonObject that the server serializes into the tool-call result.
    /// Independent of the transport (see McpServer) and of the UI (see IMcpHost),
    /// so it can be tested directly against a DemoDataStream.
    /// </summary>
    public class McpToolService
    {
        // Raw samples fetched from a ring buffer per request (matches default plot buffer size)
        private const int MaxRawSamples = 5_000_000;
        // Samples returned over the wire after decimation - keeps JSON responses manageable
        private const int MaxReturnedSamples = 100_000;

        private readonly IMcpHost _host;

        public McpToolService(IMcpHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
        }

        /// <summary>
        /// Tool definitions for the MCP tools/list response.
        /// Built fresh on every call because JsonNode instances cannot be re-parented.
        /// </summary>
        public JsonArray GetToolDefinitions()
        {
            return new JsonArray
            {
                Tool("get_status",
                    "Get the current state of PowerScope: all active data streams (type, connection state, sample rate, total samples acquired) and their channels (index, label, enabled, gain, offset). Call this first to discover available channels.",
                    new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }),

                Tool("read_samples",
                    "Read the latest samples from a channel's ring buffer. Samples are ordered oldest to newest; the last element is the most recent sample. Use 'decimate' to reduce the number of returned points for long captures.",
                    new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["channel"] = ChannelArgSchema(),
                            ["count"] = IntArgSchema($"Number of raw samples to fetch (default 1000, max {MaxRawSamples})."),
                            ["decimate"] = IntArgSchema("Return only every Nth sample (default 1 = all). The response contains at most count/decimate values.")
                        },
                        ["required"] = new JsonArray { "channel" }
                    }),

                Tool("get_measurements",
                    "Compute statistics over the latest samples of a channel: min, max, mean, RMS, standard deviation, peak-to-peak and an estimated fundamental frequency (from mean-crossings). Useful for checking signal levels, ripple and steady-state behavior without transferring raw data.",
                    new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["channel"] = ChannelArgSchema(),
                            ["count"] = IntArgSchema($"Number of latest samples to analyze (default 10000, max {MaxRawSamples}).")
                        },
                        ["required"] = new JsonArray { "channel" }
                    }),

                Tool("clear_data",
                    "Clear the ring buffers of all streams (discards acquired samples, streams keep running). Call this right before provoking a transient so read_samples afterwards contains only the event of interest.",
                    new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }),

                Tool("add_demo_stream",
                    "Add a synthetic demo data stream (no hardware required). Useful for testing the tool chain before connecting real hardware.",
                    new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["num_channels"] = IntArgSchema("Number of channels (default 4)."),
                            ["sample_rate"] = IntArgSchema("Sample rate in Hz (default 10000)."),
                            ["signal_type"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["description"] = "One of: 'Sine Wave', 'Square Wave', 'Triangle Wave', 'Random Noise', 'Mixed Signals', 'Chirp Signal', 'Tones', 'sin(x)/x'. Default 'Sine Wave'."
                            }
                        }
                    }),

                Tool("load_config",
                    "Load a PowerScope session configuration XML file (as saved via File > Save Settings). This creates and starts the configured streams (serial/USB/audio/demo) with all channel settings - the way to connect to real hardware that was previously configured in the GUI.",
                    new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["file_path"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["description"] = "Absolute path to the PowerScope settings XML file."
                            }
                        },
                        ["required"] = new JsonArray { "file_path" }
                    }),

                Tool("remove_all_streams",
                    "Stop, disconnect and remove all active streams and their channels.",
                    new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() })
            };
        }

        /// <summary>
        /// Dispatches a tools/call request. Throws McpToolException for
        /// expected errors; the caller maps those to isError tool results.
        /// </summary>
        public JsonObject CallTool(string name, JsonObject arguments)
        {
            switch (name)
            {
                case "get_status": return GetStatus();
                case "read_samples": return ReadSamples(arguments);
                case "get_measurements": return GetMeasurements(arguments);
                case "clear_data": return ClearData();
                case "add_demo_stream": return AddDemoStream(arguments);
                case "load_config": return LoadConfig(arguments);
                case "remove_all_streams": return RemoveAllStreams();
                default:
                    throw new McpToolException($"Unknown tool '{name}'.");
            }
        }

        #region Tool implementations

        private JsonObject GetStatus()
        {
            IReadOnlyList<Channel> channels = _host.GetChannels();

            // Group channels by owner stream, preserving global channel indices
            List<IDataStream> streams = new List<IDataStream>();
            Dictionary<IDataStream, JsonArray> streamChannels = new Dictionary<IDataStream, JsonArray>();

            for (int i = 0; i < channels.Count; i++)
            {
                Channel channel = channels[i];
                if (!streamChannels.ContainsKey(channel.OwnerStream))
                {
                    streams.Add(channel.OwnerStream);
                    streamChannels[channel.OwnerStream] = new JsonArray();
                }

                streamChannels[channel.OwnerStream].Add(new JsonObject
                {
                    ["index"] = i,
                    ["label"] = channel.Settings.Label,
                    ["enabled"] = channel.Settings.IsEnabled,
                    ["gain"] = channel.Settings.Gain,
                    ["offset"] = channel.Settings.Offset
                });
            }

            JsonArray streamArray = new JsonArray();
            foreach (IDataStream stream in streams)
            {
                streamArray.Add(new JsonObject
                {
                    ["type"] = stream.StreamType,
                    ["status"] = stream.StatusMessage,
                    ["connected"] = stream.IsConnected,
                    ["streaming"] = stream.IsStreaming,
                    ["sample_rate_hz"] = stream.SampleRate,
                    ["total_samples"] = stream.TotalSamples,
                    ["channels"] = streamChannels[stream]
                });
            }

            return new JsonObject
            {
                ["streams"] = streamArray,
                ["total_channels"] = channels.Count
            };
        }

        private JsonObject ReadSamples(JsonObject args)
        {
            int count = GetIntArg(args, "count", 1000, 1, MaxRawSamples);
            int decimate = GetIntArg(args, "decimate", 1, 1, 1_000_000);

            if (count / decimate > MaxReturnedSamples)
                throw new McpToolException(
                    $"count/decimate must not exceed {MaxReturnedSamples} returned samples. Increase 'decimate' or reduce 'count'.");

            (Channel channel, int index) = ResolveChannel(args);

            double[] buffer = new double[count];
            int copied = channel.CopyLatestDataTo(buffer, count);

            JsonArray samples = new JsonArray();
            for (int i = 0; i < copied; i += decimate)
                samples.Add(buffer[i]);

            return new JsonObject
            {
                ["channel"] = index,
                ["label"] = channel.Settings.Label,
                ["stream_type"] = channel.StreamType,
                ["sample_rate_hz"] = channel.OwnerStream.SampleRate,
                ["requested"] = count,
                ["copied"] = copied,
                ["decimate"] = decimate,
                ["samples"] = samples,
                ["note"] = "Samples ordered oldest to newest; the last element is the most recent sample."
            };
        }

        private JsonObject GetMeasurements(JsonObject args)
        {
            int count = GetIntArg(args, "count", 10_000, 2, MaxRawSamples);
            (Channel channel, int index) = ResolveChannel(args);

            double[] buffer = new double[count];
            int copied = channel.CopyLatestDataTo(buffer, count);

            if (copied < 2)
                throw new McpToolException(
                    $"Channel '{channel.Settings.Label}' has only {copied} samples buffered. Wait for the stream to acquire data.");

            double min = double.MaxValue, max = double.MinValue, sum = 0, sumSquares = 0;
            for (int i = 0; i < copied; i++)
            {
                double v = buffer[i];
                if (v < min) min = v;
                if (v > max) max = v;
                sum += v;
                sumSquares += v * v;
            }

            double mean = sum / copied;
            double rms = Math.Sqrt(sumSquares / copied);
            double variance = sumSquares / copied - mean * mean;
            double std = Math.Sqrt(Math.Max(0, variance));

            double sampleRate = channel.OwnerStream.SampleRate;

            // Fundamental frequency estimate from rising mean-crossings
            int crossings = 0, firstCrossing = -1, lastCrossing = -1;
            for (int i = 1; i < copied; i++)
            {
                if (buffer[i - 1] < mean && buffer[i] >= mean)
                {
                    crossings++;
                    if (firstCrossing < 0) firstCrossing = i;
                    lastCrossing = i;
                }
            }

            JsonNode frequency = null;
            if (crossings >= 2 && sampleRate > 0 && lastCrossing > firstCrossing)
            {
                double spanSeconds = (lastCrossing - firstCrossing) / sampleRate;
                frequency = (crossings - 1) / spanSeconds;
            }

            return new JsonObject
            {
                ["channel"] = index,
                ["label"] = channel.Settings.Label,
                ["samples_analyzed"] = copied,
                ["sample_rate_hz"] = sampleRate,
                ["duration_s"] = sampleRate > 0 ? copied / sampleRate : (JsonNode)null,
                ["min"] = min,
                ["max"] = max,
                ["mean"] = mean,
                ["rms"] = rms,
                ["std_dev"] = std,
                ["peak_to_peak"] = max - min,
                ["frequency_hz_estimate"] = frequency
            };
        }

        private JsonObject ClearData()
        {
            IReadOnlyList<Channel> channels = _host.GetChannels();

            List<IDataStream> uniqueStreams = new List<IDataStream>();
            foreach (Channel channel in channels)
            {
                if (!uniqueStreams.Contains(channel.OwnerStream))
                    uniqueStreams.Add(channel.OwnerStream);
            }

            foreach (IDataStream stream in uniqueStreams)
                stream.clearData();

            return new JsonObject
            {
                ["cleared"] = true,
                ["streams_cleared"] = uniqueStreams.Count
            };
        }

        private JsonObject AddDemoStream(JsonObject args)
        {
            int numChannels = GetIntArg(args, "num_channels", 4, 1, 32);
            int sampleRate = GetIntArg(args, "sample_rate", 10_000, 1, 10_000_000);
            string signalType = GetStringArg(args, "signal_type", "Sine Wave");

            int created = _host.AddDemoStream(numChannels, sampleRate, signalType);

            JsonObject result = GetStatus();
            result["created_channels"] = created;
            return result;
        }

        private JsonObject LoadConfig(JsonObject args)
        {
            string filePath = GetStringArg(args, "file_path", null);
            if (string.IsNullOrWhiteSpace(filePath))
                throw new McpToolException("Missing required argument 'file_path'.");
            if (!File.Exists(filePath))
                throw new McpToolException($"Configuration file not found: {filePath}");

            _host.LoadConfiguration(filePath);

            JsonObject result = GetStatus();
            result["loaded"] = filePath;
            return result;
        }

        private JsonObject RemoveAllStreams()
        {
            _host.RemoveAllStreams();
            return new JsonObject { ["removed"] = true };
        }

        #endregion

        #region Helpers

        private (Channel channel, int index) ResolveChannel(JsonObject args)
        {
            IReadOnlyList<Channel> channels = _host.GetChannels();
            if (channels.Count == 0)
                throw new McpToolException("No channels available. Add a stream first (add_demo_stream or load_config).");

            JsonNode channelArg = args?["channel"];
            if (channelArg == null)
                throw new McpToolException("Missing required argument 'channel' (index or label). Use get_status to list channels.");

            if (channelArg.GetValueKind() == JsonValueKind.Number && TryGetNumber(channelArg, out double numericChannel))
            {
                int index = (int)numericChannel;
                if (index < 0 || index >= channels.Count)
                    throw new McpToolException($"Channel index {index} out of range (0..{channels.Count - 1}).");
                return (channels[index], index);
            }

            string label = channelArg.GetValue<string>();
            for (int i = 0; i < channels.Count; i++)
            {
                if (string.Equals(channels[i].Settings.Label, label, StringComparison.OrdinalIgnoreCase))
                    return (channels[i], i);
            }

            if (int.TryParse(label, out int parsedIndex) && parsedIndex >= 0 && parsedIndex < channels.Count)
                return (channels[parsedIndex], parsedIndex);

            List<string> labels = new List<string>();
            foreach (Channel channel in channels)
                labels.Add(channel.Settings.Label);
            throw new McpToolException($"Channel '{label}' not found. Available channels: {string.Join(", ", labels)}");
        }

        /// <summary>
        /// Extracts a numeric value from a JsonNode regardless of whether it is
        /// backed by a parsed JsonElement (HTTP requests) or an in-memory CLR value.
        /// </summary>
        private static bool TryGetNumber(JsonNode node, out double value)
        {
            if (node is JsonValue jsonValue)
            {
                if (jsonValue.TryGetValue(out value)) return true;
                if (jsonValue.TryGetValue(out int intValue)) { value = intValue; return true; }
                if (jsonValue.TryGetValue(out long longValue)) { value = longValue; return true; }
            }
            value = 0;
            return false;
        }

        private static int GetIntArg(JsonObject args, string name, int defaultValue, int min, int max)
        {
            JsonNode node = args?[name];
            if (node == null)
                return defaultValue;

            if (!TryGetNumber(node, out double value))
                throw new McpToolException($"Argument '{name}' must be a number.");

            int result = (int)value;
            if (result < min || result > max)
                throw new McpToolException($"Argument '{name}' must be between {min} and {max}.");
            return result;
        }

        private static string GetStringArg(JsonObject args, string name, string defaultValue)
        {
            JsonNode node = args?[name];
            if (node == null)
                return defaultValue;

            try
            {
                return node.GetValue<string>();
            }
            catch
            {
                throw new McpToolException($"Argument '{name}' must be a string.");
            }
        }

        private static JsonObject Tool(string name, string description, JsonObject inputSchema)
        {
            return new JsonObject
            {
                ["name"] = name,
                ["description"] = description,
                ["inputSchema"] = inputSchema
            };
        }

        private static JsonObject ChannelArgSchema()
        {
            return new JsonObject
            {
                ["type"] = new JsonArray { "integer", "string" },
                ["description"] = "Channel to read: global channel index (from get_status) or channel label (e.g. 'CH1')."
            };
        }

        private static JsonObject IntArgSchema(string description)
        {
            return new JsonObject
            {
                ["type"] = "integer",
                ["description"] = description
            };
        }

        #endregion
    }
}
