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
        /// Dispatches a tools/call request. Throws McpToolException for
        /// expected errors; the caller maps those to isError tool results.
        /// </summary>
        public JsonObject CallTool(string name, JsonObject arguments)
        {
            switch (name)
            {
                case "get_status": return GetStatus();
                case "get_channel_info": return GetChannelInfo(arguments);
                case "set_channel": return SetChannel(arguments);
                case "set_trigger": return SetTrigger(arguments);
                case "get_x_range": return GetXRange();
                case "set_x_range": return SetXRange(arguments);
                case "get_y_range": return GetYRange();
                case "set_y_range": return SetYRange(arguments);
                case "read_samples": return ReadSamples(arguments);
                case "get_measurements": return GetMeasurements(arguments);
                case "clear_data": return ClearData();
                case "add_demo_stream": return AddDemoStream(arguments);
                case "load_config": return LoadConfig(arguments);
                case "remove_all_streams": return RemoveAllStreams();
                case "remove_stream": return RemoveStream(arguments);
                case "capture_plot": return CapturePlot(arguments);
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

            TriggerSnapshot trigger = _host.GetTriggerInfo();
            JsonObject triggerObj = new JsonObject
            {
                ["mode"] = trigger.Mode,
                ["edge"] = trigger.Edge,
                ["level"] = trigger.Level,
                ["position"] = trigger.Position,
                ["source_channel_label"] = trigger.SourceChannelLabel,
                ["source_channel_index"] = trigger.SourceChannelIndex.HasValue
                    ? (JsonNode)trigger.SourceChannelIndex.Value
                    : null
            };

            return new JsonObject
            {
                ["streams"] = streamArray,
                ["total_channels"] = channels.Count,
                ["trigger"] = triggerObj
            };
        }

        private JsonObject GetChannelInfo(JsonObject args)
        {
            (Channel channel, int index) = ResolveChannel(args);

            string colorHex = $"#{channel.Settings.Color.R:X2}{channel.Settings.Color.G:X2}{channel.Settings.Color.B:X2}";
            string filterName = channel.Settings.Filter?.GetType().Name ?? "None";

            return new JsonObject
            {
                ["index"] = index,
                ["label"] = channel.Settings.Label,
                ["enabled"] = channel.Settings.IsEnabled,
                ["gain"] = channel.Settings.Gain,
                ["offset"] = channel.Settings.Offset,
                ["color"] = colorHex,
                ["filter"] = filterName,
                ["stream_type"] = channel.StreamType
            };
        }

        private JsonObject SetChannel(JsonObject args)
        {
            (Channel channel, int index) = ResolveChannel(args);

            double? gain = null;
            double? offset = null;

            JsonNode gainNode = args?["gain"];
            if (gainNode != null)
            {
                if (!TryGetNumber(gainNode, out double gainValue))
                    throw new McpToolException("Argument 'gain' must be a number.");
                if (gainValue == 0.0)
                    throw new McpToolException("Gain must be non-zero.");
                gain = gainValue;
            }

            JsonNode offsetNode = args?["offset"];
            if (offsetNode != null)
            {
                if (!TryGetNumber(offsetNode, out double offsetValue))
                    throw new McpToolException("Argument 'offset' must be a number.");
                offset = offsetValue;
            }

            string label = null;
            JsonNode labelNode = args?["label"];
            if (labelNode != null)
            {
                label = labelNode.GetValue<string>();
                if (string.IsNullOrWhiteSpace(label))
                    throw new McpToolException("Argument 'label' must be a non-empty string.");
            }

            bool? enabled = null;
            JsonNode enabledNode = args?["enabled"];
            if (enabledNode != null)
            {
                if (enabledNode.GetValueKind() != JsonValueKind.True
                    && enabledNode.GetValueKind() != JsonValueKind.False)
                    throw new McpToolException("Argument 'enabled' must be a boolean.");
                enabled = enabledNode.GetValue<bool>();
            }

            if (gain == null && offset == null && label == null && enabled == null)
                throw new McpToolException("Provide at least one of: 'label', 'enabled', 'gain', 'offset'.");

            _host.SetChannelProperties(channel, label, enabled, gain, offset);

            return new JsonObject
            {
                ["channel"] = index,
                ["label"] = channel.Settings.Label,
                ["enabled"] = channel.Settings.IsEnabled,
                ["gain"] = channel.Settings.Gain,
                ["offset"] = channel.Settings.Offset
            };
        }

        private JsonObject SetTrigger(JsonObject args)
        {
            bool? enableEdgeTrigger = null;
            bool? singleShot = null;

            JsonNode modeNode = args?["mode"];
            if (modeNode != null)
            {
                string mode = modeNode.GetValue<string>().ToLowerInvariant().Replace("-", "_");
                switch (mode)
                {
                    case "free_run": enableEdgeTrigger = false; break;
                    case "normal":   enableEdgeTrigger = true; singleShot = false; break;
                    case "single":   enableEdgeTrigger = true; singleShot = true; break;
                    default:
                        throw new McpToolException(
                            $"Unknown trigger mode '{mode}'. Valid values: 'free_run', 'normal', 'single'.");
                }
            }

            double? level = null;
            JsonNode levelNode = args?["level"];
            if (levelNode != null)
            {
                if (!TryGetNumber(levelNode, out double levelValue))
                    throw new McpToolException("Argument 'level' must be a number.");
                level = levelValue;
            }

            int? position = null;
            JsonNode positionNode = args?["position"];
            if (positionNode != null)
            {
                if (!TryGetNumber(positionNode, out double positionValue))
                    throw new McpToolException("Argument 'position' must be a number.");
                position = (int)positionValue;
            }

            TriggerEdgeType? edge = null;
            JsonNode edgeNode = args?["edge"];
            if (edgeNode != null)
            {
                string edgeStr = edgeNode.GetValue<string>().ToLowerInvariant();
                if (edgeStr == "rising") edge = TriggerEdgeType.Rising;
                else if (edgeStr == "falling") edge = TriggerEdgeType.Falling;
                else throw new McpToolException(
                    $"Unknown edge '{edgeStr}'. Valid values: 'rising', 'falling'.");
            }

            // Channel: present in args → change it; absent → leave unchanged.
            // Value of "auto" or null → set to auto (first enabled channel).
            bool channelSpecified = false;
            Channel channel = null;
            if (args != null && args.ContainsKey("channel"))
            {
                channelSpecified = true;
                JsonNode channelArg = args["channel"];
                bool isAutoRequest = channelArg == null
                    || channelArg.GetValueKind() == JsonValueKind.Null
                    || (channelArg.GetValueKind() == JsonValueKind.String
                        && channelArg.GetValue<string>().Equals("auto", StringComparison.OrdinalIgnoreCase));

                if (!isAutoRequest)
                    (channel, _) = ResolveChannel(args);
            }

            if (!enableEdgeTrigger.HasValue && !singleShot.HasValue && !level.HasValue
                && !position.HasValue && !edge.HasValue && !channelSpecified)
                throw new McpToolException(
                    "Provide at least one of: 'mode', 'level', 'position', 'edge', 'channel'.");

            _host.SetTrigger(enableEdgeTrigger, singleShot, level, position, edge, channelSpecified, channel);

            return GetStatus();
        }

        private JsonObject GetXRange()
        {
            AxisRangeSnapshot range = _host.GetXRange();
            return new JsonObject
            {
                ["x_min"] = range.Min,
                ["x_max"] = range.Max,
                ["note"] = "Range is in sample indices. Reflects the current viewport, which may differ from the configured sample window if the user has zoomed or panned."
            };
        }

        private JsonObject SetXRange(JsonObject args)
        {
            AxisRangeSnapshot current = _host.GetXRange();
            double min = current.Min;
            double max = current.Max;

            JsonNode minNode = args?["x_min"];
            if (minNode != null)
            {
                if (!TryGetNumber(minNode, out double v))
                    throw new McpToolException("Argument 'x_min' must be a number.");
                min = v;
            }

            JsonNode maxNode = args?["x_max"];
            if (maxNode != null)
            {
                if (!TryGetNumber(maxNode, out double v))
                    throw new McpToolException("Argument 'x_max' must be a number.");
                max = v;
            }

            if (min >= max)
                throw new McpToolException($"x_min ({min}) must be less than x_max ({max}).");

            _host.SetXRange(min, max);
            return GetXRange();
        }

        private JsonObject GetYRange()
        {
            AxisRangeSnapshot range = _host.GetYRange();
            return new JsonObject
            {
                ["y_min"] = range.Min,
                ["y_max"] = range.Max,
                ["auto_scale"] = range.AutoScale,
                ["note"] = "When auto_scale is true, y_min/y_max are overridden each frame and reflect the last auto-scaled values."
            };
        }

        private JsonObject SetYRange(JsonObject args)
        {
            JsonNode autoScaleNode = args?["auto_scale"];
            if (autoScaleNode != null && autoScaleNode.GetValueKind() == JsonValueKind.True)
            {
                _host.SetYRange(0, 0, autoScale: true);
                return GetYRange();
            }

            AxisRangeSnapshot current = _host.GetYRange();
            double min = current.Min;
            double max = current.Max;

            JsonNode minNode = args?["y_min"];
            if (minNode != null)
            {
                if (!TryGetNumber(minNode, out double v))
                    throw new McpToolException("Argument 'y_min' must be a number.");
                min = v;
            }

            JsonNode maxNode = args?["y_max"];
            if (maxNode != null)
            {
                if (!TryGetNumber(maxNode, out double v))
                    throw new McpToolException("Argument 'y_max' must be a number.");
                max = v;
            }

            if (min >= max)
                throw new McpToolException($"y_min ({min}) must be less than y_max ({max}).");

            _host.SetYRange(min, max, autoScale: false);
            return GetYRange();
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

            IReadOnlyList<string> skippedStreams = _host.LoadConfiguration(filePath);

            JsonObject result = GetStatus();
            result["loaded"] = filePath;

            JsonArray skippedArray = new JsonArray();
            foreach (string skipped in skippedStreams)
                skippedArray.Add(skipped);
            result["skipped_streams"] = skippedArray;
            if (skippedArray.Count > 0)
                result["warning"] = $"{skippedArray.Count} stream(s) could not be restored. See 'skipped_streams' for details.";

            return result;
        }

        private JsonObject RemoveAllStreams()
        {
            _host.RemoveAllStreams();
            return new JsonObject { ["removed"] = true };
        }

        private JsonObject RemoveStream(JsonObject args)
        {
            (Channel channel, _) = ResolveChannel(args);
            IDataStream stream = channel.OwnerStream;
            string streamType = stream.StreamType;

            int removedChannels = 0;
            foreach (Channel c in _host.GetChannels())
            {
                if (c.OwnerStream == stream)
                    removedChannels++;
            }

            _host.RemoveStream(stream);

            return new JsonObject
            {
                ["removed"] = true,
                ["stream_type"] = streamType,
                ["channels_removed"] = removedChannels
            };
        }

        private JsonObject CapturePlot(JsonObject args)
        {
            string filePath = GetStringArg(args, "file_path", null);
            int width = GetIntArg(args, "width", 1920, 1, 7680);
            int height = GetIntArg(args, "height", 1080, 1, 4320);

            try
            {
                string saved = _host.ExportPlot(filePath, width, height);

                // Gather plot context immediately after rendering
                AxisRangeSnapshot xRange = _host.GetXRange();
                AxisRangeSnapshot yRange = _host.GetYRange();
                TriggerSnapshot trigger = _host.GetTriggerInfo();
                IReadOnlyList<Channel> channels = _host.GetChannels();

                // Visible sample index range, clamped to non-negative
                int sliceStart = (int)Math.Max(0, Math.Floor(xRange.Min));
                int sliceEnd = (int)Math.Ceiling(xRange.Max);
                int fetchCount = Math.Min(Math.Max(sliceEnd, 1), MaxRawSamples);

                // Per-channel stats for enabled channels over the visible slice
                JsonArray channelArray = new JsonArray();
                foreach (Channel channel in channels)
                {
                    if (!channel.Settings.IsEnabled) continue;

                    double[] buffer = new double[fetchCount];
                    int copied = channel.CopyLatestDataTo(buffer, fetchCount);

                    int start = Math.Min(sliceStart, copied);
                    int end = Math.Min(sliceEnd, copied);
                    int count = end - start;

                    if (count <= 0)
                    {
                        channelArray.Add(new JsonObject
                        {
                            ["label"] = channel.Settings.Label,
                            ["min"] = (JsonNode)null,
                            ["max"] = (JsonNode)null,
                            ["mean"] = (JsonNode)null,
                            ["clipped"] = false
                        });
                        continue;
                    }

                    double min = double.MaxValue, max = double.MinValue, sum = 0;
                    bool clipped = false;
                    for (int j = start; j < end; j++)
                    {
                        double v = buffer[j];
                        if (v < min) min = v;
                        if (v > max) max = v;
                        sum += v;
                        if (!yRange.AutoScale && (v < yRange.Min || v > yRange.Max))
                            clipped = true;
                    }

                    channelArray.Add(new JsonObject
                    {
                        ["label"] = channel.Settings.Label,
                        ["min"] = Math.Round(min, 4),
                        ["max"] = Math.Round(max, 4),
                        ["mean"] = Math.Round(sum / count, 4),
                        ["clipped"] = clipped
                    });
                }

                bool triggerInView = trigger.Enabled
                    && trigger.Position >= xRange.Min
                    && trigger.Position <= xRange.Max;

                return new JsonObject
                {
                    ["file_path"] = saved,
                    ["width"] = width,
                    ["height"] = height,
                    ["x_range"] = new JsonObject
                    {
                        ["min"] = Math.Round(xRange.Min, 2),
                        ["max"] = Math.Round(xRange.Max, 2),
                        ["unit"] = "samples"
                    },
                    ["y_range"] = new JsonObject
                    {
                        ["min"] = Math.Round(yRange.Min, 4),
                        ["max"] = Math.Round(yRange.Max, 4),
                        ["auto_scale"] = yRange.AutoScale
                    },
                    ["trigger"] = new JsonObject
                    {
                        ["mode"] = trigger.Mode,
                        ["position"] = trigger.Position,
                        ["in_view"] = triggerInView
                    },
                    ["channels"] = channelArray
                };
            }
            catch (NotSupportedException ex)
            {
                throw new McpToolException(ex.Message);
            }
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

        #endregion
    }
}
