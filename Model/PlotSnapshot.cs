using System;
using System.Collections.ObjectModel;

namespace PowerScope.Model
{
    /// <summary>
    /// A frozen snapshot of the data currently visible on the plot.
    /// Created by PlotManager and consumed by PlotFileWriter for export.
    /// Carries no behaviour — data only.
    /// </summary>
    public class PlotSnapshot
    {
        /// <summary>
        /// Copied display arrays, one per channel, in the same order as Channels.
        /// </summary>
        public double[][] Data { get; }

        /// <summary>
        /// Channel metadata (labels, stream info) at the time of capture.
        /// </summary>
        public ObservableCollection<Channel> Channels { get; }

        /// <summary>
        /// Sample rate in Hz at the time of capture.
        /// </summary>
        public double SampleRate { get; }

        /// <summary>
        /// UTC time at which the snapshot was taken.
        /// </summary>
        public DateTime CapturedAt { get; }

        public PlotSnapshot(double[][] data, ObservableCollection<Channel> channels, double sampleRate, DateTime capturedAt)
        {
            Data = data;
            Channels = channels;
            SampleRate = sampleRate;
            CapturedAt = capturedAt;
        }
    }
}
