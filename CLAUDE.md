# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What PowerScope Is

PowerScope is a Windows-only real-time data acquisition and visualization tool targeting embedded developers (control engineering, power electronics, motor drives/robotics). It plots high-speed serial/USB data (3 MBaud+) from MCUs and FPGAs with oscilloscope-like analysis features (filtering, FFT, cursors, measurements). The philosophy is focused functionality — no plugins, no customizable GUI.

## Build

**Requirements:** .NET 10 SDK, x64 Windows 10/11.

```powershell
# Restore and build (Debug)
dotnet build

# Build Release
dotnet build -c Release

# Build Performance configuration (enables TieredPGO + ReadyToRun)
dotnet build -c Performance

# Run
dotnet run

# Run tests (MCP server / tool layer)
dotnet test Tests\PowerScope.Tests.csproj
```

Command line arguments: `--config <path>` loads the given session file instead of `Settings.xml` at startup. A bare file path (no `--config` flag) works the same way — this is how Explorer invokes PowerScope when a registered `.psp` session file is double-clicked or opened via "Open with" (see `Model/FileAssociation.cs`). The MCP server (Streamable HTTP, `127.0.0.1:54321`) is enabled/disabled from the Plot Settings window (persisted in the session file), not via a command line switch.

## Architecture

### Data Flow: Hardware → Ring Buffer → Plot

1. **IDataStream** (`Model/DataStream.cs`) — the central interface. All sources implement `Connect()`, `StartStreaming()`, `StopStreaming()`, and `CopyLatestTo()`. Concrete implementations:
   - `SerialDataStream` — COM port via RJCP.SerialPortStream
   - `USBDataStream` — WinUSB bulk transfer via P/Invoke
   - `AudioDataStream` — system audio via NAudio
   - `DemoDataStream` — synthetic test data
   - `FileDataStream` — playback from recorded files
   - `VirtualDataStream` — computed channels (no hardware)

2. **DataParser** (`Model/DataParser.cs`) — converts raw bytes to `double[][]` (one row per channel). Supports ASCII (configurable delimiters) and binary (int8/16/32, float32/64, framed). Carries residual bytes across read cycles.

3. **RingBuffer\<T\>** (`Model/RingBuffer.cs`) — thread-safe circular buffer per channel. Fixed capacity; oldest samples silently discarded when full.

4. **PlotManager** (`Model/PlotManager.cs` + `.Cursors.cs` + `.Triggers.cs`) — the rendering hub. A `DispatcherTimer` (default 30 Hz) calls `UpdatePlot()`, which calls `CopyLatestN()` on each visible channel and hands data to ScottPlot's `WpfPlot` (software/SkiaSharp CPU renderer). Also owns trigger logic and cursor math. See the `WpfPlotGL` note under Key Dependencies for why this is not the GPU control.

### Channel Model

- **Channel** (`Model/Channel.cs`) — pairs an `IDataStream` with a local channel index. Owns the ring buffer reference for that channel.
- **ChannelSettings** (`Model/ChannelSettings.cs`) — MVVM ViewModel for one channel. Holds gain, offset, color, label, filter, enabled state. Implements `INotifyPropertyChanged`. Bound directly to UI controls — there is no separate ViewModel layer.

### Capability Interfaces

Beyond `IDataStream`, streams can implement optional interfaces:
- `IChannelConfigurable` — per-channel gain/offset/filter applied during streaming
- `IBufferResizable` — ring buffer size adjustable at runtime
- `IResamplable` — decimation or interpolation by powers of 10 (`Model/Resampler.cs`)

### VirtualDataStream

Computes samples on-demand from parent channels (add, subtract, multiply, divide) without maintaining its own ring buffer. Allows derived signals like `Power = Voltage × Current`.

### UI Structure

All UI is in `View/`. Controls in `View/UserControls/` own specific panels:
- `DataStreamBar` — manages the active stream list
- `ChannelControlBar` / `ChannelControl` — per-channel settings
- `MeasurementBar` / `MeasurementBox` — on-demand statistics (min/max/mean/RMS/FFT)
- `TriggerControl` — edge/level trigger configuration
- `CursorHorizontal` / `CursorVertical` — manual measurement cursors
- `VirtualChannelSelectionBar` — virtual channel creation

Modal dialogs in `View/UserForms/`:
- `StreamConfigWindow` — select and configure the data source
- `FilterConfigWindow` — IIR/digital filter setup
- `PlotSettingsWindow` — FPS, buffer depth, rendering options
- `FFT.xaml` — FFT spectrum view

### Session Persistence

`Serializer.cs` saves/loads all configuration (stream parameters, channel settings, plot state, measurements) to/from XML. `FileIOManager.cs` handles binary/text recording of live sample data to disk.

### MCP Server

PowerScope embeds an MCP server (`Model/Mcp/`, see `docs/MCP.md`) so AI agents can read live waveform data. Uses the official `ModelContextProtocol`/`ModelContextProtocol.AspNetCore` C# SDK (NuGet 1.4.0) over Streamable HTTP — an embedded Kestrel server on `127.0.0.1:54321`, toggled from the Plot Settings window. Any MCP client with an HTTP transport (`"type": "http"`) connects directly; no companion process needed. Tools: `get_status`, `read_samples`, `get_measurements`, `clear_data`, `add_demo_stream`, `load_config`, `remove_stream`, `remove_all_streams`, `capture_plot`. The `IMcpHost` interface decouples the tool layer from the UI — `MainWindow.McpWindowHost` marshals onto the dispatcher; tests (`Tests/`) back it with `DemoDataStream`s directly.

## Key Dependencies

| Package | Role |
|---|---|
| ScottPlot.WPF 5.1.59 | GPU-accelerated 2D waveform rendering (`WpfPlotGL`) |
| RJCP.SerialPortStream 3.0.3 | High-speed COM port (>1 MBaud reliable) |
| NAudio 2.2.1 | Audio input capture |
| Aelian.FFT 1.0.4 | FFT calculations |

Note: `WpfPlotGL`'s native memory leak (a new `GRBackendRenderTarget`/`SKSurface` per frame whose GPU memory is only reclaimed by GC finalizers) is mitigated in `PlotManager.UpdatePlot` with a non-blocking gen0 `GC.Collect` every 150 frames (~5 s at 30 Hz), keeping the finalizer queue drained and working set flat.
