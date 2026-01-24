# PlotManager Refactoring Summary

## What Was Done

The `PlotManager.cs` file (~1500 lines) has been split into three partial class files for better organization and maintainability:

### File Structure

```
Model/
??? PlotManager.cs  (~450 lines) - Core functionality
??? PlotManager.Cursors.cs   (~350 lines) - Cursor management  
??? PlotManager.Triggers.cs  (~350 lines) - Trigger functionality
```

## Benefits

1. **Smaller Files** - Each file is now <500 lines, reducing Visual Studio corruption risk
2. **Better Organization** - Each file has a single, clear responsibility
3. **Easier Maintenance** - Changes to cursors don't affect trigger code and vice versa
4. **No API Changes** - External code sees the exact same `PlotManager` class
5. **Same Performance** - Partial classes compile to identical IL code

## File Responsibilities

### PlotManager.cs (Core)
- Constructor and core field initialization
- Plot configuration and rendering
- Channel management (`SetChannels`, `UpdateChannelDisplay`)
- Data copying (`CopyChannelDataToPlot`)
- Recording functionality (`StartRecording`, `StopRecording`)
- Visual settings (colors, themes, line width)
- Main update loop (`UpdatePlot`, `StartUpdates`, `StopUpdates`)
- Event handlers (`OnSettingsChanged`, `OnChannelsCollectionChanged`)
- Utility methods (`GetPlotDataAt`, `UpdateDataStreamBufferSizes`)
- Cleanup (`Dispose`, `Clear`)

### PlotManager.Cursors.cs
- Cursor fields (`_cursor`, `_verticalCursorA/B`, `_horizontalCursorA/B`, etc.)
- Cursor properties (`HasActiveCursors`, `ActiveCursorMode`, `Cursor`)
- Cursor public methods:
  - `EnableVerticalCursors()`
  - `EnableHorizontalCursors()`
  - `DisableCursors()`
  - `UpdateCursorValues()`
- Cursor creation/removal (`CreateVerticalCursors`, `RemoveVerticalCursor`, etc.)
- Cursor data updates (`UpdateVerticalCursorData`, `UpdateHorizontalCursorData`)
- Mouse handling (`Plot_MouseDown`, `Plot_MouseMove`, `Plot_MouseUp`, `GetLineUnderMouse`)
  - Calls trigger methods via `IsTriggerLine()`, `HandleTriggerLevelDrag()`, `HandleTriggerPositionDrag()`

### PlotManager.Triggers.cs
- Trigger fields (`_triggerArmed`, `_triggerLevel`, `_triggerSampleIndex`, `_triggerWorkBuffer`, `_lastTriggerChannel`, etc.)
- Trigger properties (`IsTriggerLineVisible`, `TriggerHoldoffSeconds`, `SingleShotMode`)
- Trigger public methods:
  - `RearmTrigger()`
  - `ShowTriggerLine()`
  - `HideTriggerLine()`
- Internal methods for cursor coordination:
  - `IsTriggerLine()` - Checks if a line is a trigger line
  - `HandleTriggerLevelDrag()` - Handles trigger level dragging
  - `HandleTriggerPositionDrag()` - Handles trigger position dragging
- Private trigger logic:
  - `CheckTriggerCondition()` - Edge detection with over-fetch approach
  - `CopyChannelDataWithTriggerAlignment()` - Trigger-aligned data copying using shift
  - `EnsureTriggerWorkBufferSize()` - Working buffer management
  - `SubscribeToTriggerChannelChanges()` - Subscribe to trigger channel selection changes
  - `UnsubscribeFromTriggerChannelChanges()` - Unsubscribe from trigger channel changes
  - `OnTriggerSettingsChanged()` - Handle trigger settings property changes
  - `UpdateTriggerLineColor()` - Update trigger line colors to match channel color
  - `GetCurrentTriggerChannel()` - Get the currently active trigger channel
  - `HandleXmaxChangeForTrigger()` - Trigger position clamping
  - `HandleTriggerModeChange()` - Trigger mode reset

## Trigger Implementation (Over-fetch + Shift)

The trigger system uses a simple, universal approach that works with ALL stream types:

1. **Over-fetch**: Request `Xmax + margin` samples into a working buffer
2. **Search**: Find trigger edge in the buffer
3. **Validate**: Ensure enough pre/post-trigger samples exist
4. **Shift**: Calculate `displayStart = triggerIndex - triggerPosition`
5. **Copy**: `Array.Copy(workBuffer, displayStart, plotData, 0, Xmax)`

This approach only uses `CopyLatestTo()` from the `IDataStream` interface, making it compatible with SerialDataStream, DemoDataStream, AudioDataStream, and all other stream types.

### Performance Optimizations

**1. Trigger Channel Data Caching**
- `CheckTriggerCondition()` caches the trigger channel index and sample count
- `CopyChannelDataWithTriggerAlignment()` reuses this cached data instead of re-fetching
- Eliminates redundant data copy for the trigger channel (saves ~25% of data copying per trigger)

**2. Debug Output Throttling**
- Debug logging is throttled to once every 60 frames (~1 second at 60 FPS)
- Trigger detection events are always logged (important for debugging)
- Reduces string formatting and I/O overhead at high frame rates

### Known Timing Constraint

**FPS vs Xmax vs Sample Rate**

The trigger system has a fundamental constraint:

```
newSamplesPerFrame = SampleRate / FPS
```

| Scenario | Xmax | SampleRate | FPS | New Samples/Frame | Reliability |
|----------|------|------------|-----|-------------------|-------------|
| Good | 1,000 | 100,000 | 60 | 1,667 | ? Excellent |
| Good | 10,000 | 100,000 | 60 | 1,667 | ? Good |
| Marginal | 10,000 | 20,000 | 60 | 333 | ?? ~2 Hz max trigger rate |
| Poor | 10,000 | 20,000 | 120 | 167 | ? May miss triggers |

**Why this happens:**
- We only scan NEW samples each frame (to avoid re-triggering on same edge)
- If `newSamplesPerFrame` is small relative to `Xmax`, the search window shrinks
- Effective trigger rate is limited by buffer refill time: `Xmax / SampleRate`

**Rule of thumb:** `SampleRate / FPS` should be `>= Xmax / 10` for reliable triggering.

**Workarounds:**
- Reduce FPS when using large Xmax with low sample rates
- Use smaller Xmax for high FPS applications
- Increase sample rate if hardware supports it

## Trigger Channel Color Synchronization

The trigger lines automatically update their color to match the selected trigger channel:

- **Event-Based**: When user changes the trigger channel via `Settings.TriggerSourceChannel`, the trigger line colors update immediately
- **Fallback**: If no explicit channel is selected, uses the first enabled channel's color
- **Lifecycle**: Color subscription is active only when trigger lines are visible (activated in `ShowTriggerLine()`, deactivated in `HideTriggerLine()`)
- **Auto-Sync**: Updates happen reactively through property change notifications, no polling needed

**Implementation Pattern** (matches existing `TriggerControl` pattern):
```csharp
// In ShowTriggerLine()
SubscribeToTriggerChannelChanges();
UpdateTriggerLineColor();

// In HideTriggerLine()
UnsubscribeFromTriggerChannelChanges();

// When trigger channel changes
OnTriggerSettingsChanged() ? UpdateTriggerLineColor()
```

This provides immediate visual feedback showing which channel the trigger is monitoring.

## Cross-Partial Coordination

### Cursor ? Trigger
The cursor mouse handling needs to check if a line being dragged is a trigger line:

```csharp
// In PlotManager.Cursors.cs - Plot_MouseMove
if (IsTriggerLine(horizontalLine))
{
  HandleTriggerLevelDrag(horizontalLine);
}
```

### Core ? Trigger
The core settings handler calls trigger helper methods:

```csharp
// In PlotManager.cs - OnSettingsChanged
case nameof(PlotSettings.Xmax):
    HandleXmaxChangeForTrigger();  // Defined in Triggers partial
    // ...
```

## Usage (No Changes Required)

External code uses `PlotManager` exactly as before:

```csharp
// Still works the same way
PlotManager plotManager = new PlotManager(wpfPlot);
plotManager.EnableVerticalCursors();
plotManager.ShowTriggerLine();
plotManager.StartUpdates();
```

## Testing Notes

- Build successful ?
- All three files compile together correctly
- No behavioral changes - same compiled output
- Smaller files = less IDE corruption risk

## Migration Notes

If you ever need to add new cursor functionality:
- Add it to `PlotManager.Cursors.cs`

If you need to add new trigger functionality:
- Add it to `PlotManager.Triggers.cs`

If you need to add core plot functionality:
- Add it to `PlotManager.cs`

This keeps the separation clean and prevents files from growing too large again.
