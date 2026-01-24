using System;
using System.Linq;
using ScottPlot.Plottables;

namespace PowerScope.Model
{
    /// <summary>
    /// PlotManager partial class - Trigger functionality
    /// Handles all trigger-related operations including edge detection, holdoff, and trigger lines
    /// 
    /// Trigger approach: Over-fetch + Shift
    /// 1. Fetch more data than needed (Xmax + margin) into a working buffer
    /// 2. Search for trigger condition in the buffer
    /// 3. Calculate display window so trigger appears at _triggerPosition
    /// 4. Copy aligned window to plot data arrays
    /// This works with ALL stream types using only CopyLatestTo()
    /// 
    /// PERFORMANCE CONSTRAINT: Trigger Rate vs Sample Rate vs Xmax vs Trigger Position
    /// ================================================================================
    /// The trigger system has a fundamental timing constraint that affects reliability
    /// at high plot update rates (FPS) combined with large Xmax and low sample rates.
    /// 
    /// The constraint: newSamplesPerFrame = SampleRate / FPS
    /// 
    /// CRITICAL INSIGHT: Trigger Position Affects Search Window Size
    /// -------------------------------------------------------------
    /// The trigger position determines how much of the new data can be searched:
    /// 
    /// - Trigger at LEFT (position near 0): 
    ///   Most of the display window is POST-trigger data.
    ///   We need many samples AFTER the trigger point to fill the display.
    ///   Search window is constrained to: [preTriggerSamples, samplesCopied - postTriggerSamples]
    ///   With postTriggerSamples ? Xmax, the search window becomes very small.
    ///   
    /// - Trigger at RIGHT (position near Xmax):
    ///   Most of the display window is PRE-trigger data.
    ///   We need many samples BEFORE the trigger point (already in buffer).
    ///   Search window can extend much further into new samples.
    ///   This maximizes the chance of detecting triggers in incoming data.
    /// 
    /// Example: Xmax=10000, SampleRate=20000, FPS=60, newSamplesPerFrame?333
    /// 
    ///   Trigger at 10% (position=1000):
    ///     - preTriggerSamples = 1000, postTriggerSamples = 9000
    ///     - Search window: [1000, samplesCopied-9000] ? very narrow
    ///     - Most new samples fall outside the searchable range
    ///     - Triggers are frequently missed
    ///   
    ///   Trigger at 90% (position=9000):
    ///     - preTriggerSamples = 9000, postTriggerSamples = 1000
    ///     - Search window: [9000, samplesCopied-1000] ? much wider
    ///     - New samples fall within the searchable range
    ///     - Triggers are detected reliably
    /// 
    /// RECOMMENDATION: For challenging scenarios (high FPS, large Xmax, low sample rate),
    /// place the trigger position toward the RIGHT side of the display (70-95% of Xmax).
    /// This maximizes the effective search window for incoming data.
    /// 
    /// Example scenarios with trigger at right (90%):
    /// 
    /// GOOD: Xmax=1000, SampleRate=100000, FPS=60
    ///   - New samples per frame: 100000/60 = 1667 samples
    ///   - Search window is large, triggers detected reliably
    ///   - Time to refill buffer: 1000/100000 = 10ms (fast)
    /// 
    /// ACCEPTABLE: Xmax=10000, SampleRate=20000, FPS=60, trigger at 90%
    ///   - New samples per frame: 20000/60 = 333 samples
    ///   - With trigger at right, search window includes new samples
    ///   - Triggers detected reliably despite low sample rate
    /// 
    /// PROBLEMATIC: Xmax=10000, SampleRate=20000, FPS=60, trigger at 10%
    ///   - Same parameters but trigger at left
    ///   - Search window excludes most new samples
    ///   - Triggers frequently missed
    /// 
    /// Summary of recommendations:
    /// - For high FPS (60+), use smaller Xmax or higher sample rates
    /// - Rule of thumb: SampleRate/FPS should be >= Xmax/10 for reliable triggering
    /// - Or reduce FPS when using large Xmax with low sample rates
    /// - Place trigger position toward the RIGHT (70-95%) for best results
    /// 
    /// Technical reason:
    /// We only scan NEW samples each frame to avoid re-triggering on the same edge.
    /// If new samples per frame is small relative to Xmax, the search window shrinks,
    /// and the time between valid trigger opportunities increases.
    /// Placing the trigger at the right side minimizes postTriggerSamples, which
    /// maximizes the portion of new samples that fall within the valid search range.
    /// </summary>
    public partial class PlotManager
    {
        // Trigger state tracking fields
        private bool _triggerArmed = true;
        private double _triggerLevel = 0.0;
        private long _lastCheckedTotalSamples = 0;
        private int _triggerSampleIndex = -1;
        private int _triggerPosition = 100;
        private double _triggerHoldoffSeconds = 0.0;
        private DateTime _lastTriggerTime = DateTime.MinValue;

        // Working buffer for over-fetching (allows shifting for trigger alignment)
        private double[] _triggerWorkBuffer;
        private int _triggerWorkBufferSize = 0;

        // Debug output throttling (reduces overhead at high FPS)
        private int _triggerDebugCounter = 0;
        private const int TriggerDebugInterval = 60; // Log every 60 frames (~1 second at 60 FPS)

        // Trigger line plottables
        private HorizontalLine _triggerLevelLine;
        private VerticalLine _triggerPositionLine;
        private bool _triggerLineVisible = false;

        // Trigger channel color tracking
        private Channel _lastTriggerChannel = null;

        #region Trigger Properties

        /// <summary>
        /// Whether the trigger level line is currently visible
        /// </summary>
        public bool IsTriggerLineVisible
        {
            get { return _triggerLineVisible; }
        }

        /// <summary>
        /// Trigger holdoff time in seconds (0 = disabled)
        /// Prevents re-triggering for specified time after a trigger event
        /// </summary>
        public double TriggerHoldoffSeconds
        {
            get { return _triggerHoldoffSeconds; }
            set
            {
                if (_triggerHoldoffSeconds != value)
                {
                    _triggerHoldoffSeconds = Math.Max(0.0, value);
                    OnPropertyChanged(nameof(TriggerHoldoffSeconds));
                    System.Diagnostics.Debug.WriteLine($"Trigger holdoff set to: {_triggerHoldoffSeconds:F3} seconds");
                }
            }
        }

        /// <summary>
        /// Single-shot trigger mode
        /// When true, trigger fires once and system stops
        /// When false (default), trigger continuously
        /// </summary>
        public bool SingleShotMode
        {
            get { return Settings != null && Settings.SingleShotMode; }
            set
            {
                if (Settings != null)
                {
                    Settings.SingleShotMode = value;
                }
            }
        }

        #endregion

        #region Trigger Public Methods

        /// <summary>
        /// Shows the trigger level line at the current trigger level
        /// </summary>
        public void ShowTriggerLine()
        {
            if (_triggerLevelLine != null)
                return;

            // Get current y-axis limits to place trigger line in middle
            var limits = _plot.Plot.Axes.GetYAxes().First().Range;
            double initialTriggerLevel = (limits.Min + limits.Max) / 2.0;
            _triggerLevel = initialTriggerLevel;

            // Reset trigger tracking when showing trigger line
            _triggerArmed = true;
            _lastCheckedTotalSamples = 0;
            _lastTriggerTime = DateTime.MinValue;

            // Create trigger level line with label
            _triggerLevelLine = _plot.Plot.Add.HorizontalLine(_triggerLevel);
            _triggerLevelLine.IsDraggable = true;
            _triggerLevelLine.LineWidth = 1;
            _triggerLevelLine.Color = ScottPlot.Color.FromHex("#808080");
            _triggerLevelLine.LinePattern = ScottPlot.LinePattern.Dashed;
            _triggerLevelLine.Text = $"Trigger: {_triggerLevel:F1}";

            // Create trigger position line without label
            _triggerPositionLine = _plot.Plot.Add.VerticalLine(_triggerPosition);
            _triggerPositionLine.IsDraggable = true;
            _triggerPositionLine.LineWidth = 1;
            _triggerPositionLine.Color = ScottPlot.Color.FromHex("#FFA500");
            _triggerPositionLine.LinePattern = ScottPlot.LinePattern.Dashed;
            _triggerPositionLine.Text = string.Empty;

            // Setup mouse handling if needed
            if (!_cursorMouseHandlingEnabled)
            {
                SetupCursorMouseHandling();
            }

            // Subscribe to trigger channel changes
            SubscribeToTriggerChannelChanges();
            
            // Set initial color based on trigger channel
            UpdateTriggerLineColor();

            _triggerLineVisible = true;
            OnPropertyChanged(nameof(IsTriggerLineVisible));
            _plot.Refresh();
        }

        /// <summary>
        /// Hides the trigger level line
        /// </summary>
        public void HideTriggerLine()
        {
            if (_triggerLevelLine != null)
            {
                _plot.Plot.Remove(_triggerLevelLine);
                _triggerLevelLine = null;
            }

            if (_triggerPositionLine != null)
            {
                _plot.Plot.Remove(_triggerPositionLine);
                _triggerPositionLine = null;
            }

            _triggerLineVisible = false;
            OnPropertyChanged(nameof(IsTriggerLineVisible));

            // Unsubscribe from trigger channel changes
            UnsubscribeFromTriggerChannelChanges();

            // Remove mouse handling only if no cursors are active
            if (!HasActiveCursors && _cursorMouseHandlingEnabled)
            {
                RemoveCursorMouseHandling();
            }

            _plot.Refresh();
        }

        #endregion

        #region Trigger Internal Methods (used by Cursors partial)

        /// <summary>
        /// Checks if the given line is a trigger line
        /// Called from cursor mouse handling
        /// </summary>
        internal bool IsTriggerLine(AxisLine line)
        {
            return line == _triggerLevelLine || line == _triggerPositionLine;
        }

        /// <summary>
        /// Handles dragging of the trigger level line
        /// Called from cursor mouse handling
        /// </summary>
        internal void HandleTriggerLevelDrag(HorizontalLine line)
        {
            _triggerLevel = line.Y;
        }

        /// <summary>
        /// Handles dragging of the trigger position line
        /// Called from cursor mouse handling
        /// Clamps position to 5%-95% of Xmax to keep trigger visible
        /// </summary>
        internal void HandleTriggerPositionDrag(VerticalLine line)
        {
            int minPosition = (int)(0.05 * Settings.Xmax);
            int maxPosition = (int)(0.95 * Settings.Xmax);
            _triggerPosition = (int)Math.Clamp(line.X, minPosition, maxPosition);
            line.X = _triggerPosition;
        }

        #endregion

        #region Trigger Private Methods

        /// <summary>
        /// Subscribes to trigger channel change notifications from Settings.
        /// Called when trigger lines are shown to enable reactive color updates.
        /// </summary>
        private void SubscribeToTriggerChannelChanges()
        {
            if (Settings != null)
            {
                Settings.PropertyChanged += OnTriggerSettingsChanged;
            }
        }

        /// <summary>
        /// Unsubscribes from trigger channel change notifications.
        /// Called when trigger lines are hidden to stop listening for changes.
        /// </summary>
        private void UnsubscribeFromTriggerChannelChanges()
        {
            if (Settings != null)
            {
                Settings.PropertyChanged -= OnTriggerSettingsChanged;
            }
        }

        /// <summary>
        /// Handles trigger-related settings changes (e.g., TriggerSourceChannel).
        /// Updates trigger line color when the selected trigger channel changes.
        /// </summary>
        private void OnTriggerSettingsChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PlotSettings.TriggerSourceChannel))
            {
                UpdateTriggerLineColor();
            }
        }

        /// <summary>
        /// Updates the color of trigger lines to match the selected trigger channel.
        /// If no explicit channel is selected, uses the first enabled channel.
        /// Falls back to gray if no channels are available.
        /// </summary>
        private void UpdateTriggerLineColor()
        {
            if (_triggerLevelLine == null || _triggerPositionLine == null)
                return;

            Channel triggerChannel = GetCurrentTriggerChannel();

            // Determine color
            ScottPlot.Color newColor;
            if (triggerChannel != null)
            {
                newColor = new ScottPlot.Color(
                    triggerChannel.Color.R,
                    triggerChannel.Color.G,
                    triggerChannel.Color.B);
            }
            else
            {
                newColor = ScottPlot.Color.FromHex("#808080");  // Gray fallback
            }

            // ALWAYS update colors, don't check _lastTriggerChannel
            _triggerLevelLine.Color = newColor;
            _triggerPositionLine.Color = newColor;
            
            // Track for future reference
            _lastTriggerChannel = triggerChannel;

            if (triggerChannel != null)
                System.Diagnostics.Debug.WriteLine($"Trigger lines: {triggerChannel.Label}");
            else
                System.Diagnostics.Debug.WriteLine($"Trigger lines: no channel (gray)");

            _plot.Refresh();
        }

        /// <summary>
        /// Gets the current trigger channel.
        /// Returns the explicitly configured trigger channel, or the first enabled channel if none is configured.
        /// </summary>
        private Channel GetCurrentTriggerChannel()
        {
            Channel triggerChannel = Settings.TriggerSourceChannel;

            // If no explicit channel set, find first enabled channel
            if (triggerChannel == null && _channels != null && _channels.Count > 0)
            {
                for (int i = 0; i < _channels.Count; i++)
                {
                    if (_channels[i].IsEnabled)
                    {
                        triggerChannel = _channels[i];
                        break;
                    }
                }
            }

            return triggerChannel;
        }

        /// <summary>
        /// Ensures the trigger working buffer is properly sized.
        /// The buffer needs to be larger than Xmax to allow for trigger alignment shifting.
        /// </summary>
        private void EnsureTriggerWorkBufferSize()
        {
            // We need extra margin to shift data for trigger alignment
            // Margin = max possible shift = max(triggerPosition, Xmax - triggerPosition)
            // Use Xmax/2 as a reasonable margin that covers most cases
            int margin = Settings.Xmax / 2;
            int requiredSize = Settings.Xmax + margin;

            if (_triggerWorkBuffer == null || _triggerWorkBufferSize < requiredSize)
            {
                _triggerWorkBuffer = new double[requiredSize];
                _triggerWorkBufferSize = requiredSize;
                System.Diagnostics.Debug.WriteLine($"Trigger work buffer resized to {requiredSize} (Xmax={Settings.Xmax}, margin={margin})");
            }
        }

        /// <summary>
        /// Checks if trigger condition is met for edge trigger mode.
        /// Uses over-fetch approach: fetches extra samples, finds trigger, stores index for alignment.
        /// Only triggers on the explicitly selected trigger channel - does not auto-fallback to other channels.
        /// </summary>
        private bool CheckTriggerCondition()
        {
            // Get the explicitly selected trigger channel
            Channel triggerChannel = Settings.TriggerSourceChannel;

            // If no channel selected or channel is disabled, stop triggering
            // (plot freezes on last valid frame)
            if (triggerChannel == null || !triggerChannel.IsEnabled)
                return false;

            // Trigger holdoff: check if we're still in holdoff period
            if (_triggerHoldoffSeconds > 0.0)
            {
                TimeSpan timeSinceLastTrigger = DateTime.Now - _lastTriggerTime;
                if (timeSinceLastTrigger.TotalSeconds < _triggerHoldoffSeconds)
                    return false;
            }

            // Get current total samples from stream
            long currentTotalSamples = triggerChannel.OwnerStream.TotalSamples;
            long newSampleCount = currentTotalSamples - _lastCheckedTotalSamples;

            if (newSampleCount <= 0)
                return false;

            // Initialize tracking on first check
            if (_lastCheckedTotalSamples == 0)
            {
                _lastCheckedTotalSamples = currentTotalSamples;
                return false;
            }

            // Throttled debug output - sample arrival rate
            _triggerDebugCounter++;
            bool shouldLogDebug = (_triggerDebugCounter % TriggerDebugInterval == 0);
            
            if (shouldLogDebug)
            {
                System.Diagnostics.Debug.WriteLine($"NewSampleCount: {newSampleCount} (per frame, every {TriggerDebugInterval} frames logged)");
            }

            // Ensure working buffer is properly sized
            EnsureTriggerWorkBufferSize();

            // Calculate how many samples we need to fetch
            int preTriggerSamples = _triggerPosition;
            int postTriggerSamples = Settings.Xmax - _triggerPosition;
            int fetchCount = Settings.Xmax + Math.Max(preTriggerSamples, postTriggerSamples);
            fetchCount = Math.Min(fetchCount, _triggerWorkBufferSize);

            // Fetch data into working buffer
            int samplesCopied = triggerChannel.CopyLatestDataTo(_triggerWorkBuffer, fetchCount);

            if (samplesCopied < Settings.Xmax)
            {
                // Not enough data yet - wait for more
                _lastCheckedTotalSamples = currentTotalSamples;
                return false;
            }

            // Define the search window for the trigger
            int searchStart = Math.Max(1, preTriggerSamples);
            int searchEnd = Math.Min(samplesCopied - 1, samplesCopied - postTriggerSamples);

            // Only scan NEW samples (based on how many arrived since last check)
            // 
            // TIMING CONSTRAINT NOTE:
            // newSampleCount = samples arrived since last frame = SampleRate / FPS
            // 
            // The search window is bounded by:
            //   - searchStart = max(1, preTriggerSamples) where trigger can validly occur
            //   - searchEnd = samplesCopied - postTriggerSamples must have room for post-trigger data
            //   - newSamplesStart = samplesCopied - newSampleCount only scan new data
            // 
            // TRIGGER POSITION MATTERS:
            // When trigger is at LEFT (small preTriggerSamples, large postTriggerSamples):
            //   - searchEnd is constrained far from the buffer end
            //   - New samples (at buffer end) often fall OUTSIDE the valid search range
            //   - Result: triggers frequently missed
            // 
            // When trigger is at RIGHT (large preTriggerSamples, small postTriggerSamples):
            //   - searchEnd extends close to the buffer end
            //   - New samples fall INSIDE the valid search range
            //   - Result: triggers detected reliably
            // 
            // Example: Xmax=10000, trigger at 10% vs 90%
            //   - At 10%: postTriggerSamples=9000, searchEnd=samplesCopied-9000 (very early)
            //   - At 90%: postTriggerSamples=1000, searchEnd=samplesCopied-1000 (near end)
            // 
            // RECOMMENDATION: Place trigger at 70-95% of Xmax for reliable detection.
            // 
            // See class-level documentation for detailed analysis and recommendations.
            int newSamplesStart = Math.Max(searchStart, samplesCopied - (int)newSampleCount);

            if (shouldLogDebug)
            {
                System.Diagnostics.Debug.WriteLine($"Trigger search: buffer has {samplesCopied} samples, searching [{newSamplesStart}..{searchEnd}], triggerPos={_triggerPosition}");
            }

            // Search for trigger condition
            bool triggerDetected = false;
            int triggerBufferIndex = -1;

            for (int i = newSamplesStart; i < searchEnd; i++)
            {
                double previousSample = _triggerWorkBuffer[i - 1];
                double currentSample = _triggerWorkBuffer[i];

                // Check for edge based on TriggerEdge setting
                bool edgeDetected;
                if (Settings.TriggerEdge == TriggerEdgeType.Rising)
                    edgeDetected = previousSample < _triggerLevel && currentSample >= _triggerLevel;
                else
                    edgeDetected = previousSample >= _triggerLevel && currentSample < _triggerLevel;

                if (_triggerArmed && edgeDetected)
                {
                    // Verify we have enough samples before and after this trigger point
                    int samplesBeforeTrigger = i;
                    int samplesAfterTrigger = samplesCopied - i - 1;

                    if (samplesBeforeTrigger >= preTriggerSamples && samplesAfterTrigger >= postTriggerSamples)
                    {
                        triggerDetected = true;
                        triggerBufferIndex = i;
                        _triggerArmed = false;
                        _lastTriggerTime = DateTime.Now;

                        if (Settings.SingleShotMode)
                        {
                            // Auto-stop the update timer when trigger fires in single-shot mode
                            // This makes IsRunning the single source of truth for system state
                            StopUpdates();
                        }

                        // Always log trigger detection (important event)
                        string edgeType = Settings.TriggerEdge == TriggerEdgeType.Rising ? "Rising" : "Falling";
                        System.Diagnostics.Debug.WriteLine($"TRIGGER DETECTED at buffer[{i}], value={currentSample:F3}, level={_triggerLevel:F3}, edge={edgeType}");
                        break;
                    }
                }

                // Re-arm trigger when signal crosses back
                bool shouldRearm;
                if (Settings.TriggerEdge == TriggerEdgeType.Rising)
                    shouldRearm = currentSample < _triggerLevel;
                else
                    shouldRearm = currentSample >= _triggerLevel;

                if (!_triggerArmed && shouldRearm)
                    _triggerArmed = true;
            }

            // Store trigger index for CopyChannelDataToPlot to use
            if (triggerDetected)
                _triggerSampleIndex = triggerBufferIndex;

            // Update tracking
            _lastCheckedTotalSamples = currentTotalSamples;

            // Track the channel where the trigger was detected, for color updates
            if (triggerDetected)
                _lastTriggerChannel = triggerChannel;

            return triggerDetected;
        }

        /// <summary>
        /// Copies channel data aligned to trigger point using over-fetch and shift approach.
        /// Fetches data, then copies the window that places trigger at _triggerPosition.
        /// Works with ALL stream types using only CopyLatestTo().
        /// </summary>
        private void CopyChannelDataWithTriggerAlignment()
        {
            if (_channels == null || _triggerSampleIndex < 0)
                return;

            EnsureTriggerWorkBufferSize();

            int preTriggerSamples = _triggerPosition;
            int postTriggerSamples = Settings.Xmax - _triggerPosition;
            int fetchCount = Settings.Xmax + Math.Max(preTriggerSamples, postTriggerSamples);
            fetchCount = Math.Min(fetchCount, _triggerWorkBufferSize);

            bool shouldLogDebug = (_triggerDebugCounter % TriggerDebugInterval == 0);

            for (int i = 0; i < _channels.Count && i < _maxChannels; i++)
            {
                Channel channel = _channels[i];

                if (!channel.IsEnabled)
                {
                    // Clear data for disabled channels
                    if (_data[i] != null)
                        Array.Clear(_data[i], 0, _data[i].Length);
                    continue;
                }

                // Fetch fresh data for each channel
                int samplesCopied = channel.CopyLatestDataTo(_triggerWorkBuffer, fetchCount);

                if (samplesCopied < Settings.Xmax)
                {
                    // Not enough data - fill with zeros
                    Array.Clear(_data[i], 0, Settings.Xmax);
                    continue;
                }

                // Calculate the start index in the working buffer for the display window
                // This places the trigger at _triggerPosition on the display
                int displayStart = _triggerSampleIndex - preTriggerSamples;
                
                // Clamp to valid range
                displayStart = Math.Max(0, displayStart);
                displayStart = Math.Min(displayStart, samplesCopied - Settings.Xmax);

                // Copy the trigger-aligned window to plot data
                Array.Copy(_triggerWorkBuffer, displayStart, _data[i], 0, Settings.Xmax);

                if (shouldLogDebug)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Channel {i}: copied window [{displayStart}..{displayStart + Settings.Xmax}] from {samplesCopied} samples, " +
                        $"trigger at buffer[{_triggerSampleIndex}] displayed at [{preTriggerSamples}]");
                }
            }
        }

        /// <summary>
        /// Handles Xmax changes for trigger position clamping
        /// Called from OnSettingsChanged in main partial class
        /// </summary>
        private void HandleXmaxChangeForTrigger()
        {
            // Clamp trigger position to new Xmax range (5%-95% to keep trigger visible)
            if (_triggerPositionLine != null)
            {
                int oldTriggerPosition = _triggerPosition;
                int minPosition = (int)(0.05 * Settings.Xmax);
                int maxPosition = (int)(0.95 * Settings.Xmax);
                _triggerPosition = (int)Math.Clamp(_triggerPosition, minPosition, maxPosition);

                if (_triggerPosition != oldTriggerPosition)
                {
                    _triggerPositionLine.X = _triggerPosition;
                    _triggerPositionLine.Text = $"Trig Pos: {_triggerPosition}";
                    
                }
            }

            // Invalidate working buffer so it gets resized on next use
            _triggerWorkBufferSize = 0;

            // Reset trigger sample index - it's no longer valid for the new Xmax
            _triggerSampleIndex = -1;
        }

        /// <summary>
        /// Handles EnableEdgeTrigger changes
        /// Called from OnSettingsChanged in main partial class
        /// </summary>
        private void HandleTriggerModeChange()
        {
            // Reset trigger state when mode changes
            _triggerArmed = true;
            _lastCheckedTotalSamples = 0;
            _triggerSampleIndex = -1;
            
            // Update trigger line color when mode changes (in case channels changed while disabled)
            UpdateTriggerLineColor();
            
            System.Diagnostics.Debug.WriteLine($"Edge trigger mode changed to: {Settings.EnableEdgeTrigger}");
        }

        #endregion
    }
}
