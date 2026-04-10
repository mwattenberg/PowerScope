# PowerScope AI Coding Instructions

## Project Overview
PowerScope is a WPF desktop application (.NET 8.0) for real-time plotting and analysis of high-speed serial data from embedded systems (MCUs/FPGAs). Built for control engineering, power electronics, and robotics debugging. Uses ScottPlot (GPU-accelerated) for rendering at 3MBaud+ data rates.

## Architecture

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

PowerScope follows a **channel-centric data flow** where raw data from hardware passes through multiple processing stages before being displayed:

### 1. Stream Creation & Connection
User adds stream via `DataStreamBar` → creates `IDataStream` implementation (e.g., `SerialDataStream`, `AudioDataStream`, `FTDI_SerialDataStream`).

### 2. Channel Registration  
Stream creates `Channel` objects → added to `DataStreamBar.Channels` (the single source of truth for all channels in the application).

### 3. Data Acquisition (Background Thread)
Each `IDataStream` implementation runs a background thread/timer that:
- Reads raw bytes from hardware (serial port, audio device, FTDI chip, etc.)
- Parses bytes into samples via `DataParser` (ASCII or binary formats)
- Writes parsed samples to per-channel `RingBuffer<double>` instances

### 4. Per-Channel Processing (Before RingBuffer Storage)
**Critical**: All `IDataStream` implementations that support `IChannelConfigurable` apply processing **before** storing to ring buffers:

```csharp
private double ApplyChannelProcessing(int channel, double rawSample)
{
    ChannelSettings settings = _channelSettings[channel];
    IDigitalFilter filter = _channelFilters[channel];
    
    if (settings == null)
        return rawSample;
    
    // Apply gain and offset
    double processed = settings.Gain * (rawSample + settings.Offset);
    
    // Apply digital filter if configured
    if (filter != null)
    {
        processed = filter.Filter(processed);
    }
    
    return processed;
}
```

**Processing Order**:
1. **Offset** - Added to raw sample value
2. **Gain** - Multiplies the offset-adjusted value  
3. **Digital Filtering** - Applied to scaled value (see `Model/DigitalFilters.cs`)

**Available Filters** (`IDigitalFilter` implementations):
- `ExponentialLowPassFilter` - First-order IIR smoothing (alpha parameter)
- `ExponentialHighPassFilter` - DC removal via complementary low-pass
- `MovingAverageFilter` - Simple windowed averaging
- `MedianFilter` - Outlier rejection via median of window
- `NotchFilter` - Recursive 2nd-order notch (removes specific frequency)
- `AbsoluteFilter` - Takes absolute value of signal
- `SquaredFilter` - Squares the signal  
- `DownsamplingFilter` - Reduces sample rate with interpolation
- `BiquadFilter` - Generic 2-pole 2-zero IIR (programmable coefficients)

### 5. Up/Down Sampling (Optional Post-Processing)
If `IUpDownSampling` is supported and enabled, processed samples are resampled **after** gain/offset/filtering but **before** ring buffer storage:
- **Upsampling** - Interpolates additional samples (increases data rate)
- **Downsampling** - Decimates samples (reduces data rate)

This affects the reported `SampleRate` and number of samples stored.

### 6. Ring Buffer Storage
Processed (and optionally resampled) data is stored in lock-free thread-safe `RingBuffer<double>` instances:
- One ring buffer per channel per stream
- Fixed capacity (configured via `PlotSettings.BufferSize`)
- Overwrites oldest data when full (circular buffer behavior)

### 7. Plot Updates (UI Thread Timer)
`PlotManager` runs a `DispatcherTimer` at configured FPS (`PlotSettings.PlotUpdateRateFPS`):

```csharp
private void UpdatePlot(object sender, EventArgs e)
{
    // Check trigger condition if enabled
    bool shouldUpdatePlot = Settings.EnableEdgeTrigger 
        ? CheckTriggerCondition() 
        : true;
    
    if (shouldUpdatePlot)
    {
        CopyChannelDataToPlot();  // Pull data from ring buffers
        
        if (_fileWriter.IsRecording)
            _fileWriter.WritePendingSamples();  // Record to CSV
        
        if (Settings.YAutoScale)
            _plot.Plot.Axes.AutoScaleY();
        
        _plot.Refresh();  // ScottPlot GPU-accelerated render
    }
}
```

### 8. Data Copy to Plot Arrays
For each enabled channel, `PlotManager` calls:
```csharp
channel.CopyLatestDataTo(_data[i], Settings.Xmax);
```

This delegates to:
```csharp
_stream.CopyLatestTo(_indexWithinDatastream, destination, n);
```

Which efficiently copies the **latest N samples** from the ring buffer to ScottPlot's display arrays **without locking** (lock-free read).

### 9. GPU-Accelerated Rendering
ScottPlot renders the data arrays using GPU acceleration (`WpfPlotGL`) at the configured FPS, independent of data acquisition rate.

---

### Key Architectural Points

**Thread Safety**:
- Data acquisition runs on background threads (per stream)
- Plot updates run on UI dispatcher thread
- Ring buffers provide lock-free coordination between threads

**Processing Location**:
- **All transformations** (gain, offset, filters) happen **in the data acquisition thread** before storage
- Plot updates only **copy** pre-processed data from ring buffers
- This keeps the UI thread fast and responsive

**Channel-Centric API**:
- `Channel` objects abstract away stream/index resolution
- UI components work with `Channel` objects, not raw stream references
- Simplifies virtual channel support (filters/math operations create new streams transparently)

**Sample Rate Handling**:
- Physical streams calculate sample rate dynamically from incoming data
- Up/down sampling multiplies the base sample rate
- `IDataStream.SampleRate` property reflects the **final** sample rate after processing
- Used for time-axis scaling and CSV recording timestamps

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
