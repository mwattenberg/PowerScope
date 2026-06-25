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

Command line arguments: `--config <path>` (load a session XML instead of Settings.xml at startup). The TCP MCP server is enabled/disabled from the Plot Settings window (persisted in the session XML), not via a command line switch.

## Companion Repositories

These directories are part of the broader PowerScope ecosystem and are frequently relevant:

- `C:\Users\Martin\mtw\PowerScope_FX2G3` — Cypress FX2G3 MCU-side firmware (C, MTB build system). The MCU counterpart to PowerScope's host app.
- `C:\Users\Martin\mtw\USBHS_Device` — Cypress PSoC USBHS device reference/echo project used as a USB dev testbed.
- `C:\Users\Martin\mtw\mtb_shared\usbfxstack\release-v1.3.3` — Infineon EZ-USB FXStack Middleware v1.3.3: USB stack, DMA manager, and LVDS driver for FX2G3 and other EZ-USB FX devices.

### Building the FX2G3 Firmware

The firmware uses ModusToolbox 3.8 with a make/ninja build system. The system environment has a stale `CY_TOOLS_PATHS` pointing to `tools_3.5`; always override it. Build must be invoked through the modus-shell's bash (not plain PowerShell) so Unix tools are available.

```powershell
# Run from PowerShell — override the stale CY_TOOLS_PATHS env var
& "C:\Users\Martin\ModusToolbox\tools_3.8\modus-shell\bin\bash.exe" --login -c `
  "export CY_TOOLS_PATHS='C:/Users/Martin/ModusToolbox/tools_3.8'; " + `
  "cd /cygdrive/c/Users/Martin/mtw/PowerScope_FX2G3 && " + `
  "make CY_MAKE_IDE=eclipse CY_IDE_TOOLS_DIR=C:/Users/Martin/ModusToolbox/tools_3.8 CY_IDE_BT_TOOLS_DIR= -j8 build_proj"
```

The built `.hex` file is at `build/APP_KIT_FX2G3_104LGA/Release/mtb-example-fx2g3-hello-world.hex`.

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

4. **PlotManager** (`Model/PlotManager.cs` + `.Cursors.cs` + `.Triggers.cs`) — the rendering hub. A `DispatcherTimer` (default 30 Hz) calls `UpdatePlot()`, which calls `CopyLatestN()` on each visible channel and hands data to ScottPlot's `WpfPlot` (software/SkiaSharp CPU renderer). Also owns trigger logic and cursor math. See "Known Issues" below for why this is not the GPU (`WpfPlotGL`) control.

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

PowerScope embeds an MCP server (`Model/Mcp/`, see `docs/MCP.md`) so AI agents can read live waveform data. Uses the official `ModelContextProtocol` C# SDK (NuGet 1.4.0) with stdio transport — launched with `--stdio` for Claude Desktop. Tools: `get_status`, `read_samples`, `get_measurements`, `clear_data`, `add_demo_stream`, `load_config`, `remove_all_streams`. The `IMcpHost` interface decouples the tool layer from the UI — `MainWindow.McpWindowHost` marshals onto the dispatcher; tests (`Tests/`) back it with `DemoDataStream`s directly.

## Key Dependencies

| Package | Role |
|---|---|
| ScottPlot.WPF 5.1.59 | GPU-accelerated 2D waveform rendering (`WpfPlotGL`) |
| RJCP.SerialPortStream 3.0.3 | High-speed COM port (>1 MBaud reliable) |
| NAudio 2.2.1 | Audio input capture |
| Aelian.FFT 1.0.4 | FFT calculations |

## Known Issues / Future Work

## Known Issues / Future Work

### GPU rendering (`WpfPlotGL`) — native memory leak workaround

`WpfPlotGL` leaks **native** memory on every `Refresh()` (~2.5 GB/min at 30 Hz; managed heap stays flat). The leak is SkiaSharp creating a new `GRBackendRenderTarget` + `SKSurface` per frame whose native GPU memory is only reclaimed by GC finalizers. With an allocation-free hot path, GC barely runs and finalizers never fire.

**Current mitigation:** `PlotManager.UpdatePlot` triggers a non-blocking gen0 `GC.Collect` every 150 frames (~5 s at 30 Hz). This keeps the finalizer queue drained and working set flat without visible pauses. Monitor with Task Manager: working set should remain stable under a high-rate Demo stream.

**Original dependency-conflict blocker (resolved):** When the project targeted `windows10.0.17763`, NuGet fell back to `SkiaSharp.Views.WPF`'s `.NETFramework4.6.2` asset (the only one whose OS version was compatible) which references `OpenTK 3.3.1`. ScottPlot requires `OpenTK 4.9.4`. The two are binary-incompatible → `FileNotFoundException` at first GL render. Fixed by bumping the TFM to `net10.0-windows10.0.19041` so NuGet picks the `net8.0-windows10.0.19041` asset (OpenTK 4.3.0 → resolves to 4.9.4, same major, semver-compatible).

### Latent (unrelated) cleanups noticed during the above

- Resampler default factor is `1` (= 2× upsampling *enabled*) in `USBDataStream`, `SerialDataStream`, `DemoDataStream`; only `AudioDataStream` uses `0` (bypass). Currently masked because `StreamSettings.CreateDataStream` resets it to `0`, but any construction path that skips that reset silently enables the allocating resampler.
- `StreamInfoPanel` subscribes to the stream's `PropertyChanged` but never unsubscribes (the `Unloaded` handler's unsubscribe is commented out), leaking a panel + stream (~8 MB/channel ring buffer + read thread) per stream remove/reconfigure.
