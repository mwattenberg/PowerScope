using System;
using System.ComponentModel;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace PowerScope.Model.Mcp
{
    /// <summary>
    /// MCP server using the official ModelContextProtocol C# SDK with stdio transport.
    /// Register in Claude Desktop's claude_desktop_config.json:
    ///   "powerscope": { "command": "C:\\...\\PowerScope.exe", "args": ["--stdio"] }
    /// </summary>
    public sealed class McpServer : IDisposable
    {
        private readonly IHost _host;
        private readonly CancellationTokenSource _cts = new();
        private bool _disposed;

        public McpServer(McpToolService tools)
        {
            var settings = new HostApplicationBuilderSettings { Args = [] };
            var builder = Host.CreateEmptyApplicationBuilder(settings);

            // Stdio transport: only JSON-RPC must go to stdout. Route any SDK/host
            // log messages to stderr so they don't corrupt the protocol stream.
            builder.Logging
                   .SetMinimumLevel(LogLevel.Warning)
                   .AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

            builder.Services
                   .AddSingleton(tools)
                   .AddMcpServer()
                   .WithStdioServerTransport()
                   .WithToolsFromAssembly();

            _host = builder.Build();
        }

        public void Start()
        {
            _ = _host.RunAsync(_cts.Token);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cts.Cancel();
            _cts.Dispose();
            _host.Dispose();
        }
    }

    /// <summary>
    /// Bridge from the SDK's attribute-based tool discovery to the existing McpToolService
    /// implementation. Each method re-serializes its typed parameters into a JsonObject so all
    /// business logic, validation, and error handling remain in McpToolService (unit-tested in
    /// McpToolServiceTests). McpToolException propagates naturally; the SDK wraps it as an
    /// isError=true tool result per the MCP spec.
    /// </summary>
    [McpServerToolType]
    internal sealed class PowerScopeTools
    {
        private readonly McpToolService _service;

        public PowerScopeTools(McpToolService service) => _service = service;

        [McpServerTool(Name = "get_status")]
        [Description("Get the current state of PowerScope: all active data streams (type, connection state, sample rate, total samples acquired) and their channels (index, label, enabled, gain, offset). Call this first to discover available channels.")]
        public string GetStatus() => _service.CallTool("get_status", new JsonObject()).ToJsonString();

        [McpServerTool(Name = "read_samples")]
        [Description("Read the latest samples from a channel's ring buffer. Samples are ordered oldest to newest; the last element is the most recent. Use 'decimate' to reduce the number of returned points for long captures.")]
        public string ReadSamples(
            [Description("Channel to read: global index integer (0, 1, ...) or label string ('CH1', 'VOUT', ...) from get_status.")] string channel,
            [Description("Number of raw samples to fetch (default 1000, max 5000000).")] int count = 1000,
            [Description("Return only every Nth sample (default 1 = all). The response contains at most count/decimate values.")] int decimate = 1)
        {
            var args = new JsonObject { ["count"] = count, ["decimate"] = decimate };
            SetChannel(args, channel);
            return _service.CallTool("read_samples", args).ToJsonString();
        }

        [McpServerTool(Name = "get_measurements")]
        [Description("Compute statistics over the latest samples of a channel: min, max, mean, RMS, standard deviation, peak-to-peak and estimated fundamental frequency (from mean-crossings). Useful for checking signal levels, ripple and steady-state behavior without transferring raw data.")]
        public string GetMeasurements(
            [Description("Channel to analyze: global index integer or label string from get_status.")] string channel,
            [Description("Number of latest samples to analyze (default 10000, max 5000000).")] int count = 10000)
        {
            var args = new JsonObject { ["count"] = count };
            SetChannel(args, channel);
            return _service.CallTool("get_measurements", args).ToJsonString();
        }

        [McpServerTool(Name = "clear_data")]
        [Description("Clear the ring buffers of all streams (discards acquired samples, streams keep running). Call this right before provoking a transient so read_samples afterwards contains only the event of interest.")]
        public string ClearData() => _service.CallTool("clear_data", new JsonObject()).ToJsonString();

        [McpServerTool(Name = "add_demo_stream")]
        [Description("Add a synthetic demo data stream (no hardware required). Useful for testing the tool chain before connecting real hardware.")]
        public string AddDemoStream(
            [Description("Number of channels (default 4).")] int num_channels = 4,
            [Description("Sample rate in Hz (default 10000).")] int sample_rate = 10000,
            [Description("Signal shape: 'Sine Wave', 'Square Wave', 'Triangle Wave', 'Random Noise', 'Mixed Signals', 'Chirp Signal', 'Tones', 'sin(x)/x'. Default 'Sine Wave'.")] string signal_type = "Sine Wave")
        {
            return _service.CallTool("add_demo_stream", new JsonObject
            {
                ["num_channels"] = num_channels,
                ["sample_rate"] = sample_rate,
                ["signal_type"] = signal_type
            }).ToJsonString();
        }

        [McpServerTool(Name = "load_config")]
        [Description("Load a PowerScope session configuration XML file (saved via File > Save Settings). Creates and starts the configured streams — the standard way to connect to real hardware that was set up in the GUI.")]
        public string LoadConfig(
            [Description("Absolute path to the PowerScope settings XML file.")] string file_path)
        {
            return _service.CallTool("load_config", new JsonObject
            {
                ["file_path"] = file_path
            }).ToJsonString();
        }

        [McpServerTool(Name = "remove_all_streams")]
        [Description("Stop, disconnect and remove all active streams and their channels.")]
        public string RemoveAllStreams() => _service.CallTool("remove_all_streams", new JsonObject()).ToJsonString();

        private static void SetChannel(JsonObject args, string channel)
        {
            if (int.TryParse(channel, out int idx))
                args["channel"] = idx;
            else
                args["channel"] = channel;
        }
    }
}
