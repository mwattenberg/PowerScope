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
    /// 3. Calculate display window so trigger appears at TriggerPosition
    /// 4. Copy aligned window to plot data arrays
    /// This works with ALL stream types using only CopyLatestTo()
    /// 
    /// Edge ID Tracking
    /// ================
    /// To allow placing the trigger at ANY position (including left side for post-trigger viewing),
    /// we use "Edge ID Tracking" to uniquely identify each edge crossing.
    /// 
    /// THE PROBLEM: Rolling Trigger
    /// ----------------------------
    /// If we scan the entire buffer every frame without tracking, the same edge crossing may 
    /// appear in multiple consecutive frames (until it scrolls out of the buffer). This would
    /// cause re-triggering on the same edge every frame, creating a "rolling" display effect.
    /// 
    /// THE SOLUTION: Absolute Edge Index
    /// ----------------------------------
    /// Each edge crossing has a unique identity based on its absolute position in the stream:
    ///   absoluteEdgeIndex = TotalSamples - (bufferSamplesCopied - localBufferIndex)
    /// 
    /// We track the last triggered edge's absolute index. When searching for triggers:
    ///   - We scan the ENTIRE valid window (not just new samples)
    ///   - We only trigger on edges with absoluteIndex > lastTriggeredIndex
    ///   - This naturally filters out edges we've already triggered on
    /// 
    /// BENEFITS:
    ///   - Trigger position can be placed ANYWHERE (left, center, right)
    ///   - Post-trigger viewing works reliably (trigger at left shows what happens after)
    ///   - No "rolling trigger" problem
    ///   - Simpler than the old "only scan new samples + armed state" approach
    /// </summary>
    public partial class PlotManager
    {
        // Trigger state tracking fields
        private long _lastCheckedTotalSamples = 0;
        private int _triggerSampleIndex = -1;
        private double _triggerHoldoffSeconds = 0.0;
        private DateTime _lastTriggerTime = DateTime.MinValue;

        // Edge ID tracking to prevent re-triggering on the same edge
        // Each edge crossing has a unique absolute sample index in the stream's lifetime.
        // By tracking which edge we last triggered on, we can scan the entire buffer
        // without causing a "rolling trigger" effect.
        private long _lastTriggeredEdgeAbsoluteIndex = -1;

        // Working buffer for over-fetching (allows shifting for trigger alignment)
        private double[] _triggerWorkBuffer;
        private int _triggerWorkBufferSize = 0;

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
        /// Hold off is currently not implemented yet.
        /// Depends on the practical use case requirements.
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
                    Settings.SingleShotMode = value;
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

            // Reset trigger tracking when showing trigger line
            _lastCheckedTotalSamples = 0;
            _lastTriggerTime = DateTime.MinValue;
            _lastTriggeredEdgeAbsoluteIndex = -1;

            // Create trigger level line with label
            // Uses Settings.TriggerLevel as-is (loaded from settings or default 0.0)
            _triggerLevelLine = _plot.Plot.Add.HorizontalLine(Settings.TriggerLevel);
            _triggerLevelLine.IsDraggable = true;
            _triggerLevelLine.LineWidth = 1;
            _triggerLevelLine.Color = ScottPlot.Color.FromHex("#808080");
            _triggerLevelLine.LinePattern = ScottPlot.LinePattern.Dashed;
            _triggerLevelLine.Text = $"Trigger: {Settings.TriggerLevel:F1}";

            // Create trigger position line without label
            _triggerPositionLine = _plot.Plot.Add.VerticalLine(Settings.TriggerPosition);
            _triggerPositionLine.IsDraggable = true;
            _triggerPositionLine.LineWidth = 1;
            _triggerPositionLine.Color = ScottPlot.Color.FromHex("#FFA500");
            _triggerPositionLine.LinePattern = ScottPlot.LinePattern.Dashed;
            _triggerPositionLine.Text = string.Empty;

            // Setup mouse handling if needed
            if (!_cursorMouseHandlingEnabled)
                SetupCursorMouseHandling();

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

            UnsubscribeFromTriggerChannelChanges();

            if (!HasActiveCursors && _cursorMouseHandlingEnabled)
                RemoveCursorMouseHandling();

            _plot.Refresh();
        }

        #endregion

        #region Trigger Internal Methods (used by Cursors partial)

        /// <summary>
        /// Checks if the given line is a trigger line
        /// </summary>
        internal bool IsTriggerLine(AxisLine line)
        {
            return line == _triggerLevelLine || line == _triggerPositionLine;
        }

        /// <summary>
        /// Handles dragging of the trigger level line
        /// </summary>
        internal void HandleTriggerLevelDrag(HorizontalLine line)
        {
            Settings.TriggerLevel = line.Y;
            _triggerLevelLine.Text = $"Trigger: {Settings.TriggerLevel:F1}";
        }

        /// <summary>
        /// Handles dragging of the trigger position line
        /// Clamps position to 5%-95% of Xmax to keep trigger visible
        /// </summary>
        internal void HandleTriggerPositionDrag(VerticalLine line)
        {
            int minPosition = (int)(0.05 * Settings.Xmax);
            int maxPosition = (int)(0.95 * Settings.Xmax);
            Settings.TriggerPosition = (int)Math.Clamp(line.X, minPosition, maxPosition);
            line.X = Settings.TriggerPosition;
        }

        #endregion

        #region Trigger Private Methods

        private void SubscribeToTriggerChannelChanges()
        {
            if (Settings != null)
                Settings.PropertyChanged += OnTriggerSettingsChanged;
        }

        private void UnsubscribeFromTriggerChannelChanges()
        {
            if (Settings != null)
                Settings.PropertyChanged -= OnTriggerSettingsChanged;
        }

        private void OnTriggerSettingsChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PlotSettings.TriggerSourceChannel))
                UpdateTriggerLineColor();
        }

        /// <summary>
        /// Updates the color of trigger lines to match the selected trigger channel.
        /// Falls back to gray if no channels are available.
        /// </summary>
        private void UpdateTriggerLineColor()
        {
            if (_triggerLevelLine == null || _triggerPositionLine == null)
                return;

            Channel triggerChannel = GetCurrentTriggerChannel();

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
                newColor = ScottPlot.Color.FromHex("#808080");
            }

            _triggerLevelLine.Color = newColor;
            _triggerPositionLine.Color = newColor;
            _lastTriggerChannel = triggerChannel;

            _plot.Refresh();
        }

        /// <summary>
        /// Gets the current trigger channel.
        /// Returns the explicitly configured trigger channel, or the first enabled channel if none is configured.
        /// </summary>
        private Channel GetCurrentTriggerChannel()
        {
            Channel triggerChannel = Settings.TriggerSourceChannel;

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
        /// </summary>
        private void EnsureTriggerWorkBufferSize()
        {
            int margin = Settings.Xmax / 2;
            int requiredSize = Settings.Xmax + margin;

            if (_triggerWorkBuffer == null || _triggerWorkBufferSize < requiredSize)
            {
                _triggerWorkBuffer = new double[requiredSize];
                _triggerWorkBufferSize = requiredSize;
            }
        }

        /// <summary>
        /// Checks if trigger condition is met for edge trigger mode.
        /// Uses over-fetch approach: fetches extra samples, finds trigger, stores index for alignment.
        /// 
        /// EXPERIMENTAL: Edge ID Tracking
        /// We scan the ENTIRE valid search window and use absolute sample indices to track
        /// which edges we've already triggered on. This prevents the "rolling trigger" problem
        /// while allowing the trigger to be placed anywhere.
        /// </summary>
        private bool CheckTriggerCondition()
        {
            Channel triggerChannel = Settings.TriggerSourceChannel;

            if (triggerChannel == null || !triggerChannel.IsEnabled)
                return false;

            if (_triggerHoldoffSeconds > 0.0)
            {
                TimeSpan timeSinceLastTrigger = DateTime.Now - _lastTriggerTime;
                if (timeSinceLastTrigger.TotalSeconds < _triggerHoldoffSeconds)
                    return false;
            }

            long currentTotalSamples = triggerChannel.OwnerStream.TotalSamples;
            long newSampleCount = currentTotalSamples - _lastCheckedTotalSamples;

            if (newSampleCount <= 0)
                return false;

            if (_lastCheckedTotalSamples == 0)
            {
                _lastCheckedTotalSamples = currentTotalSamples;
                return false;
            }

            EnsureTriggerWorkBufferSize();

            int preTriggerSamples = Settings.TriggerPosition;
            int postTriggerSamples = Settings.Xmax - Settings.TriggerPosition;
            int fetchCount = Settings.Xmax + Math.Max(preTriggerSamples, postTriggerSamples);
            fetchCount = Math.Min(fetchCount, _triggerWorkBufferSize);

            int samplesCopied = triggerChannel.CopyLatestDataTo(_triggerWorkBuffer, fetchCount);

            if (samplesCopied < Settings.Xmax)
            {
                _lastCheckedTotalSamples = currentTotalSamples;
                return false;
            }

            // Search the ENTIRE valid window (edge tracking prevents rolling trigger)
            int searchStart = Math.Max(1, preTriggerSamples);
            int searchEnd = Math.Min(samplesCopied - 1, samplesCopied - postTriggerSamples);

            bool triggerDetected = false;
            int triggerBufferIndex = -1;

            for (int i = searchStart; i < searchEnd; i++)
            {
                double previousSample = _triggerWorkBuffer[i - 1];
                double currentSample = _triggerWorkBuffer[i];

                bool edgeDetected;
                if (Settings.TriggerEdge == TriggerEdgeType.Rising)
                    edgeDetected = previousSample < Settings.TriggerLevel && currentSample >= Settings.TriggerLevel;
                else
                    edgeDetected = previousSample >= Settings.TriggerLevel && currentSample < Settings.TriggerLevel;

                if (edgeDetected)
                {
                    // Calculate absolute sample index for this edge
                    // This uniquely identifies the edge across the stream's lifetime
                    long absoluteEdgeIndex = currentTotalSamples - (samplesCopied - i);

                    // Only trigger if this is a NEW edge (prevents rolling trigger)
                    if (absoluteEdgeIndex > _lastTriggeredEdgeAbsoluteIndex)
                    {
                        int samplesBeforeTrigger = i;
                        int samplesAfterTrigger = samplesCopied - i - 1;

                        if (samplesBeforeTrigger >= preTriggerSamples && samplesAfterTrigger >= postTriggerSamples)
                        {
                            triggerDetected = true;
                            triggerBufferIndex = i;
                            _lastTriggeredEdgeAbsoluteIndex = absoluteEdgeIndex;
                            _lastTriggerTime = DateTime.Now;

                            if (Settings.SingleShotMode)
                                StopUpdates();

                            break;
                        }
                    }
                }
            }

            if (triggerDetected)
                _triggerSampleIndex = triggerBufferIndex;

            _lastCheckedTotalSamples = currentTotalSamples;

            if (triggerDetected)
                _lastTriggerChannel = triggerChannel;

            return triggerDetected;
        }

        /// <summary>
        /// Copies channel data aligned to trigger point using over-fetch and shift approach.
        /// </summary>
        private void CopyChannelDataWithTriggerAlignment()
        {
            if (_channels == null || _triggerSampleIndex < 0)
                return;

            EnsureTriggerWorkBufferSize();

            int preTriggerSamples = Settings.TriggerPosition;
            int postTriggerSamples = Settings.Xmax - Settings.TriggerPosition;
            int fetchCount = Settings.Xmax + Math.Max(preTriggerSamples, postTriggerSamples);
            fetchCount = Math.Min(fetchCount, _triggerWorkBufferSize);

            for (int i = 0; i < _channels.Count && i < _maxChannels; i++)
            {
                Channel channel = _channels[i];

                if (!channel.IsEnabled)
                {
                    if (_data[i] != null)
                        Array.Clear(_data[i], 0, _data[i].Length);
                    continue;
                }

                int samplesCopied = channel.CopyLatestDataTo(_triggerWorkBuffer, fetchCount);

                if (samplesCopied < Settings.Xmax)
                {
                    Array.Clear(_data[i], 0, Settings.Xmax);
                    continue;
                }

                int displayStart = _triggerSampleIndex - preTriggerSamples;
                displayStart = Math.Max(0, displayStart);
                displayStart = Math.Min(displayStart, samplesCopied - Settings.Xmax);

                Array.Copy(_triggerWorkBuffer, displayStart, _data[i], 0, Settings.Xmax);
            }
        }

        /// <summary>
        /// Handles Xmax changes for trigger position clamping
        /// </summary>
        private void HandleXmaxChangeForTrigger()
        {
            if (_triggerPositionLine != null)
            {
                int oldTriggerPosition = Settings.TriggerPosition;
                int minPosition = (int)(0.05 * Settings.Xmax);
                int maxPosition = (int)(0.95 * Settings.Xmax);
                Settings.TriggerPosition = (int)Math.Clamp(Settings.TriggerPosition, minPosition, maxPosition);

                if (Settings.TriggerPosition != oldTriggerPosition)
                    _triggerPositionLine.X = Settings.TriggerPosition;
            }

            // Invalidate working buffer so it gets resized on next use
            _triggerWorkBufferSize = 0;
            _triggerSampleIndex = -1;
        }

        /// <summary>
        /// Handles EnableEdgeTrigger changes
        /// </summary>
        private void HandleTriggerModeChange()
        {
            _lastCheckedTotalSamples = 0;
            _triggerSampleIndex = -1;
            _lastTriggeredEdgeAbsoluteIndex = -1;
            
            UpdateTriggerLineColor();
        }

        #endregion
    }
}
