using PowerScope.Model;
using PowerScope.Model.Mcp;

namespace PowerScope.Tests
{
    /// <summary>
    /// IMcpHost implementation for tests: backs the MCP tool layer with
    /// DemoDataStream instances directly, without any WPF UI or dispatcher.
    /// </summary>
    internal class TestMcpHost : IMcpHost, IDisposable
    {
        private readonly List<Channel> _channels = new();
        private readonly List<IDataStream> _streams = new();
        private readonly object _lock = new();

        public IReadOnlyList<Channel> GetChannels()
        {
            lock (_lock)
            {
                return _channels.ToList();
            }
        }

        public int AddDemoStream(int numberOfChannels, int sampleRate, string signalType)
        {
            // Go through the same factory the application uses so the stream is
            // configured identically (e.g. up/down sampling disabled by default)
            StreamSettings settings = new StreamSettings
            {
                StreamSource = StreamSource.Demo,
                NumberOfChannels = numberOfChannels,
                DemoSampleRate = sampleRate,
                DemoSignalType = signalType
            };
            IDataStream stream = settings.CreateDataStream();
            stream.Connect();
            stream.StartStreaming();

            lock (_lock)
            {
                _streams.Add(stream);
                for (int i = 0; i < numberOfChannels; i++)
                {
                    ChannelSettings channelSettings = new ChannelSettings
                    {
                        Label = $"CH{_channels.Count + 1}",
                        Gain = 1.0,
                        Offset = 0.0,
                        IsEnabled = true
                    };
                    _channels.Add(new Channel(stream, i, channelSettings));
                }
            }

            return numberOfChannels;
        }

        // Trigger state for test host
        private bool _triggerEnabled = false;
        private bool _triggerSingleShot = false;
        private double _triggerLevel = 0.0;
        private string _triggerEdge = "Rising";

        private int _triggerPosition = 100;

        public TriggerSnapshot GetTriggerInfo() => new TriggerSnapshot
        {
            Enabled = _triggerEnabled,
            Mode = !_triggerEnabled ? "free_run" : _triggerSingleShot ? "single" : "normal",
            Edge = _triggerEdge,
            Level = _triggerLevel,
            Position = _triggerPosition,
            SourceChannelLabel = null,
            SourceChannelIndex = null
        };

        public void SetTrigger(bool? enableEdgeTrigger, bool? singleShot, double? level,
                               int? position, TriggerEdgeType? edge, bool channelSpecified, Channel channel)
        {
            if (enableEdgeTrigger.HasValue) _triggerEnabled = enableEdgeTrigger.Value;
            if (singleShot.HasValue) _triggerSingleShot = singleShot.Value;
            if (level.HasValue) _triggerLevel = level.Value;
            if (position.HasValue) _triggerPosition = position.Value;
            if (edge.HasValue) _triggerEdge = edge.Value.ToString();
        }

        public void SetChannelProperties(Channel channel, string label, bool? enabled, double? gain, double? offset)
        {
            if (label != null) channel.Settings.Label = label;
            if (enabled.HasValue) channel.Settings.IsEnabled = enabled.Value;
            if (gain.HasValue) channel.Settings.Gain = gain.Value;
            if (offset.HasValue) channel.Settings.Offset = offset.Value;
        }

        // Viewport state for test host (no real plot)
        private double _xMin = 0, _xMax = 10000;
        private double _yMin = -100, _yMax = 100;
        private bool _yAutoScale = false;

        public AxisRangeSnapshot GetXRange() =>
            new AxisRangeSnapshot { Min = _xMin, Max = _xMax, AutoScale = false };

        public void SetXRange(double min, double max) { _xMin = min; _xMax = max; }

        public AxisRangeSnapshot GetYRange() =>
            new AxisRangeSnapshot { Min = _yMin, Max = _yMax, AutoScale = _yAutoScale };

        public void SetYRange(double min, double max, bool autoScale)
        {
            _yAutoScale = autoScale;
            if (!autoScale) { _yMin = min; _yMax = max; }
        }

        public IReadOnlyList<string> LoadConfiguration(string filePath)
        {
            throw new NotSupportedException("load_config is not available in the test host");
        }

        public string ExportPlot(string filePath, int width, int height)
        {
            throw new NotSupportedException("capture_plot is not available in the test host");
        }

        public void RemoveAllStreams()
        {
            lock (_lock)
            {
                foreach (Channel channel in _channels)
                    channel.Dispose();
                _channels.Clear();

                foreach (IDataStream stream in _streams)
                {
                    stream.StopStreaming();
                    stream.Disconnect();
                    stream.Dispose();
                }
                _streams.Clear();
            }
        }

        public void RemoveStream(IDataStream stream)
        {
            lock (_lock)
            {
                _channels.RemoveAll(channel =>
                {
                    if (channel.OwnerStream != stream)
                        return false;
                    channel.Dispose();
                    return true;
                });

                if (_streams.Remove(stream))
                {
                    stream.StopStreaming();
                    stream.Disconnect();
                    stream.Dispose();
                }
            }
        }

        /// <summary>
        /// Blocks until the first channel has buffered at least minSamples samples.
        /// </summary>
        public void WaitForSamples(int minSamples, int timeoutSeconds = 10)
        {
            Channel channel = GetChannels()[0];
            double[] buffer = new double[minSamples];
            DateTime deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);

            while (DateTime.UtcNow < deadline)
            {
                if (channel.CopyLatestDataTo(buffer, minSamples) >= minSamples)
                    return;
                Thread.Sleep(50);
            }

            throw new TimeoutException($"Demo stream did not produce {minSamples} samples within {timeoutSeconds}s");
        }

        public void Dispose()
        {
            RemoveAllStreams();
        }
    }
}
