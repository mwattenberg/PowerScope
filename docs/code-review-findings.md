# PowerScope Code Review — Findings

**Date:** 2026-06-15
**Branch:** `feature/tcp-mcp-server`
**Scope:** Per `docs/code-review-instructions.md` — Theme 1 (Architectural Consistency), Theme 2 (GC Pressure, Efficiency, Threading), Theme 3 (Style Guide Compliance).
**Method:** Themes 1 and 2 were each reviewed by a dedicated read-only agent over the full `Model/` + `View/` tree; Theme 3 was a mechanical pass driven by pattern search across all source (excluding `obj/`/`bin/`). No source files were modified.

> Out of scope (unchanged from the brief): filter/FFT/trigger math correctness, ScottPlot rendering internals, USB/serial driver correctness.

---

## Executive Summary

The codebase has a clear layering spine (`IDataStream` → `RingBuffer` → `PlotManager` → View) and the **render/ring-buffer boundary is genuinely well engineered** — pre-allocated reuse, in-place ScottPlot updates, single-pass accumulators, span-based binary reads. The **MCP layer is exemplary** and the cleanest seam in the project.

The problems cluster in three recurring patterns:

1. **The Model layer leaks WPF.** `PlotSettings` returns `Brush`es and reads `Application.Current.Resources`; `ChannelSettings`/`PlotManager` carry `System.Windows.Media` types; `Serializer` (model) drives a `UserControl`. (Theme 1)
2. **Allocation pressure sits *upstream* of the ring buffer.** The parse-and-ingest path allocates `O(channels)` arrays per read cycle, the ASCII path allocates `O(lines × channels)` strings, `MovingAverageFilter` allocates per **sample**, and several worker arrays are reallocated from the dispatcher while background threads read them. (Theme 2)
3. **Pervasive style-guide drift.** `var`, `?.`/`??`, ternaries, and expression-bodied members appear in the hundreds; a `clearData()` method-naming violation is replicated across 7 files. (Theme 3)

Highest-value fixes, in order: remove per-sample / per-cycle allocations in `DataParser` + `MovingAverageFilter` + `UpDownSampling` (Theme 2, throughput); close the background-thread data races on run-flags and reallocated buffers (Theme 2, correctness); pull WPF `Brush`/resource lookups out of `PlotSettings` (Theme 1).

---

## Theme 1 — Architectural Consistency

### Separation of concerns

| Sev | Location | Violation |
|---|---|---|
| **High** | `Model/PlotSettings.cs:472-510` (`NormalModeButtonBackground`, `SingleModeButtonBackground`) | Model class returns `System.Windows.Media.Brush` and reads `Application.Current.Resources["PlotSettings_TitleBarBrush"]`. Model depends on the running WPF app + a resource key; not headless-testable; reverses the VM→Model direction. **Fix:** move brush selection to a `Style`/`DataTrigger` in the View; expose only the booleans. |
| **Medium** | `Model/PlotManager.cs` (+ `.Cursors.cs`, `.Triggers.cs`) | Render hub also owns recording orchestration (`StartRecording`/`StopRecording`/`FileWriter`), trigger edge-detection logic (`CheckTriggerCondition`, `.Triggers.cs:333-456`), and reads `Application.Current.Resources["Highlight_Normal"]` for cursor colors (`.Cursors.cs:129-130,151-152`). Trigger detection is DSP, not rendering. **Fix:** extract a pure edge-detector helper; resolve cursor colors in the View and pass them in. |
| **Medium** | `Model/Serializer.cs:1-7,14,22,63-227` | Model-layer serializer takes `DataStreamBar` (a `UserControl`) and drives it (`CreateDataStreamFromUserInput`, `AddChannelsForStream`, `GetChannelsForStream`); `using PowerScope.View.UserControls`. Session reconstruction is entangled with UI lifecycle. **Fix:** operate on `ObservableCollection<Channel>` + a stream-factory interface; let `DataStreamBar` adapt to it. |
| **Low** | `Model/PlotManager.cs:206,271` | Model calls `Application.Current.Dispatcher.BeginInvoke` directly. Acceptable for a hub that wraps `WpfPlotGL`, but spreads UI-thread assumptions. |

**Done well:** `DataParser` (Span<byte> in, `ParsedData` out, zero UI/channel knowledge); the MCP layer (`McpToolService`/`McpServer`/`IMcpHost`) touches only `IMcpHost` + model interfaces — `MainWindow.McpWindowHost` (`MainWindow.xaml.cs:445-519`) is the sole dispatcher bridge.

### Single source of truth

| Sev | Location | Violation |
|---|---|---|
| **Medium** | `PlotSettings.cs:114` (50 000), `Serializer.cs:263` (500 000), `MainWindow.xaml.cs:302` (5 000 000), `SerialDataStream.cs:206` (500 000) | Default ring-buffer depth is four different magic numbers in four files. The `PlotSettings` default is effectively dead — `MainWindow` overwrites it at startup. **Fix:** one `const`, referenced everywhere. |
| **Low** | `PlotSettings.cs:162` & `SerialDataStream.cs:83` | `SerialPortUpdateRateHz = 300` hard-coded independently in both. |

**Done well:** channel enabled state lives only in `ChannelSettings.IsEnabled`; `Channel.IsEnabled` and `PlotManager` read through rather than caching (`ChannelSettings.cs:55-67`, `Channel.cs:158-161`). Buffer size follows a correct **push** model (`PlotSettings.BufferSize` → `IBufferResizable` → `RingBuffer`, `PlotManager.cs:561-576`) rather than two synced copies.

### WPF MVVM

| Sev | Location | Violation |
|---|---|---|
| **High** | `View/UserControls/ChannelControl.xaml.cs:92-147,391-476` | Code-behind computes presentation state: `UpdateFilterButtonStyle`/`UpdateMeasureButtonStyle` set `Button.Background` from `Application.Current.Resources`; `UpdateTopColorBar` builds `LinearGradientBrush`es. Driven by manual `PropertyChanged` subscriptions, not bindings. **Fix:** bind `Background` via `DataTrigger`/converter; `ChannelSettings` already exposes `DisplayColor`. |
| **Medium** | `TriggerControl.xaml.cs:134-171`, `HorizontalControl.xaml.cs:85-102`, `MeasurementBar.xaml.cs:253-275`, `RunControl.xaml.cs:154-219` | Button highlight/active colors + content set imperatively in code-behind (`activeBrush`/`inactiveBrush`, runtime-built `StackPanel`+`TextBlock`). `TriggerControl` calls `FindResource` and switches on `TriggerEdge`. **Fix:** `Style`/`DataTrigger` bound to existing booleans. |
| **Medium** | All `View/UserControls/*.xaml.cs` — e.g. `ChannelControl.xaml.cs:149-217`, `HorizontalControl.xaml.cs:122-164`, `TriggerControl.xaml.cs:182-226`, `PlotSettingsWindow.xaml.cs:110-172` | Nearly all user actions are `Click=` handlers calling into the model (`Settings.Gain *= 2`, `_plotManager.ShowTriggerLine()`) with calculation (clamping/doubling) inline, not `ICommand`. `RelayCommand` exists (`MainWindow.xaml.cs:524`) but only for menu shortcuts. **Fix:** move the calculations into VM methods/commands; thin handlers acceptable thereafter. |
| **Medium** | `VirtualChannelSelectionBar.xaml.cs:348-451` | A full `Window` subclass (`ConstantValueInputDialog`) with hand-built `Grid`/`StackPanel`/`Button` layout in C#, bypassing XAML entirely. Dialogs also constructed directly in `ChannelControl.xaml.cs:195,207`. **Fix:** move the dialog to its own XAML. |
| **Medium** | `Model/ChannelSettings.cs` (dual role) | Simultaneously the persisted model (Serializer reads `Label/Color/IsEnabled/Gain/Offset/Filter`, `Serializer.cs:178-204`) **and** the bound VM (`INotifyPropertyChanged`, `DisplayColor`), living in `Model` namespace while depending on `System.Windows.Media.Color`. Works because it is the single source of truth, but couples the persistence format to presentation and makes the Model layer depend on WPF. **Fix (low urgency):** acceptable under the project's "duplication over coupling" philosophy — document it; split a plain serializable record only if it causes friction. |
| **Low** | `ChannelControl.xaml.cs:94,124,161-162,393` | `this.FindName(...)` string-keyed element lookups + mutation instead of binding. |

**Overall (Theme 1):** Layering spine is sound and the MCP seam is textbook. Weaknesses concentrate in (a) Model→WPF leakage and (b) presentation state computed imperatively in code-behind, with the same active/inactive color logic duplicated across four controls. Highest value: remove `Brush`/resource lookups from `PlotSettings`, then replace the hand-rolled color code-behind with XAML triggers bound to booleans those classes already expose.

---

## Theme 2 — GC Pressure, Efficiency, Threading

### Part A — Allocation / efficiency

| Sev | Cat | Location | Issue & impact |
|---|---|---|---|
| **High** | Alloc | `DataParser.cs:80-107,109-148,195-251` | Every parse allocates a fresh `double[N][]` + `new double[numberOfLines]` per channel; binary residue is `data.Slice(...).ToArray()`; framed path builds a `List<int>` of offsets. Hundreds of calls/sec at 3 MBaud → continuous garbage on the acquisition thread. **Fix:** fill caller-owned per-channel buffers; fixed residue array + length counter. |
| **High** | Alloc | `DataParser.cs:70,197,211,219` (ASCII path) | `Encoding.UTF8.GetString` → `Split(FrameEnd)` → per-line `Trim().Split(Separator)` → per-field `Trim()`. ≈ `1+1+L+L+L·(1+C)` heap objects per parse. Heaviest allocator in the hot path. **Fix:** index-walk the span with `Utf8Parser`/`double.TryParse(ReadOnlySpan<char>)`. |
| **High** | Alloc | `SerialDataStream.cs:740-807` (`AddDataToRingBuffers`), `USBDataStream.cs:766-794` | Per read cycle allocates `new double[channelsToProcess][]` + `new double[len]` per channel. **Fix:** reuse pre-sized grow-only per-channel buffers; process gain/offset/filter in place. |
| **High** | Alloc | `UpDownSampling.cs:205-237,241-288` | Each call allocates `up`/`y`/`outBlock` (up) or `raw`/`y`/`outBlock` (down) — 3 full-block arrays per channel per cycle. **Fix:** per-`ChannelState` reusable scratch buffers, grown only on block-size increase; return count + buffer. |
| **High** | Alloc | `DigitalFilters.cs:254-255` (`MovingAverageFilter.Filter`) | `double[] temp = new double[_windowSize]; _window.CopyTo(temp);` — a window-sized array allocated **per sample** (millions/sec) just to read the oldest element. **Fix:** expose tail element from `CircularBuffer` (Peek/indexer); running sum needs no copy. |
| **High** | Alloc | `Measurement.cs:858,900` (`FFT_Update`) | `SignalData.CreateFromRealSize(...)` + `new double[spectrumLength]` allocated every FFT update (several/sec per FFT measurement). **Fix:** cache both as fields keyed on `effectiveFftSize` (the class already caches window coefficients this way). |
| **Medium** | Alloc | `PlotManager.cs:182-199` (`GetSnapshot`) | `new double[count][]` + `new double[Xmax]` per channel — but on-demand only (MCP/recording), so bounded. |
| **Medium** | Eff | `Measurement.cs:945-977` | Peak detection O(spectrumLength × ±5 bins) + `List.Sort(lambda)` + per-update `Dispatcher.BeginInvoke` closure. **Fix:** cache comparator; marshal only when peaks change. |
| **Medium** | Eff | `Measurement.cs:795-797,813-815` | Std-dev/variance do two passes (mean, then squared diff). **Fix:** single-pass Welford. (Min/Max/Mean/RMS already correctly single-pass.) |
| **Medium** | Alloc/bug | `Measurement.cs:164-165` | `MeasurementWindowLength` setter does `Math.Max(100000, value)` — forces ≥100k doubles (~800 KB) and reallocates `_dataBuffer` on every set. Comment says "reasonable max" but `Max` enforces a **floor**. **Fix:** likely should be `Math.Min`; reallocate only on growth. |
| **Medium** | Alloc | `RingBuffer.cs:143-157` (`GetNewData`), `85-99` (`GetLatest`) | `GetNewData` allocates a `List<T>` per call; `GetLatest` is an iterator that **yields while holding `_lock`** (blocks the writer for the consumer's whole iteration). **Fix:** prefer `CopyLatestTo`; never yield under lock. (Verify hot-path callers — hot path correctly uses `CopyLatestTo`.) |

**Done well:** `VirtualDataStream.cs:275-339` (plain `for` over pre-allocated `_computeBuffer1/2`, grown 1.5×); `DataParser.cs:150-165` (`BinaryPrimitives.Read*LittleEndian` over span, no per-value copy); Biquad/Notch/Exponential/Median filters (fixed-field state, `MedianFilter` pre-allocated `_sortBuffer`); `RingBuffer.CopyLatestTo` (caller buffer, 1–2 `Array.Copy`, single backing `T[]`, `T=double` no boxing); `PlotManager` `_data`/`_signals` reuse + `_triggerWorkBuffer` + in-place ScottPlot `Signal` updates.

### Part B — Threading

| Sev | Cat | Location | Issue & impact |
|---|---|---|---|
| **High** | Correctness | `SerialDataStream.cs:59-61,148-170,458-514,572`; same in `USBDataStream.cs:75-77,623` | `_isStreaming`/`_isConnected` are plain `bool`, written from dispatcher (Start/Stop) **and** read thread (`HandleRuntimeDisconnection`), read in `while (_isStreaming && ...)` with no `volatile`/lock. Release JIT may not observe the flip → delayed shutdown, `Join(1000)` timeout. **Fix:** `volatile` / `Volatile.Read/Write` (USB already does this for the lifetime counters). |
| **High** | Correctness | `SerialDataStream.cs:297-325` (`BufferSize`→`InitializeRingBuffers`) vs read thread `AddDataToRingBuffers` | `BufferSize` setter replaces `ReceivedData` under `_channelConfigLock`; the read thread indexes `ReceivedData[channel]` **without** that lock. Torn reference / NRE / writes to a discarded buffer. Same shape in `AudioDataStream.BufferSize` vs `OnDataAvailable`. **Fix:** guard read-path access, or swap the array atomically and let the in-flight batch keep its captured reference. |
| **High** | Correctness | `USBDataStream.cs:249-260,667`; `SerialDataStream.cs:128-139` | `StatusMessage`/`IsConnected`/`IsStreaming` setters raise `PropertyChanged` directly from the background read thread into WPF bindings. Classic cross-thread WPF hazard. **Fix:** marshal notifications to the dispatcher. |
| **Medium** | Scalability | `SerialDataStream.cs:750-787` (`Parallel.For` per read cycle); `MeasurementBar.xaml.cs:85-95` (`Task.Run`+`Parallel.ForEach`) | `Parallel.For` across a few channels on every small batch (hundreds/sec) — scheduling overhead can exceed the work; contends ThreadPool; multiple streams fan out independently. **Fix:** plain `for` for typical channel counts; measure before keeping parallelism in the read loop. |
| **Medium** | Correctness | `Measurement.cs:666` (worker writes `_dataBuffer`) vs `164-165` (dispatcher reallocates it) | Reallocated from dispatcher while the measurement worker reads/writes it → torn reference / index past shrunk buffer. **Fix:** guard the swap, or resize only while stopped. |
| **Medium** | Correctness | `Measurement.cs:271-277,134-141,988-991` | `UpdateStatistics` raises `PropertyChanged` (Min/Max/Mean/SamplesCount) from the worker thread; `_fftPeaks` mutated on worker and read in a deferred UI `BeginInvoke` (`ApplyFFTPeakSorting`). Off-thread notify + unsynchronized list across two threads. **Fix:** snapshot peaks before marshalling; raise stat notifications via dispatcher. |
| **Medium** | Scalability | `PlotManager.cs:490-527` (`UpdatePlot`) | Trigger search, channel copies, optional `AutoScaleY`, and recording flush (`WritePendingSamples`) all run synchronously inside the 30 Hz dispatcher tick. Bounded today (~Xmax/channel) but competes with rendering on one thread as channels/Xmax/recording grow. **Fix:** keep copies on dispatcher; move trigger search + recording I/O off-thread if scale grows. |

**Done well:** `USBDataStream.cs:92-102` uses `Interlocked.Read/Add` + `Volatile` for lifetime counters / Win32 error — the model the serial run-flags should follow; `RingBuffer` consistently locks all mutating/reading ops (conservative but safe SPSC); `PlotManager` trigger search is a single `for` over the reused buffer.

**Note (latent):** `DataParser` is thread-safe **only** because each stream owns its instance and only its own read thread touches the residue. Document the single-owner-thread invariant so it is never shared across concurrent streams.

---

## Theme 3 — Style Guide Compliance

`StyleGuide.txt` is largely **not** enforced in the current code. The violations below are systematic, not incidental. Counts are approximate (pattern-search based, includes the whole tree minus `obj/`/`bin/`); the point is the scale, not the exact number.

### Control flow

- **Ternary operators — forbidden, ~30+ occurrences.** Examples: `FFT.xaml.cs:256`; `StreamConfigWindow.xaml.cs:165,203,778,784,806,809`; `RunControl.xaml.cs:106,234`; `FFTPeakData.cs:70`; `FileIOManager.cs:195,307`; `StreamInfoPanel.xaml.cs:113`; `FileDataStream.cs:269`; `McpToolService.cs:197`; `PlotSettings.cs:372`; `Serializer.cs:114,115,136,749,755,761,767,773,774,775`; `VirtualDataStream.cs:296`; `USBDataStream.cs:969,1048`; `StreamSettings.cs:589,590,613`; `VirtualChannelSelectionBar.xaml.cs:91`. **Each must become an explicit `if`/`else`.**
- **Null-conditional / null-coalescing (`?.`, `??`) — forbidden, ~140 occurrences across 33 files.** Pervasive in three idioms: `PropertyChanged?.Invoke(...)` (most files), `Dispose` patterns (`_x?.Dispose()` in `MainWindow.xaml.cs:116-436`, `PlotManager.cs:622,639`, stream classes), and value fallbacks (`CursorChannelModel.cs:95,103,111`; `FileDataStream.cs:36,97`; `FileIOManager.cs:59`; `PlotSettings.cs:63`; `DemoDataStream.cs:425`). **Each must become an explicit null check.** (This is the single largest style debt by volume — worth deciding policy before mass-editing, since `PropertyChanged?.Invoke` is idiomatic .NET.)
- **Switch over `if`/`else if` chains** — not exhaustively audited; spot-check `FileIOManager.cs:223-256` (`else if` chain on `line.Contains(...)`) and similar string-dispatch sites.

### Expressions and methods

- **Expression-bodied members — forbidden.** Confirmed examples: `McpServer.cs:106,110,137,167`; `PlotSettings.cs:162,464`; `UpDownSampling.cs:51,53,54,55`; `USBDataStream.cs:98-102`; `FileDataStream.cs:36,59`; `MeasurementBox.xaml.cs:50`; `StreamInfoPanel.xaml.cs:22`; `FileIOManager.cs:59`; `StreamConfigWindow.xaml.cs:954`. (The broad `=>` count is ~174, but most are legitimate lambdas — the member declarations above are the violations.) **Each must expand to a block body.**
- **LINQ in the Model layer.** `using System.Linq` appears in 19 files incl. Model: `Serializer`, `USBDataStream`, `SerialDataStream`, `PlotManager.Triggers`, `PlotManager.Cursors`, `Measurement`, `FileIOManager`, `DigitalFilters`, `DataStream`. Concrete calls: `FileIOManager.cs:66,70,158,301,373,388` (heavy — `ToList`/`Select`/`Any`/`Where`); `PlotManager.Cursors.cs:124,146` (`.GetXAxes().First()` — also a chained call); `Measurement.cs:137` (`.Take(10)`); `MainWindow.xaml.cs:456` (`.ToList()`). **Replace with `for`/`foreach`.** (`DataParser.cs:104,140,144` use `Span<T>.ToArray()` — not `System.Linq`, but flagged under Theme 2 for allocation.) — *This overlaps with Theme 2: the same LINQ has both a performance and a style reason to go.*
- **Chained method calls.** `PlotManager.Cursors.cs:124,146` (`_plot.Plot.Axes.GetXAxes().First().Range`). Audit fluent chains and assign intermediates to named locals.

### Variables and types

- **`var` — forbidden, ~169 occurrences across 28 files.** Heaviest in `StreamConfigWindow.xaml.cs` (27), `FilterConfigWindow.xaml.cs` (24), `FileIOManager.cs` (11), `VirtualChannelSelectionBar.xaml.cs` (12), `USBDataStream.cs`/`UpDownSampling.cs`/`PlotManager.cs`/`PlotManager.Cursors.cs` (8–9 each). **Every `var` must carry an explicit type.**
- **Verbosity:** the guide permits verbose code — do **not** flag length; only the rule violations above.

### Naming conventions

- **Method naming violation — `clearData()`, replicated in 7 files:** `AudioDataStream.cs:311`, `ConstantDataStream.cs:104`, `DemoDataStream.cs:569`, `FileDataStream.cs:321`, `SerialDataStream.cs:539`, `USBDataStream.cs:589`, `VirtualDataStream.cs:341`. Methods must be `PascalCase` → `ClearData()`. (A rename should be done across all `IDataStream` implementations together.)
- **Private fields:** spot-check found **no** non-`_camelCase` private fields — this convention appears to be followed (positive).

### Defensive code

- `SerialDataStream.cs:524-526` returns `0` early when not connected/streaming — check whether this hides a caller bug vs. a legitimate state. (Low confidence; flagged for human judgment per "fix bugs, don't hide them".)
- See Theme 2 for background-thread `catch` blocks that swallow errors and leave streams inconsistent — those are both a threading and a defensive-code concern.

### Comments and dead code

- **Commented-out code:** `SerialDataStream.cs:528-536` — a commented `try`/`catch` wrapper around the live `return`. Delete or convert to a `// TODO:` with a tracked reason.

### File-level hygiene (not yet run)

The brief asks to run these as part of the pass — **not executed in this review** (no build/format run was performed):

- `dotnet format --verify-no-changes` — recommend running to catch whitespace/indent (some files show irregular indentation, e.g. `ChannelTypeColorBrushConverter.cs:73-75`).
- `git diff --check` for mixed line endings.
- Add a root **`.editorconfig`** encoding the naming/brace/`var` rules so tooling enforces them — none was found at the repo root. This is the highest-leverage Theme 3 action: it turns the entire pervasive-drift category (var, expression-bodied, naming) into automated diagnostics.

---

## Prioritized Fix List

**Throughput (Theme 2):**
1. `DigitalFilters.MovingAverageFilter` — kill the per-sample array allocation (`DigitalFilters.cs:254`).
2. `DataParser` ASCII + binary — fill caller buffers, fixed residue array, span-walk parsing.
3. `UpDownSampling` + `AddDataToRingBuffers` — reuse per-channel scratch buffers.
4. `Measurement.FFT_Update` — cache `SignalData`/`magnitudes`; fix the `Math.Max(100000,…)` floor.

**Correctness (Theme 2):**
5. `volatile`/`Volatile` on `_isStreaming`/`_isConnected` (serial + audio; USB partly done).
6. Guard `ReceivedData`/`_dataBuffer` reallocation against the background read/measurement threads.
7. Marshal off-thread `PropertyChanged` (stream status, measurement stats) to the dispatcher.

**Architecture (Theme 1):**
8. Remove `Brush`/`Application.Current.Resources` from `PlotSettings` (High).
9. Replace hand-rolled color code-behind in `ChannelControl`/`TriggerControl`/`HorizontalControl` with XAML triggers.
10. Decouple `Serializer` from `DataStreamBar` via a stream-factory interface.

**Style (Theme 3):**
11. Add a root `.editorconfig`; rename `clearData()` → `ClearData()` across all streams; then mechanically clear `var`/ternary/expression-bodied/`?.` (decide policy on `PropertyChanged?.Invoke` first).
