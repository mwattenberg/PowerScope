# PowerScope Code Review Instructions

This document guides a structured review of the PowerScope codebase. The review covers three independent themes — work through each separately, or assign them to parallel reviewers.

---

## Theme 1: Architectural Consistency

### Goal
Assess whether the codebase consistently applies three principles: **separation of concerns**, **single source of truth**, and **WPF MVVM**. Flag places where those principles are violated. When evaluating a potential fix, prefer duplicating a small piece of logic in two independent layers over introducing a dependency between them — a little repetition is far less harmful than a little hidden coupling.

### Separation of concerns

Each layer should own exactly one responsibility. Violations show up as a layer reaching into a neighbor's domain.

- Does `PlotManager` only orchestrate rendering, or does it also make decisions about which channels are enabled, what gain to apply, or how to parse data? Any business logic inside the render hub is a concern violation.
- Does `DataParser` know about channels, ring buffers, or UI state? It should accept raw bytes and return structured samples — nothing else. If it references anything above the `Model/` layer, note it.
- Does `Serializer.cs` reference WPF types (`DependencyObject`, `Control`, `FrameworkElement`)? Serialization is a model-layer responsibility; pulling in UI types creates a circular concern.
- Do the `View/UserControls/` code-behind files (`DataStreamBar`, `ChannelControlBar`, etc.) contain conditionals or calculations that belong in a ViewModel — e.g., computing a display string, deciding which controls to show based on stream type, or calling `PlotManager` directly? Code-behind should only wire bindings and forward user gestures.
- Does the MCP tool layer (`McpServer.cs`, `McpToolService.cs`) touch anything beyond `IMcpHost`? It must not reference `MainWindow`, `PlotManager`, or any WPF type. The `IMcpHost` interface is the seam; nothing should bypass it.

### Single source of truth

Each piece of state should have exactly one authoritative home. Violations show up as two places that must be kept in sync.

- Is channel enabled/disabled state held in `ChannelSettings`, or is it also tracked somewhere in `PlotManager` or a UI control? If both hold it, they will diverge.
- Is the ring buffer size stored in one place and read everywhere else, or is it duplicated between `ChannelSettings`, `IBufferResizable`, and the ring buffer itself?
- Does `Serializer.cs` reconstruct state by reading from the model, or does it read from UI controls directly? If it reads from controls, the XML file will diverge from the model whenever the user changes a setting without triggering a UI update.
- Are there magic numbers (sample rates, buffer depths, baud rates, timer intervals) defined in more than one file? Each such constant that appears twice is a future bug waiting for the two copies to drift.
- Check `PlotSettings` and `ChannelSettings`: if a setting is relevant to both, is it stored in one and referenced from the other, or copied into both?

### WPF MVVM pattern

MVVM means the View knows the ViewModel, the ViewModel knows the Model, and neither direction reverses. The ViewModel is fully testable without a WPF host.

- **Bindings, not code-behind logic.** Each `UserControl`'s code-behind should be near-empty: `InitializeComponent()`, event-to-command forwarding, and nothing else. Flag any property assignments, conditionals, or method calls on model/viewmodel objects that live in `.xaml.cs` files.
- **Commands, not event handlers.** Check whether user actions (button clicks, dropdown changes) are handled via `ICommand` bindings or via `Click="..."` event handlers wired in code-behind. Event handlers that call into the model are a MVVM violation.
- **ViewModel owns presentation state.** Things like "is this button enabled", "what color is this channel", "what label should this axis show" should be ViewModel properties, not computed inline in XAML triggers or in code-behind.
- **No ViewModel-to-View references.** A ViewModel must not hold a reference to a `Control`, `Window`, or `UIElement`. If a ViewModel needs to trigger a dialog, it should do so via a service interface or a command, not by calling `new MyDialog().ShowDialog()` directly.
- **`ChannelSettings` role.** It is described as both model and ViewModel. Determine which it actually is. If it implements `INotifyPropertyChanged` and is bound to the UI, it is a ViewModel — in that case it must not also be the thing `Serializer.cs` writes to XML, since serialization should operate on the model. If they are the same object, note the tension and whether it causes practical problems.

---

## Theme 2: GC Pressure, Computational Efficiency, and Threading

### Goal
Identify unnecessary heap allocations and threading problems in the computationally intensive parts of the model. The target is sustainable throughput at 3 MBaud+ across multiple channels — not micro-optimised cleverness. Prefer plain, classical imperative code (index loops, pre-allocated arrays, explicit state) over high-level abstractions. Rely on the compiler to optimise straightforward code; flag complexity that prevents it from doing so.

**General rule: avoid LINQ in the model layer.** LINQ expressions (`.Where`, `.Select`, `.OrderBy`, `.ToArray`, `.ToList`, etc.) allocate enumerator objects, intermediate arrays, and closure captures. In hot paths these become a steady stream of short-lived objects that pressure the GC. Replace them with plain `for` loops over pre-allocated buffers. Flag every LINQ expression found in the model — in non-hot UI code it is acceptable, but in `Model/` it should be the exception, not the norm.

**General rule: allocate once, reuse always.** Where a buffer, array, or working-space object is needed on every tick or every sample, it should be allocated at construction or stream-open time and reused in-place. Flag any `new T[]`, `new List<T>`, or `new SomeWorkingObject()` found inside a loop, a timer callback, or a method called from one.

### Locations to inspect

**`DataParser`**
- Is the output sample buffer (`double[][]` or equivalent) allocated once and filled in-place, or reallocated on every parse call?
- In the ASCII path: does the code call `string.Split`, `Trim`, `Substring`, or `ToString` per sample? Each produces a heap allocation. A span-based or index-walking approach avoids this entirely.
- In the binary path: are there intermediate `byte[]` copies, or does the parser read directly from the receive buffer using `BinaryPrimitives` or `BitConverter` with an offset?
- Is there a residual-bytes buffer for incomplete frames? Verify it is a fixed array with a length counter, not a `List<byte>` that is cleared and appended to.

**Measurements (`MeasurementBar`, `MeasurementBox`)**
- Are statistics (min, max, mean, RMS) computed by iterating the sample array once with running accumulators, or by multiple passes or LINQ aggregations?
- Is any intermediate collection allocated during measurement computation? A single sweep over a `double[]` with scalar accumulators needs no allocation at all.

**Digital filter**
- IIR filters maintain state across samples. Is the filter state (delay-line coefficients) stored in a fixed array field, or re-created each call?
- Are filter coefficients stored as plain `double[]` fields, or wrapped in objects that add indirection?
- Is the filter inner loop a plain `for` over the delay line, or does it use LINQ, delegates, or virtual dispatch per sample?

**`PlotManager` — render timer (30 Hz) and trigger search**
- Does `CopyLatestN()` write into a caller-supplied buffer, or return a new array? Returning `new double[]` at 30 Hz is ~30 allocations/sec per visible channel.
- Trigger search scans the sample buffer for an edge. Verify it is a single `for` loop with scalar state — no intermediate arrays, no LINQ, no delegate per-sample.
- Cursor math runs every render tick. Check `PlotManager.Cursors.cs` for intermediate allocations.
- Are ScottPlot data sources updated in-place (via `Update()` on an existing signal), or replaced with a new data object each tick?

**Up/down sampling (`IUpDownSampling` implementations)**
- Decimation and interpolation require working buffers. Are they allocated at construction, or per-call?
- If interpolation produces more samples than the input, is the output buffer large enough and pre-allocated, or grown dynamically?

**`VirtualDataStream`**
- Virtual channels compute element-wise over parent channel arrays. Verify the computation walks pre-allocated arrays with a `for` loop, not LINQ zip or projection.
- Is there a pre-allocated output buffer, or is a new `double[]` created per evaluation?

**`RingBuffer<T>`**
- Verify the backing store is a fixed-size `T[]` allocated once at construction. It must never reallocate.
- `CopyLatestN` should accept a caller-supplied `T[]` or `Span<T>` and fill it in-place.
- Confirm `T` is always a value type (`double`) so there is no boxing through the generic.

### Threading

Correct, efficient threading is as important as allocation behaviour. At 3 MBaud the acquisition thread runs continuously; the render timer fires at 30 Hz on the dispatcher thread. Failures here show up as data corruption, dropped samples, or wasted CPU.

**Correctness — shared state between threads**
- `RingBuffer<T>` is the primary shared structure between the acquisition thread and the render timer. Verify its read/write operations are safe for a single writer and single reader without a lock — or that an appropriate lock protects them if multiple writers are possible.
- Look for any other fields in `PlotManager`, `DataParser`, or `IDataStream` implementations that are written on one thread and read on another. Each such field either needs a lock, `volatile`, or `Interlocked` — or should be restructured so only one thread touches it.
- Verify `DataParser` is not shared across multiple stream instances if streams run concurrently. If it holds mutable state (residual buffer, parser position), it is not thread-safe to share.

**`SerialDataStream` / `USBDataStream` / `AudioDataStream`**
- Each stream likely owns a background thread or uses async I/O. Confirm that the read loop does not take a lock that the render timer also holds — that is a latency inversion.
- Check whether the stream start/stop path (`Connect`, `StartStreaming`, `StopStreaming`) is safe to call from the dispatcher thread while the background thread is running. Race conditions on state flags (`isRunning`, `isConnected`) are common here.
- Verify that exception handling on the background thread does not silently swallow errors that leave the stream in an inconsistent state.

**Scalability across CPU cores**
- If multiple streams are active simultaneously, do they each run on their own thread, or do they share one? Sharing one thread serialises acquisition and caps throughput at a single core.
- The render timer runs on the WPF dispatcher (single-threaded). Check whether any computation that could run off-thread (measurement calculations, trigger search over a large buffer, resampling) is blocked inside `UpdatePlot`. Long-running work on the dispatcher stalls the UI.
- If background `Task`s or `ThreadPool` work items are used, verify they do not inadvertently capture and access UI-thread state (controls, dependency properties) without marshalling back via `Dispatcher.Invoke`.

---

## Theme 3: Style Guide Compliance

### Goal
Check that the code conforms to `StyleGuide.txt`. This is a mechanical line-by-line pass — flag every violation, regardless of how minor. Consistency matters more than any individual rule.

### Control flow

- **No ternary operators.** Every `condition ? a : b` expression must be replaced with an explicit `if`/`else` block. Flag all occurrences, including those nested inside arguments or assignments.
- **No null-conditional operators.** Every `?.` and `??` must be replaced with an explicit null check. Flag all occurrences.
- **Explicit `if`/`else` — no early-exit shortcuts.** Conditions must be expressed as full `if`/`else` statements, not collapsed to single expressions.
- **Braces omitted for single-line bodies.** A single-statement `if` or `else` body must *not* use `{}`. Multi-statement bodies must always use `{}`. Flag both directions of violation (braces added where not needed, braces missing where needed).
- **Switch over chains of `if`/`else if`.** When three or more conditions test the same variable or expression, a `switch` statement is required. Flag `if`/`else if`/`else if` chains that should be switches.

### Expressions and methods

- **No expression-bodied members.** Every `=>` member (properties, methods, constructors, indexers) must be expanded to a full block body. Flag all `=>` in member declarations.
- **No chained method calls.** Each method call result must be assigned to a named local variable before the next call acts on it. Flag fluent chains like `a.B().C().D()`.
- **No LINQ.** Flag every LINQ method call (`Where`, `Select`, `Any`, `All`, `First`, `FirstOrDefault`, `Count`, `Sum`, `Min`, `Max`, `OrderBy`, `ToList`, `ToArray`, `ToDictionary`, etc.) and every query-syntax expression (`from … in … select`). Replace with `foreach` loops or `for` loops over explicit local variables.
- **Clear getter and setter methods.** Properties that contain non-trivial logic (more than returning or assigning a backing field) should be refactored to explicit `GetX()` / `SetX()` methods so the logic is visible at the call site.

### Variables and types

- **No `var`.** Every local variable declaration must carry an explicit type. Flag every `var` keyword.
- **Verbosity is acceptable.** Do not flag code as "too long" or "too verbose" — the style guide explicitly permits verbosity for the sake of clarity.

### Naming conventions

Check every identifier against the table below. Flag any deviation.

| Identifier kind | Required convention |
|---|---|
| Private fields | `_camelCase` |
| Public fields and properties | `PascalCase` |
| Local variables | `camelCase` |
| Method parameters | `camelCase` |
| Interfaces | `IPascalCase` |
| Classes | `PascalCase` |
| Methods | `PascalCase` |

### Defensive code

- Flag any code that swallows exceptions silently, returns a default value to hide a bug, or adds a null/bounds check that protects against a condition that should never occur given correct inputs. The rule is: if something is a bug, fix the bug — do not add a guard that hides it.

### Comments and dead code

- Flag commented-out code blocks. They should either be deleted or replaced with a `// TODO:` that references a specific known issue.
- Flag XML doc comments (`///`) on private members — the style guide does not require them and they add noise without benefit. Public interface surface (`IMcpHost`, `IDataStream`, public methods of `PlotManager`) is the appropriate place for doc comments.

### File-level hygiene

- Run `dotnet format --verify-no-changes` to catch whitespace and indentation issues not covered by the manual checks above.
- Check for mixed line endings (CRLF vs LF) with `git diff --check`.
- Verify a `.editorconfig` at the repo root encodes the naming and brace rules above so that tooling can enforce them automatically.

---

## Suggested Review Order

1. Run `dotnet format --verify-no-changes` and `dotnet build` first to establish a clean baseline.
2. Do Theme 3 (formatting) as a mechanical pass — fixes here reduce diff noise for Themes 1 and 2.
3. Do Theme 2 (GC pressure and threading) with a profiler attached if possible: run under `dotnet-trace` with the `gc-verbose` and `threading` providers while streaming at 3 MBaud across multiple channels, then inspect the allocation trace and thread-contention events to confirm or refute findings.
4. Do Theme 1 (architecture) last — it benefits from having already read the code during the other two passes.

## Out of Scope

- Feature correctness and domain logic (filter math, FFT accuracy, trigger edge detection).
- Performance of ScottPlot rendering internals — treat ScottPlot as a black box.
- USB/serial driver correctness — that belongs to a separate firmware/driver review.
