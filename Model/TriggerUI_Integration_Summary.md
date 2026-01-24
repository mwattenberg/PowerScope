# Single-Shot Trigger UI Update Implementation

## Overview
Implemented bidirectional data flow between `PlotManager.Triggers` and `RunControl` to provide visual feedback when a single-shot trigger event occurs.

## Architecture

### Data Flow

```
PlotManager (Triggers Partial)
    ?
 ??? SingleShotTriggered Property (INotifyPropertyChanged)
            ?
         ??? RunControl.PlotManager
    ?
  ??? TriggerStatusText (XAML TextBlock)
    ?
            ??? Display: "Ready", "Waiting...", or "TRIGGERED!"
```

## Components Modified

### 1. **PlotManager.Triggers.cs**
Added public property and event notifications:

```csharp
// Public property exposing trigger state
public bool SingleShotTriggered
{
    get { return _singleShotTriggered; }
}

// Notification when trigger fires (in CheckTriggerCondition)
if (_singleShotMode)
{
    _singleShotTriggered = true;
    OnPropertyChanged(nameof(SingleShotTriggered));  // ? NEW
}

// Notification when trigger is re-armed (in RearmTrigger)
public void RearmTrigger()
{
    _singleShotTriggered = false;
    // ...
    OnPropertyChanged(nameof(SingleShotTriggered));  // ? NEW
}
```

### 2. **RunControl.xaml**
Added status indicator TextBlock:

```xaml
<!-- Trigger status indicator - shows when single-shot trigger has fired -->
<TextBlock x:Name="TriggerStatusText"
      Text="Ready"
  Foreground="LimeGreen"
 TextAlignment="Center"
 Margin="0,8,0,0"
      FontSize="10"
           FontWeight="Bold"
           ToolTip="Shows single-shot trigger status"/>
```

**Display States:**
- **"Ready"** (LimeGreen) - Normal mode, not single-shot
- **"Waiting..."** (Yellow) - Single-shot armed, waiting for trigger
- **"TRIGGERED!"** (Red) - Single-shot trigger has fired

### 3. **RunControl.xaml.cs**
Added PlotManager reference and trigger monitoring:

```csharp
// Property to set PlotManager reference
public PlotManager PlotManager
{
    get { return _plotManager; }
    set
    {
    // Subscribe to PropertyChanged events
      if (_plotManager != null)
         _plotManager.PropertyChanged -= PlotManager_PropertyChanged;
        
  _plotManager = value;
        
        if (_plotManager != null)
        {
  _plotManager.PropertyChanged += PlotManager_PropertyChanged;
            UpdateTriggerStatusDisplay();
        }
    }
}

// Listener for trigger state changes
private void PlotManager_PropertyChanged(object sender, PropertyChangedEventArgs e)
{
    if (e.PropertyName == nameof(PlotManager.SingleShotTriggered))
        UpdateTriggerStatusDisplay();
}

// Update display based on trigger state
private void UpdateTriggerStatusDisplay()
{
    if (_plotManager.SingleShotMode && _plotManager.SingleShotTriggered)
        TriggerStatusText.Text = "TRIGGERED!";  // Red
    else if (_plotManager.SingleShotMode && !_plotManager.SingleShotTriggered)
        TriggerStatusText.Text = "Waiting...";  // Yellow
    else
        TriggerStatusText.Text = "Ready"; // LimeGreen
}
```

### 4. **MainWindow.xaml.cs**
Connected the pieces:

```csharp
void InitializeControls()
{
    // ... existing code ...
    
    // Set PlotManager for RunControl to monitor trigger state
 RunControl.PlotManager = _plotManager;
    
    // ... rest of initialization ...
}
```

## How It Works

1. **User enables Single-Shot Mode** via TriggerControl buttons (Normal/Single)
   - `PlotManager.SingleShotMode = true`
   - RunControl displays "Waiting..." (Yellow)

2. **Trigger Condition is Met**
   - Edge detection fires in `CheckTriggerCondition()`
   - `_singleShotTriggered = true`
   - `OnPropertyChanged(nameof(SingleShotTriggered))` fires

3. **Property Change Event Propagates**
   - RunControl's `PlotManager_PropertyChanged()` receives notification
   - `UpdateTriggerStatusDisplay()` is called
   - TextBlock updates to "TRIGGERED!" (Red)

4. **User Re-arms Trigger** via TriggerControl
   - Calls `PlotManager.RearmTrigger()`
   - Sets `_singleShotTriggered = false`
   - `OnPropertyChanged(nameof(SingleShotTriggered))` fires
   - TextBlock returns to "Waiting..." (Yellow)

## Benefits

- ? **Automatic Updates** - No polling or manual updates needed
- ? **Real-time Feedback** - User sees trigger state immediately
- ? **Clean Architecture** - PlotManager doesn't know about UI, only exposes properties
- ? **Decoupled** - RunControl can be reused anywhere with any PlotManager
- ? **Observable Pattern** - Uses standard .NET INotifyPropertyChanged
- ? **Type-Safe** - Uses `nameof()` for property names (refactoring safe)

## Testing

To test the implementation:

1. Enable trigger mode (TriggerControl)
2. Set to "Single" mode (TriggerControl)
3. Configure trigger level and edge type
4. RunControl shows "Waiting..."
5. When trigger fires, RunControl shows "TRIGGERED!"
6. Click "Normal" or "Single" button to re-arm
7. Returns to "Waiting..."
