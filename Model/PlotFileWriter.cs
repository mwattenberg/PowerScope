using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Collections.Generic;

namespace PowerScope.Model
{
    /// <summary>
    /// Responsible for writing plot data to a CSV file.
    /// Simplified implementation using FileIOManager for consistent file format.
    /// Thread-safe for public API via simple locking where needed.
    /// </summary>
    public class PlotFileWriter : IDisposable
    {
        private readonly object _sync = new object();
        private StreamWriter _writer;
        private long _lastRecordedSampleCount;

        /// <summary>
        /// Whether recording is active.
        /// Public read, private set.
        /// </summary>
        public bool IsRecording { get; private set; }

        /// <summary>
        /// Current recording file path.
        /// Public read, private set.
        /// </summary>
        public string RecordingFilePath { get; private set; } = string.Empty;

        /// <summary>
        /// Number of samples written so far.
        /// Public read, private set.
        /// </summary>
        public long RecordingSampleCount { get; private set; }

        /// <summary>
        /// Channels collection to write from. Caller must provide and manage this.
        /// Initialized to an empty collection; caller may replace it.
        /// </summary>
        public ObservableCollection<Channel> Channels { get; set; }

        /// <summary>
        /// Sample rate in Hz. Caller should update this when needed.
        /// </summary>
        public double SampleRate { get; set; }

        public PlotFileWriter()
        {
            // Initialize with an empty collection; caller can set Channels property later.
            Channels = new ObservableCollection<Channel>();
            _writer = null;
            IsRecording = false;
            _lastRecordedSampleCount = 0;
            RecordingSampleCount = 0;
            SampleRate = 0.0;
        }

        /// <summary>
        /// Starts recording. Returns false if already recording. IO errors will surface from StreamWriter.
        /// </summary>
        public bool StartRecording(string filePath)
        {
            lock (_sync)
            {
                if (IsRecording)
                {
                    return false;
                }

                RecordingFilePath = filePath;
                _writer = new StreamWriter(RecordingFilePath);
                
                // Write header using centralized FileIOManager
                FileIOManager.WriteFileHeader(_writer, Channels, SampleRate);
                
                _writer.Flush();
                RecordingSampleCount = 0;
                _lastRecordedSampleCount = 0;
                IsRecording = true;
                return true;
            }
        }

        /// <summary>
        /// Stops recording and releases the file. IO exceptions may surface.
        /// </summary>
        public void StopRecording()
        {
            lock (_sync)
            {
                StopRecordingNoLock();
            }
        }

        private void StopRecordingNoLock()
        {
            if (!IsRecording)
                return;

            // Let IO exceptions surface
            _writer.Flush();
            _writer.Close();
            _writer.Dispose();

            _writer = null;
            IsRecording = false;
            RecordingFilePath = string.Empty;
            _lastRecordedSampleCount = 0;
        }

        /// <summary>
        /// Called periodically to write any newly available samples.
        /// Errors are allowed to surface to the caller.
        /// </summary>
        public void WritePendingSamples()
        {
            lock (_sync)
            {
                if (!IsRecording || _writer == null)
                {
                    return;
                }

                // Rely on caller to ensure Channels is valid
                if (Channels.Count == 0)
                    return;

                Channel firstChannel = Channels[0];
                IDataStream firstStream = firstChannel.OwnerStream;

                long currentSampleCount = firstStream.TotalSamples;
                long newSamplesCount = currentSampleCount - _lastRecordedSampleCount;

                if (newSamplesCount <= 0)
                    return;

                int channelCount = Channels.Count;
                int newSamplesInt = (int)newSamplesCount;

                // allocate buffers for each channel
                double[][] channelBuffers = new double[channelCount][];
                for (int cIndex = 0; cIndex < channelCount; cIndex++)
                {
                    channelBuffers[cIndex] = new double[newSamplesInt];
                }

                // copy data for each channel
                for (int cIndex = 0; cIndex < channelCount; cIndex++)
                {
                    Channel ch = Channels[cIndex];
                    ch.CopyLatestDataTo(channelBuffers[cIndex], newSamplesInt);
                }

                // Write data using FileIOManager for consistent formatting
                FileIOManager.AppendDataRows(_writer, channelBuffers);
                RecordingSampleCount += newSamplesInt;

                _lastRecordedSampleCount = currentSampleCount;
                _writer.Flush();
            }
        }

        public void Dispose()
        {
            StopRecording();
        }
    }
}
