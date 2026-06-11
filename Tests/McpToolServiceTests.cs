using System.Text.Json.Nodes;
using PowerScope.Model.Mcp;
using Xunit;

namespace PowerScope.Tests
{
    /// <summary>
    /// Tests the MCP tool layer directly (no HTTP) against DemoDataStream.
    /// The demo waveforms have known analytical properties, e.g. a sine wave of
    /// amplitude 1000 has RMS = 1000/sqrt(2) = 707 and peak-to-peak = 2000,
    /// which lets us verify the whole acquisition path end to end.
    /// </summary>
    public class McpToolServiceTests
    {
        private const int SampleRate = 5000;

        private static (McpToolService service, TestMcpHost host) CreateServiceWithSineData()
        {
            TestMcpHost host = new TestMcpHost();
            host.AddDemoStream(2, SampleRate, "Sine Wave");
            host.WaitForSamples(2000);
            return (new McpToolService(host), host);
        }

        [Fact]
        public void GetStatus_ReportsDemoStreamAndChannels()
        {
            (McpToolService service, TestMcpHost host) = CreateServiceWithSineData();
            using (host)
            {
                JsonObject status = service.CallTool("get_status", new JsonObject());

                Assert.Equal(2, (int)status["total_channels"]);

                JsonArray streams = (JsonArray)status["streams"];
                Assert.Single(streams);
                Assert.Equal("Demo", (string)streams[0]["type"]);
                Assert.True((bool)streams[0]["streaming"]);
                Assert.Equal(SampleRate, (double)streams[0]["sample_rate_hz"]);

                JsonArray channels = (JsonArray)streams[0]["channels"];
                Assert.Equal(2, channels.Count);
                Assert.Equal("CH1", (string)channels[0]["label"]);
                Assert.Equal("CH2", (string)channels[1]["label"]);
            }
        }

        [Fact]
        public void ReadSamples_ReturnsRequestedCountWithPlausibleValues()
        {
            (McpToolService service, TestMcpHost host) = CreateServiceWithSineData();
            using (host)
            {
                JsonObject result = service.CallTool("read_samples", new JsonObject
                {
                    ["channel"] = 0,
                    ["count"] = 1000
                });

                Assert.Equal(1000, (int)result["copied"]);
                JsonArray samples = (JsonArray)result["samples"];
                Assert.Equal(1000, samples.Count);

                // Sine amplitude is 1000: all samples within range, peaks present
                double max = double.MinValue;
                foreach (JsonNode sample in samples)
                {
                    double v = (double)sample;
                    Assert.InRange(v, -1100, 1100);
                    if (v > max) max = v;
                }
                Assert.True(max > 500, $"Expected sine peaks above 500, got max {max}");
            }
        }

        [Fact]
        public void ReadSamples_ResolvesChannelByLabel()
        {
            (McpToolService service, TestMcpHost host) = CreateServiceWithSineData();
            using (host)
            {
                JsonObject result = service.CallTool("read_samples", new JsonObject
                {
                    ["channel"] = "CH2",
                    ["count"] = 100
                });

                Assert.Equal(1, (int)result["channel"]);
                Assert.Equal("CH2", (string)result["label"]);
            }
        }

        [Fact]
        public void ReadSamples_DecimateReducesReturnedSamples()
        {
            (McpToolService service, TestMcpHost host) = CreateServiceWithSineData();
            using (host)
            {
                JsonObject result = service.CallTool("read_samples", new JsonObject
                {
                    ["channel"] = 0,
                    ["count"] = 1000,
                    ["decimate"] = 10
                });

                Assert.Equal(1000, (int)result["copied"]);
                Assert.Equal(100, ((JsonArray)result["samples"]).Count);
            }
        }

        [Fact]
        public void GetMeasurements_SineStatisticsMatchTheory()
        {
            (McpToolService service, TestMcpHost host) = CreateServiceWithSineData();
            using (host)
            {
                JsonObject result = service.CallTool("get_measurements", new JsonObject
                {
                    ["channel"] = 0,
                    ["count"] = 2000
                });

                // Amplitude 1000 sine: RMS = 707, peak-to-peak = 2000, mean = 0
                Assert.InRange((double)result["rms"], 600, 800);
                Assert.InRange((double)result["peak_to_peak"], 1800, 2200);
                Assert.InRange((double)result["mean"], -100, 100);

                // CH1 of the demo sine is 60 Hz; the mean-crossing estimate should be close
                Assert.NotNull(result["frequency_hz_estimate"]);
                Assert.InRange((double)result["frequency_hz_estimate"], 50, 70);
            }
        }

        [Fact]
        public void ClearData_ReportsClearedStreams()
        {
            (McpToolService service, TestMcpHost host) = CreateServiceWithSineData();
            using (host)
            {
                JsonObject result = service.CallTool("clear_data", new JsonObject());

                Assert.True((bool)result["cleared"]);
                Assert.Equal(1, (int)result["streams_cleared"]);
            }
        }

        [Fact]
        public void AddDemoStream_CreatesChannels()
        {
            using TestMcpHost host = new TestMcpHost();
            McpToolService service = new McpToolService(host);

            JsonObject result = service.CallTool("add_demo_stream", new JsonObject
            {
                ["num_channels"] = 3,
                ["sample_rate"] = 1000,
                ["signal_type"] = "Square Wave"
            });

            Assert.Equal(3, (int)result["created_channels"]);
            Assert.Equal(3, (int)result["total_channels"]);
        }

        [Fact]
        public void RemoveAllStreams_LeavesNoChannels()
        {
            (McpToolService service, TestMcpHost host) = CreateServiceWithSineData();
            using (host)
            {
                service.CallTool("remove_all_streams", new JsonObject());

                JsonObject status = service.CallTool("get_status", new JsonObject());
                Assert.Equal(0, (int)status["total_channels"]);
            }
        }

        [Fact]
        public void UnknownTool_ThrowsToolException()
        {
            using TestMcpHost host = new TestMcpHost();
            McpToolService service = new McpToolService(host);

            Assert.Throws<McpToolException>(() => service.CallTool("does_not_exist", new JsonObject()));
        }

        [Fact]
        public void ReadSamples_UnknownChannel_ThrowsWithAvailableLabels()
        {
            (McpToolService service, TestMcpHost host) = CreateServiceWithSineData();
            using (host)
            {
                McpToolException ex = Assert.Throws<McpToolException>(() =>
                    service.CallTool("read_samples", new JsonObject { ["channel"] = "VOUT" }));

                Assert.Contains("CH1", ex.Message);
            }
        }

        [Fact]
        public void ReadSamples_WithoutStreams_ThrowsHelpfulError()
        {
            using TestMcpHost host = new TestMcpHost();
            McpToolService service = new McpToolService(host);

            McpToolException ex = Assert.Throws<McpToolException>(() =>
                service.CallTool("read_samples", new JsonObject { ["channel"] = 0 }));

            Assert.Contains("add_demo_stream", ex.Message);
        }
    }
}
