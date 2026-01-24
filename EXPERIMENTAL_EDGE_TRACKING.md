# Edge ID Tracking for Trigger System

## Status: PRODUCTION - Standard Implementation

This feature is now the standard trigger implementation after successful testing and validation.

## The Problem: Rolling Trigger

### Why Edge Tracking is Needed

If we scan the entire buffer every frame looking for trigger conditions, the **same edge crossing 
may appear in multiple consecutive frames** (until it scrolls out of the buffer). Without tracking,
we would re-trigger on that same edge every frame, causing a "rolling" display effect where the
waveform appears to drift.

```
Frame 1: Buffer contains edge E1 at position 500 ? Trigger!
Frame 2: Buffer still contains E1 at position 450 ? Trigger again! (wrong)
Frame 3: Buffer still contains E1 at position 400 ? Trigger again! (wrong)
... display "rolls" as same edge keeps re-triggering
```

### The Old Solution (Limited)

The old approach was to **only scan NEW samples** each frame:
- Calculate how many samples arrived since last frame
- Only search those new samples for edges
- This prevented rolling, but had a major limitation:

**Problem:** If trigger is at LEFT (10%), we need 90% post-trigger data, but new samples 
arrive at the END of the buffer. The search window often excluded new samples entirely,
making left-side triggers unreliable.

### The New Solution: Edge ID Tracking

Each edge crossing has a **unique identity** based on its absolute position in the stream:

```csharp
absoluteEdgeIndex = TotalSamples - (bufferSamplesCopied - localBufferIndex)
```

We track which edge we last triggered on:
- Scan the **ENTIRE** valid window (not just new samples)
- Only trigger on edges with `absoluteIndex > lastTriggeredIndex`
- This naturally filters out edges we've already seen

```
Frame 1: Find E1 at absoluteIndex=1000 ? 1000 > -1 ? ? Trigger! Save 1000
Frame 2: Find E1 at absoluteIndex=1000 ? 1000 > 1000 ? ? Skip (already triggered)
         Find E2 at absoluteIndex=1500 ? 1500 > 1000 ? ? Trigger! Save 1500
Frame 3: Find E2 at absoluteIndex=1500 ? 1500 > 1500 ? ? Skip
         Find E3 at absoluteIndex=2000 ? 2000 > 1500 ? ? Trigger! Save 2000
```

## Benefits

| Aspect | Old Approach | New Approach |
|--------|--------------|--------------|
| Trigger at LEFT (10%) | ? Unreliable | ? Works |
| Trigger at CENTER (50%) | ?? Sometimes | ? Works |
| Trigger at RIGHT (90%) | ? Reliable | ? Works |
| Post-trigger viewing | ? Limited | ? Full support |
| Rolling trigger | ? Prevented | ? Prevented |
| Code complexity | Higher (2 concepts) | Lower (1 concept) |

## Testing Results

? **All positions tested and working:**
- Trigger at 10% (LEFT) - captures post-trigger data reliably
- Trigger at 50% (CENTER) - works as expected  
- Trigger at 90% (RIGHT) - works as expected
- Single-shot mode at all positions - behaves correctly
- No "rolling trigger" behavior observed
- Performance acceptable at 60 FPS with large Xmax

## Code Implementation

### Core Concept

```csharp
// Calculate absolute sample index for this edge
long absoluteEdgeIndex = currentTotalSamples - (samplesCopied - i);

// Only trigger if this is a NEW edge (prevents rolling trigger)
if (absoluteEdgeIndex > _lastTriggeredEdgeAbsoluteIndex)
{
    // Valid new edge - trigger on it
    _lastTriggeredEdgeAbsoluteIndex = absoluteEdgeIndex;
    // ... trigger logic ...
}
```

### Key Changes

- Removed: `_triggerArmed` field (replaced by edge tracking)
- Removed: `newSamplesStart` calculation (no longer needed)
- Removed: Re-arm logic (`shouldRearm`, etc.)
- Added: `_lastTriggeredEdgeAbsoluteIndex` field
- Modified: Search entire valid window: `for (int i = searchStart; i < searchEnd; i++)`

## Performance

| Metric | Impact |
|--------|--------|
| Search range | ~10x more (entire window vs new samples only) |
| Per-iteration cost | Same - simple comparisons |
| Real-world impact | Negligible |

The actual bottleneck is ScottPlot rendering, not trigger search. CPU handles billions of simple comparisons per second.

## Files Modified

- `Model/PlotManager.Triggers.cs` - Standard implementation with edge tracking

## Status: PRODUCTION
? **Tested and validated** - Now the standard trigger implementation
