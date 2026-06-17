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
