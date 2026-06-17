using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.ComponentModel;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace PowerScope.Model.Mcp
{
    /// <summary>
    /// MCP server using the official ModelContextProtocol C# SDK over TCP.
    /// Listens on localhost:54321 and handles one client connection at a time.
    /// Connect via the PowerScopeMCP companion executable, which bridges
    /// an MCP client's stdio ↔ this TCP port.
    /// </summary>
    public sealed class McpServer : IDisposable
    {
        public const int DefaultPort = 54321;

        private readonly McpToolService _tools;
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private bool _disposed;

        public McpServer(McpToolService tools, int port = DefaultPort)
        {
            _tools = tools;
            _listener = new TcpListener(IPAddress.Loopback, port);
        }

        public void Start()
        {
            _listener.Start();
            _ = AcceptLoopAsync(_cts.Token);
        }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    TcpClient client = await _listener.AcceptTcpClientAsync(ct);
                    _ = HandleClientAsync(client, ct);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Debug.WriteLine($"MCP TCP accept error: {ex.Message}");
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
        {
            using (client)
            {
                NetworkStream stream = client.GetStream();

                var settings = new HostApplicationBuilderSettings { Args = [] };
                var builder = Host.CreateEmptyApplicationBuilder(settings);

                builder.Logging
                       .SetMinimumLevel(LogLevel.Warning)
                       .AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

                builder.Services
                       .AddSingleton(_tools)
                       .AddMcpServer()
                       .WithStreamServerTransport(stream, stream)
                       .WithToolsFromAssembly();

                using IHost host = builder.Build();
                await host.RunAsync(ct);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cts.Cancel();
            _listener.Stop();
            _cts.Dispose();
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

        [McpServerTool(Name = "remove_stream")]
        [Description("Stop, disconnect and remove a single stream and all of its channels, identified by any one of its channels. Other streams are left running. Use get_status first to find the channel index or label.")]
        public string RemoveStream(
            [Description("Any channel belonging to the stream to remove: global index integer or label string from get_status.")] string channel)
        {
            var args = new JsonObject();
            SetChannel(args, channel);
            return _service.CallTool("remove_stream", args).ToJsonString();
        }

        [McpServerTool(Name = "capture_plot")]
        [Description("Render the current PowerScope plot to a PNG or SVG file and return the file path. Use this to get a visual snapshot of the waveforms without transferring raw sample data.")]
        public string CapturePlot(
            [Description("Absolute path for the output file. Extension determines format: .png (default) or .svg. Omit to use a temp file.")] string file_path = null,
            [Description("Image width in pixels (default 1920).")] int width = 1920,
            [Description("Image height in pixels (default 1080).")] int height = 1080)
        {
            var args = new JsonObject { ["width"] = width, ["height"] = height };
            if (file_path != null) args["file_path"] = file_path;
            return _service.CallTool("capture_plot", args).ToJsonString();
        }

        private static void SetChannel(JsonObject args, string channel)
        {
            if (int.TryParse(channel, out int idx))
                args["channel"] = idx;
            else
                args["channel"] = channel;
        }
    }
}
