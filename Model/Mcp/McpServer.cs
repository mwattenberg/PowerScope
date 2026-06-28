using System.ComponentModel;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace PowerScope.Model.Mcp
{
    /// <summary>
    /// MCP server using the official ModelContextProtocol C# SDK over Streamable HTTP.
    /// Listens on http://127.0.0.1:54321/ — any MCP client that supports the HTTP
    /// transport connects directly, no companion stdio bridge process required.
    /// </summary>
    public sealed class McpServer : IDisposable
    {
        public const int DefaultPort = 54321;

        private readonly WebApplication _app;
        private bool _disposed;

        public McpServer(McpToolService tools, int port = DefaultPort)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder();

            builder.Logging
                   .SetMinimumLevel(LogLevel.Warning)
                   .AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

            builder.WebHost.UseUrls($"http://127.0.0.1:{port}");

            builder.Services
                   .AddSingleton(tools)
                   .AddMcpServer()
                   .WithHttpTransport()
                   .WithToolsFromAssembly();

            _app = builder.Build();
            _app.MapMcp();
        }

        public void Start()
        {
            _app.Start();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
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

        [McpServerTool(Name = "get_channel_info")]
        [Description("Get detailed settings for a single channel: label, enabled state, gain, offset, color and active filter. Useful for reading the current calibration of a channel before or after calling set_channel.")]
        public string GetChannelInfo(
            [Description("Channel to query: global index integer (0, 1, ...) or label string ('CH1', 'VOUT', ...) from get_status.")] string channel)
        {
            var args = new JsonObject();
            SetChannel(args, channel);
            return _service.CallTool("get_channel_info", args).ToJsonString();
        }

        [McpServerTool(Name = "set_channel")]
        [Description("Set any combination of label, enabled state, gain, or offset for a channel. Updates take effect immediately. Provide at least one parameter; omit the rest to leave them unchanged. Gain must be non-zero.")]
        public string SetChannelProperties(
            [Description("Channel to configure: global index integer or label string from get_status.")] string channel,
            [Description("New display label (non-empty string). Omit to leave unchanged.")] string label = null,
            [Description("Enable or disable the channel. Omit to leave unchanged.")] bool? enabled = null,
            [Description("New gain (multiplicative scale factor, non-zero). Omit to leave unchanged.")] double? gain = null,
            [Description("New offset (additive shift, in scaled units). Omit to leave unchanged.")] double? offset = null)
        {
            var args = new JsonObject();
            SetChannel(args, channel);
            if (label != null) args["label"] = label;
            if (enabled.HasValue) args["enabled"] = enabled.Value;
            if (gain.HasValue) args["gain"] = gain.Value;
            if (offset.HasValue) args["offset"] = offset.Value;
            return _service.CallTool("set_channel", args).ToJsonString();
        }

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

        [McpServerTool(Name = "set_trigger")]
        [Description("Configure the trigger. All parameters are optional — omit any you don't want to change. Current trigger state is always included in get_status under the 'trigger' key.")]
        public string SetTrigger(
            [Description("Operating mode: 'free_run' (continuous, no trigger), 'normal' (re-arms after each event), 'single' (captures once then stops). Omit to leave unchanged.")] string mode = null,
            [Description("Trigger threshold in amplitude units. Omit to leave unchanged.")] double? level = null,
            [Description("Trigger X position in sample indices (where the trigger point appears on the display). Clamped to 5%–95% of the sample window. Omit to leave unchanged.")] int? position = null,
            [Description("Edge direction: 'rising' or 'falling'. Omit to leave unchanged.")] string edge = null,
            [Description("Source channel: global index integer or label string. Pass 'auto' to use the first enabled channel. Omit entirely to leave unchanged.")] string channel = null)
        {
            var args = new JsonObject();
            if (mode != null) args["mode"] = mode;
            if (level.HasValue) args["level"] = level.Value;
            if (position.HasValue) args["position"] = position.Value;
            if (edge != null) args["edge"] = edge;
            if (channel != null) args["channel"] = channel;
            return _service.CallTool("set_trigger", args).ToJsonString();
        }

        [McpServerTool(Name = "get_x_range")]
        [Description("Get the current horizontal (X) axis viewport range in sample indices. Reflects the live view, which may differ from the configured sample window if the user has zoomed or panned.")]
        public string GetXRange() => _service.CallTool("get_x_range", new JsonObject()).ToJsonString();

        [McpServerTool(Name = "set_x_range")]
        [Description("Set the horizontal (X) axis viewport range in sample indices. Equivalent to zooming/panning the plot with the mouse — transient, does not change the configured sample window. The plot resets to its configured range if the user changes the time base.")]
        public string SetXRange(
            [Description("Left edge of the viewport in sample indices (default: current value).")] double? x_min = null,
            [Description("Right edge of the viewport in sample indices (default: current value).")] double? x_max = null)
        {
            var args = new JsonObject();
            if (x_min.HasValue) args["x_min"] = x_min.Value;
            if (x_max.HasValue) args["x_max"] = x_max.Value;
            return _service.CallTool("set_x_range", args).ToJsonString();
        }

        [McpServerTool(Name = "get_y_range")]
        [Description("Get the current vertical (Y) axis viewport range and whether auto-scale is active.")]
        public string GetYRange() => _service.CallTool("get_y_range", new JsonObject()).ToJsonString();

        [McpServerTool(Name = "set_y_range")]
        [Description("Set the vertical (Y) axis viewport range. Automatically disables auto-scale so the range is not overridden on the next render. Pass auto_scale=true to re-enable auto-scaling instead (y_min/y_max are ignored when auto_scale is true). Transient — resets if PlotSettings Y range changes.")]
        public string SetYRange(
            [Description("Bottom of the viewport in amplitude units (default: current value). Ignored when auto_scale is true.")] double? y_min = null,
            [Description("Top of the viewport in amplitude units (default: current value). Ignored when auto_scale is true.")] double? y_max = null,
            [Description("Re-enable Y auto-scaling (default false). When true, y_min and y_max are ignored.")] bool auto_scale = false)
        {
            var args = new JsonObject { ["auto_scale"] = auto_scale };
            if (y_min.HasValue) args["y_min"] = y_min.Value;
            if (y_max.HasValue) args["y_max"] = y_max.Value;
            return _service.CallTool("set_y_range", args).ToJsonString();
        }

        [McpServerTool(Name = "capture_plot")]
        [Description("Render the current PowerScope plot to a PNG or SVG file. Returns the file path plus rich context: x/y axis viewport ranges, trigger state (mode, position, in_view), and per-channel statistics (min, max, mean, clipped) over the visible portion of the plot for all enabled channels.")]
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
