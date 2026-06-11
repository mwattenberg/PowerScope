# PowerScope MCP Server

PowerScope embeds an [MCP](https://modelcontextprotocol.io) (Model Context Protocol) server so AI agents
like Claude Code can read live waveform data from connected hardware. The intended workflow: an agent
writes MCU application code (e.g. control loop gains of a buck converter), builds and flashes it, then
verifies the resulting behavior through PowerScope and iterates.

The MCP layer is transport-agnostic — it operates on PowerScope's `IDataStream`/`Channel` abstraction,
so the MCU can be connected any way PowerScope supports: its UART through a virtual COM port, native
USB if the MCU has it, a USB bridge chip (e.g. FX2G3), or even an audio input. The agent sees the same
channels and tools regardless of the physical link.

The server starts automatically with the application and listens on **localhost only** —
it is never reachable from other machines.

## Connecting from Claude Code

```
claude mcp add --transport http powerscope http://localhost:5642/mcp
```

Or in `.mcp.json` / `settings.json`:

```json
{
  "mcpServers": {
    "powerscope": {
      "type": "http",
      "url": "http://localhost:5642/mcp"
    }
  }
}
```

## Command line arguments

| Argument | Effect |
|---|---|
| `--config <path>` | Load the given session XML at startup instead of `Settings.xml`. Configure streams once in the GUI, save the session, then launch reproducibly: `PowerScope.exe --config buck_test.xml` |
| `--mcp-port <port>` | MCP server port (default 5642). Use when running multiple instances. |
| `--no-mcp` | Disable the MCP server. |

## Tools

| Tool | Description |
|---|---|
| `get_status` | All active streams (type, connection, sample rate, total samples) and channels (index, label, enabled, gain, offset). Call first to discover channels. |
| `read_samples` | Latest samples from a channel's ring buffer, oldest to newest. Args: `channel` (index or label), `count` (default 1000), `decimate` (return every Nth sample). |
| `get_measurements` | Statistics over the latest samples: min, max, mean, RMS, std dev, peak-to-peak, estimated fundamental frequency. Args: `channel`, `count` (default 10000). |
| `clear_data` | Clear all ring buffers (streams keep running). Call right before provoking a transient so subsequent reads contain only the event. |
| `add_demo_stream` | Add a synthetic stream (sine/square/triangle/noise/chirp/...) for testing without hardware. |
| `load_config` | Load a PowerScope session XML — creates and starts the configured streams. This is how an agent connects to real hardware that was set up in the GUI. |
| `remove_all_streams` | Stop and remove all streams. |

## Typical agent loop (firmware tuning)

1. `load_config` with a session XML pointing at the MCU's serial/USB stream
2. `clear_data`, then trigger the load step / setpoint change on the MCU
3. `read_samples` on the output voltage channel; analyze overshoot and settling
4. Edit firmware gains, rebuild, reflash
5. Repeat from 2 until the transient meets the spec

## Architecture

- `Model/Mcp/McpServer.cs` — dependency-free JSON-RPC server over the MCP streamable HTTP transport (`HttpListener`, localhost prefix needs no admin rights). Supports `initialize`, `ping`, `tools/list`, `tools/call`, notifications.
- `Model/Mcp/McpToolService.cs` — tool implementations, transport-independent.
- `Model/Mcp/IMcpHost.cs` — boundary to the application. `MainWindow.McpWindowHost` implements it by marshalling onto the WPF dispatcher; tests implement it with bare `DemoDataStream`s.

Tests live in `Tests/PowerScope.Tests.csproj` and verify the tool layer against demo waveforms with known properties (sine RMS = amplitude/√2, etc.) plus the HTTP protocol behavior end to end:

```powershell
dotnet test Tests\PowerScope.Tests.csproj
```
