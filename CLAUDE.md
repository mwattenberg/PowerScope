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
| ScottPlot.WPF 5.1.59 | 2D waveform rendering (software `WpfPlot`; see Known Issues) |
| RJCP.SerialPortStream 3.0.3 | High-speed COM port (>1 MBaud reliable) |
| NAudio 2.2.1 | Audio input capture |
| Aelian.FFT 1.0.4 | FFT calculations |

## Known Issues / Future Work

### GPU rendering (`WpfPlotGL`) is disabled — native memory leak

The plot uses the **software** `WpfPlot` (SkiaSharp CPU) control, not the GPU `WpfPlotGL` control, even though GPU would render faster.

**Why:** `WpfPlotGL` leaks **native** memory on every `Refresh()` (working set climbs ~2.5 GB/min at 30 Hz; the managed GC heap stays flat). The leak was masked before the acquisition-path allocation optimization, because the constant managed-GC churn reclaimed the native render surfaces; once the hot path became allocation-free, GC effectively stopped and native memory grew unbounded — the app got sluggish but never threw OOM. Switching to software `WpfPlot` (identical `PlotManager` usage) makes the leak disappear entirely.

**Why we can't just fix it on the current package:** ScottPlot.WPF 5.1.59 has an internally inconsistent GL dependency closure — it requires `OpenTK >= 4.9.4` / `OpenTK.GLWpfControl >= 4.3.3`, but the `SkiaSharp.Views.WPF 3.119.0` `SKGLElement` it uses is a .NET Framework package built against `OpenTK 3.3.1`. Those OpenTK majors are mutually exclusive: with 4.x loaded, `SKGLElement.OnPaint` throws `FileNotFoundException` for `OpenTK 3.3.1` at first render; pinning to 3.x is rejected (`NU1605` downgrade). This is an upstream packaging bug.

**To revisit GPU rendering later:**
1. File an upstream ScottPlot issue (repro: the `NU1605` / `OpenTK 3.3.1 FileNotFound` GL dependency conflict on net10).
2. When a ScottPlot 5.x ships with a consistent GL closure, switch `WpfPlot` → `WpfPlotGL` in `MainWindow.xaml`, `View/UserForms/FFT.xaml`, and the `_plot` field/property/ctor in `Model/PlotManager.cs`, then **re-verify the native leak is actually fixed** (gcdump: managed flat + working set flat under a high-rate demo) — a runnable GL build does NOT by itself prove the leak is gone.

### Latent (unrelated) cleanups noticed during the above

- Resampler default factor is `1` (= 2× upsampling *enabled*) in `USBDataStream`, `SerialDataStream`, `DemoDataStream`; only `AudioDataStream` uses `0` (bypass). Currently masked because `StreamSettings.CreateDataStream` resets it to `0`, but any construction path that skips that reset silently enables the allocating resampler.
- `StreamInfoPanel` subscribes to the stream's `PropertyChanged` but never unsubscribes (the `Unloaded` handler's unsubscribe is commented out), leaking a panel + stream (~8 MB/channel ring buffer + read thread) per stream remove/reconfigure.
