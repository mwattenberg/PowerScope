using System;
using System.Collections.Generic;

namespace PowerScope.Model.Mcp
{
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
        /// Creates, connects and starts a demo data stream (synthetic waveforms).
        /// Returns the number of channels created.
        /// </summary>
        int AddDemoStream(int numberOfChannels, int sampleRate, string signalType);

        /// <summary>
        /// Loads a full session configuration (streams, channels, plot settings)
        /// from a PowerScope XML settings file. Streams are connected and started.
        /// </summary>
        void LoadConfiguration(string filePath);

        /// <summary>
        /// Stops, disconnects and removes all active streams and their channels.
        /// </summary>
        void RemoveAllStreams();
    }
}
