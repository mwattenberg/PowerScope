namespace PowerScope.Model.Mcp
{
    /// <summary>
    /// Snapshot of a plot axis range, safe to read from any thread.
    /// </summary>
    public readonly struct AxisRangeSnapshot
    {
        public double Min { get; init; }
        public double Max { get; init; }
        /// <summary>Only meaningful for the Y axis; always false for X.</summary>
        public bool AutoScale { get; init; }
    }

    /// <summary>
    /// Snapshot of the trigger configuration, safe to read from any thread.
    /// </summary>
    public readonly struct TriggerSnapshot
    {
        public bool Enabled { get; init; }
        /// <summary>"free_run", "normal", or "single"</summary>
        public string Mode { get; init; }
        /// <summary>"Rising" or "Falling"</summary>
        public string Edge { get; init; }
        public double Level { get; init; }
        /// <summary>X position (sample index) where the trigger point appears on the display.</summary>
        public int Position { get; init; }
        /// <summary>Label of the source channel, or null when auto (first enabled channel).</summary>
        public string SourceChannelLabel { get; init; }
        /// <summary>Global channel index of the source channel, or null when auto.</summary>
        public int? SourceChannelIndex { get; init; }
    }

    /// <summary>
    /// Abstraction between the MCP tool layer and the running application.
    /// MainWindow implements this by marshalling onto the WPF dispatcher;
    /// tests implement it with plain in-memory collections (no UI required).
    /// All members must be safe to call from non-UI threads.
    /// </summary>
    public interface IMcpHost
    {
        /// <summary>
        /// Returns a snapshot of all channels currently known to the application.
        /// The returned list must not be mutated by the host afterwards.
        /// </summary>
        IReadOnlyList<Channel> GetChannels();

        /// <summary>
        /// Returns a snapshot of the current trigger configuration.
        /// </summary>
        TriggerSnapshot GetTriggerInfo();

        /// <summary>
        /// Returns the current live X axis (horizontal) viewport range.
        /// This is the ScottPlot axis state and may differ from PlotSettings.Xmax
        /// if the user has zoomed or panned.
        /// </summary>
        AxisRangeSnapshot GetXRange();

        /// <summary>
        /// Sets the X axis viewport range transiently. Does not change PlotSettings.
        /// Equivalent to the user zooming/panning with the mouse.
        /// </summary>
        void SetXRange(double min, double max);

        /// <summary>
        /// Returns the current live Y axis (vertical) viewport range plus whether
        /// YAutoScale is currently active.
        /// </summary>
        AxisRangeSnapshot GetYRange();

        /// <summary>
        /// Sets the Y axis viewport range transiently and disables YAutoScale so the
        /// next render does not override it. Pass autoScale=true to re-enable
        /// auto-scaling instead (y_min/y_max are ignored in that case).
        /// </summary>
        void SetYRange(double min, double max, bool autoScale);

        /// <summary>
        /// Configures the trigger. Any null argument is left unchanged.
        /// channelSpecified=true with channel=null means "set source to auto (first enabled channel)".
        /// channelSpecified=false means "leave source channel unchanged".
        /// </summary>
        void SetTrigger(bool? enableEdgeTrigger, bool? singleShot, double? level,
                        int? position, TriggerEdgeType? edge, bool channelSpecified, Channel channel);

        /// <summary>
        /// Sets any combination of label, enabled, gain, offset on a channel. Pass null to leave a value unchanged.
        /// </summary>
        void SetChannelProperties(Channel channel, string label, bool? enabled, double? gain, double? offset);

        /// <summary>
        /// Creates, connects and starts a demo data stream (synthetic waveforms).
        /// Returns the number of channels created.
        /// </summary>
        int AddDemoStream(int numberOfChannels, int sampleRate, string signalType);

        /// <summary>
        /// Loads a full session configuration (streams, channels, plot settings)
        /// from a PowerScope XML settings file. Streams are connected and started.
        /// Returns descriptions of any streams that could not be restored (e.g. a
        /// saved COM port no longer exists on this system); empty if all succeeded.
        /// </summary>
        IReadOnlyList<string> LoadConfiguration(string filePath);

        /// <summary>
        /// Stops, disconnects and removes all active streams and their channels.
        /// </summary>
        void RemoveAllStreams();

        /// <summary>
        /// Stops, disconnects and removes a single stream and its channels.
        /// </summary>
        void RemoveStream(IDataStream stream);

        /// <summary>
        /// Renders the current plot to a file and returns the absolute path written.
        /// Supported formats: ".png" and ".svg" (determined by filePath extension).
        /// Pass null to have the implementation choose a temp path.
        /// </summary>
        string ExportPlot(string filePath, int width, int height);
    }
}
