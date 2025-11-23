# PowerScope AI Coding Instructions

## Project Overview
PowerScope is a WPF desktop application (.NET 8.0) for real-time plotting and analysis of high-speed serial data from embedded systems (MCUs/FPGAs). Built for control engineering, power electronics, and robotics debugging. Uses ScottPlot (GPU-accelerated) for rendering at 3MBaud+ data rates.

## Architecture

### Channel-Centric Design (Critical Pattern)
The codebase follows a **channel-centric architecture** where `Channel` objects encapsulate both data sources and display settings:

- **Channel** (`Model/Channel.cs`) - Core abstraction combining:
  - Reference to owner `IDataStream` 
  - Local channel index within that stream
  - `ChannelSettings` for display/processing (color, label, gain, offset, filters)
  - Collection of `Measurement` objects
  - Support for both physical and virtual channels

- **Do NOT use global channel indices** - channels are always accessed through their owner stream + local index
- When adding stream support, work through `Channel` objects rather than raw stream references

### Data Stream Hierarchy
All data sources implement `IDataStream` interface (`Model/DataStream.cs`):

**Physical Streams:**
- `SerialDataStream` - Serial/UART data (primary use case)
- `FTDI_SerialDataStream` - High-speed FTDI chips
- `AudioDataStream` - Audio input devices  
- `FileDataStream` - Playback from CSV files
- `DemoDataStream` - Simulated test data
- `USBDataStream` - Custom USB protocols

**Virtual Streams:**
- `VirtualDataStream` - Computed channels from math operations (add/subtract/multiply/divide) or filtering

**Key Interfaces:**
- `IChannelConfigurable` - Per-channel settings (gain/offset/filters)
- `IBufferResizable` - Runtime buffer size changes
- `IUpDownSampling` - Upsampling/downsampling support

### Core Components

**PlotManager** (`Model/PlotManager.cs`)
- Owns ScottPlot integration and rendering loop
- Manages cursor functionality (vertical/horizontal measurement cursors)
- Handles data recording to CSV via `PlotFileWriter`
- Updates at configurable FPS (controlled by `PlotSettings.TimerInterval`)
- Works directly with `Channel` collection from `DataStreamBar`

**DataStreamBar** (`View/UserControls/DataStreamBar.xaml.cs`)
- Manages `ObservableCollection<Channel>` - single source of truth for all channels
- Automatically handles stream disposal cascades (virtual channels depend on physical)
- UI panels for stream configuration/status

**DataParser** (`Model/DataParser.cs`)
- Parses incoming data from streams
- Supports ASCII (delimited text) and Binary modes (int16/uint16/int32/uint32/float)
- Binary mode supports framing with start bytes
- Critical for embedded protocol integration

**RingBuffer** (`Model/RingBuffer.cs`)
- Lock-free circular buffer for high-throughput data
- Used by all streams for sample storage
- Thread-safe producer/consumer pattern

## Data Flow

1. **Stream Creation**: User adds stream via `DataStreamBar` → creates `IDataStream` implementation
2. **Channel Registration**: Stream creates `Channel` objects → added to `DataStreamBar.Channels`
3. **Data Acquisition**: Background thread in stream writes to `RingBuffer`
4. **Plot Updates**: `PlotManager` timer copies data via `Channel.CopyLatestDataTo()` → ScottPlot arrays
5. **Rendering**: ScottPlot GPU-accelerated rendering at configured FPS

## Settings & Serialization

**Serializer** (`Model/Serializer.cs`)
- Saves/loads entire application state to XML
- Persists: plot settings, stream configurations, channel settings, colors, measurements
- Auto-saves to `Settings.xml` on exit, auto-loads on startup
- Format designed for hand-editing if needed

**PlotSettings** (`Model/PlotSettings.cs`)
- Centralized plot configuration (axes, update rate, buffer size, line width, anti-aliasing)
- Changes propagate automatically via INotifyPropertyChanged
- `TimerInterval` derived from `PlotUpdateRateFPS`

## Critical Patterns

### Adding a New DataStream Type
1. Implement `IDataStream` interface
2. Add optional interfaces (`IChannelConfigurable`, `IBufferResizable`, `IUpDownSampling`)
3. Create background thread/timer for data acquisition
4. Write data to `RingBuffer` instances (one per channel)
5. Implement `CopyLatestTo()` for plot data retrieval
6. Add disposal handling with `Disposing` event
7. Update `Serializer` to persist stream configuration
8. Add UI in `SerialConfigWindow` or similar for configuration

### Working with Measurements
- `Measurement` class (`Model/Measurement.cs`) computes min/max/avg/RMS/FFT over sliding windows
- Owned by `Channel`, not globally managed
- Access via `channel.Measurements` collection
- Updates happen in measurement timer, separate from plot updates

### UI Threading
- All stream operations run on background threads
- UI updates MUST use `Application.Current.Dispatcher.BeginInvoke()` or `.Invoke()`
- PlotManager's update timer already runs on UI dispatcher thread

### Virtual Channels
- Created from existing channels with math operations or filters
- Subscribe to source channel's stream `Disposing` event for cascade deletion
- Example: `new Channel(sourceChannel, operation, settings)`

## Build & Run

**Configuration:** Uses .NET 8.0 with WPF, targets x64 Windows 8.0+

**Build profiles:**
- Debug - Standard debugging
- Release - Optimized for deployment  
- Performance - TieredPGO enabled

**Run:** `dotnet run` or F5 in Visual Studio

**Dependencies:** 
- ScottPlot.WPF 5.0.55 (plotting)
- RJCP.SerialPortStream (reliable serial)
- NAudio (audio input)
- FtdiSharp (local project reference for FTDI support)

## Testing & Debug Workflows

**Use Demo Stream:** Click "Add data stream" → Demo → generates synthetic waveforms for UI testing without hardware

**File Playback:** Load CSV files via RunControl "Load" button → loops playback for reproducible testing

**Measurement Cursors:** Enable via HorizontalControl/VerticalControl → drag to measure time/voltage deltas

## Common Gotchas

1. **Channel indices are LOCAL to streams** - never assume global indexing
2. **Ring buffers don't resize automatically** - must recreate when `BufferSize` changes
3. **ScottPlot requires UI thread** - all plot operations via Dispatcher
4. **Virtual channels cascade delete** - removing source stream removes dependent virtuals
5. **Parser byte alignment matters** - binary mode requires exact byte counts per sample
6. **ChannelSettings changes auto-apply** - no explicit "apply" needed due to PropertyChanged bindings

## Color Palette
Uses `ScottPlot.Palettes.Tsitsulin` with special case for index 3 → LimeGreen (see `PlotManager.GetColor()`)

## File Structure
- `Model/` - Core data structures and business logic
- `View/UserControls/` - Reusable WPF controls (DataStreamBar, MeasurementBar, etc.)
- `View/UserForms/` - Dialog windows for configuration
- `Converters/` - WPF value converters for data binding
- `Icons/` - SVG icons for UI

## Performance Considerations
- Plot updates at 15-60 FPS (configurable)
- Ring buffers sized via `PlotSettings.BufferSize` (default 5M samples)
- GPU acceleration via ScottPlotGL
- Thread-safe lock-free ring buffer implementation
- Measurements computed on separate timer to avoid blocking plot updates

## Code Style Guidelines

**Readability First** - Code can be verbose for clarity. Not overly defensive - fix bugs, don't hide them.

**Avoid Modern C# "Conveniences":**
- No ternary operators
- No null conditional operators (`?.`, `??`)
- No `var` keyword - always explicitly declare types
- No expression-bodied members
- Minimize LINQ usage - prefer `foreach` when more readable
- Avoid chaining method calls

**Control Flow:**
- Use explicit `if-else` statements
- Skip `{}` for single-line if/else statements
- Prefer `switch` statements over multiple `if-else-if`

**Naming Conventions:**
- Private fields: `_camelCase`
- Public fields/properties: `PascalCase`
- Local variables: `camelCase`
- Method parameters: `camelCase`
- Interfaces: `IPascalCase`
- Classes: `PascalCase`
- Methods: `PascalCase`

**Example:**
```csharp
// Good
if (channel == null)
    return;

IDataStream stream = channel.OwnerStream;
if (stream.IsConnected)
{
    stream.StartStreaming();
}
else
{
    stream.Connect();
    stream.StartStreaming();
}

// Bad
var stream = channel?.OwnerStream;
stream?.IsConnected ? stream.StartStreaming() : stream?.Connect()?.StartStreaming();
```
