# Resampler refactor (deserves its own branch)

Notes from a code-quality pass on `Model/UpDownSampling.cs` — not urgent, revisit separately.

## Sentiment

DSP core is solid: sinc-windowed FIR, correct tail-state continuity across blocks,
recently vectorized, and cross-checked against a reference scalar implementation in
`Tests/UpDownSamplingTests.cs`. The surrounding product treatment is unfinished —
naming, UI presence, and runtime control all lag behind the algorithm quality.

## Steps

1. **Rename for clarity.** `UpDownSampling` describes the toggle, not the operation
   (rational resampling via insert-zeros+filter or filter+decimate). Rename to
   `Resampler` (or `SincResampler`). Cascade rename:
   - `Model/UpDownSampling.cs` → `Model/Resampler.cs`
   - `Tests/UpDownSamplingTests.cs` → `Tests/ResamplerTests.cs`
   - `IUpDownSampling` interface (`Model/DataStream.cs`) → consider `IResamplable`
   - Usages in `SerialDataStream.cs`, `USBDataStream.cs`, `AudioDataStream.cs`,
     `DemoDataStream.cs`, `StreamSettings.cs`, `Serializer.cs`.
   - `SamplingFactor` property name is fine; reconsider only if it reads awkwardly
     after the class rename.

2. **Fix the UI range mismatch.** `View/UserForms/StreamConfigWindow.xaml` ComboBox
   only exposes factors `-4..4` (1/5x–5x), but `UpDownSampling`/`Resampler` clamps to
   `-9..9` (1/10x–10x). Either widen the ComboBox or intentionally narrow the model's
   clamp to match — decide which range is actually useful before changing.

3. **Decide on runtime control.** `SamplingFactor` setter already resets internal
   filter state cleanly on change, so the model supports live adjustment on a running
   stream. Nothing in the UI currently exposes this after connecting — it's only set
   once in the connect-time dialog. Decide whether to wire live adjustment into the
   channel/stream control bar, or leave it connect-time-only by design.

## Out of scope for this branch

Should not be bundled with general code-quality/style cleanup elsewhere in the repo
(e.g. `PlotManager.cs`, `RingBuffer.cs` work) — keep this as an isolated branch since
it touches naming across multiple files and a UI surface.

## Bigger idea: reconfigurable channels/streams at runtime

Discussed as a possible home for runtime resampler control: an older, shelved idea
to let the user adjust stream parameters (baud rate, channel count, etc.) without
closing and reopening the stream config dialog. Worth designing properly before
touching code — this is bigger than the resampler rename above and has real
architectural fallout.

### Why this is harder than it looks

Today, `Channel` (`Model/Channel.cs`) is the identity every other part of the app
holds onto directly: `DataStreamBar`'s `ObservableCollection<Channel>`,
`PlotManager`'s channel list, `VirtualDataStream` (holds source `Channel`
references for derived math channels), `Measurement` (holds stream + channel
index), the MCP host (`IMcpHost.GetChannels()`, indices exposed to tool calls), and
`PlotFileWriter`/recording. None of these hold a stable "logical channel" handle
that's independent of the concrete `Channel`/`IDataStream` instance.

`Channel._stream` is `readonly` today — by design, a `Channel` is permanently
bound to one `IDataStream` instance for its lifetime. Swapping the underlying
stream (e.g. for a baud rate change) currently means destroying and recreating the
`Channel`, which invalidates every reference listed above.

### Two different kinds of "reconfigurable", worth not conflating

1. **In-place adjustable** — already supported today without touching stream
   identity: `IBufferResizable.BufferSize` (clears + recreates ring buffers in
   place), `IChannelConfigurable` (gain/offset/filter), and notably
   `IUpDownSampling.UpDownSamplingFactor` (`Model/DataStream.cs`) — this interface
   already exists and is already runtime-settable; only the UI never wired it up.
   The resampler's runtime control (item 3 in the Steps above) is *already* this
   category — no architectural change needed for that part.

2. **Structural** — baud rate, channel count, port/device selection, sample
   format. These change what the underlying transport is or how many logical
   channels exist, and genuinely require tearing down and rebuilding the
   `IDataStream` (and, when channel count shrinks/grows, adding or removing
   `Channel` objects). This is the part that would require the stream to be
   "deleted and restored in a consistent way," per the original idea.

### Revised plan: snapshot → teardown → rebuild → reapply (preferred over a stable identity layer)

Realized this doesn't need a new invariant — PowerScope already has this exact
pattern, just scoped to the whole session: `Serializer.cs` + `LoadConfiguration`
already serialize each channel's `ChannelSettings` and `Measurement` types
(`Serializer.cs:214-219`, `705-716`) and, on load, tear down all streams and
rebuild channels from that snapshot (`ApplyChannelSettings` re-adds measurements
onto the freshly created `Channel`). `PlotManager.SetChannels()` already supports
wholesale collection replacement cleanly too (unsubscribes old collection,
subscribes new one — no special-casing required).

So a per-stream reconfigure (baud rate, channel count, etc.) can reuse that same
flow at smaller scope instead of inventing in-place stream-swapping inside
`Channel`:

1. Snapshot the target stream's `ChannelSettings` + measurement types (same shape
   `Serializer.cs` already captures).
2. Stop/disconnect/dispose the old stream and its `Channel`s.
3. Create the new stream with the new structural parameters, create new
   `Channel`s.
4. Reapply the snapshotted settings/measurements onto the new channels (by index
   or label) — same `ApplyChannelSettings`-style step the session loader already
   does.
5. Replace the channel collection — `PlotManager.SetChannels()` already handles
   this safely.

This avoids touching the `Channel._stream` `readonly` invariant entirely and
reuses proven code instead of adding a new identity concept. Much smaller lift
than originally assessed.

### Decision: virtual channels are not carried over

Accepted scope cut — reconfigure simply drops any virtual channels built on the
reconfigured stream's channels, same as the existing (pre-existing, unrelated)
gap where `Serializer.cs` doesn't persist `VirtualDataStream`-derived channels
across a session reload either. Virtual channels are a nice-to-have feature that
isn't seeing heavy use, so it's not worth adding teardown/rebuild-by-label
bookkeeping for them just to support reconfigure. If usage changes later this can
be revisited, but for now: reconfigure (and reload) simply lose virtual channels,
and that's fine.

### Known gaps in that pattern (true today, would carry over)

- **The swap window.** MCP and the recorder query `GetChannels()` live rather than
  caching references, so they're naturally safe across a rebuild as long as no
  call lands mid-swap — worth briefly pausing/guarding rather than assuming the
  swap is instantaneous.
- **PlotManager/timer safety.** `UpdatePlot` runs on a `DispatcherTimer` reading
  channel data every tick; pause updates (or otherwise guarantee no read hits a
  half-torn-down stream) during steps 2-5 above.

### Recommendation

Treat the resampler's runtime-control step (Steps, item 3) as already deliverable
without any of this — it only needs UI wiring. Treat structural runtime
reconfiguration (baud rate, channel count) as a small design spike around the
snapshot/teardown/rebuild flow above. With virtual channels explicitly out of
scope, the remaining work is mostly plumbing (steps 1-5 above) plus the swap-
window and timer-safety guards — no new architectural concept needed.
