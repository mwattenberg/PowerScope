using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PowerScope.Model
{
    public interface IDataStream : IDisposable, INotifyPropertyChanged
    {
        //Status message to be displayed in the UI, e.g. "Connected", "Disconnected", "Error: Port not found", etc.
        string StatusMessage { get;}
        //Indicates the type of data stream, e.g. "Serial", "USB", "Audio", etc.
        string StreamType { get;}
        //True when connected to the data source
        bool IsConnected { get;}
        //True when actively sampling from the data source
        bool IsStreaming { get;}
        //Total number of samples acquired since the stream started
        //This value increases as new samples are added and is used for:
        // - Calculating sampling rate
        // - Recording new data tracking (detecting new samples available)
        long TotalSamples { get;}
        //Total number of bits acquired since the stream started
        //Useful for calculating bus bandwidth
        long TotalBits { get;}
        //Number of channels in the data stream
        int ChannelCount { get; }
        //Sample rate of the data stream in samples per second (Hz)
        //For streams with known fixed rates, returns the configured rate
        //For streams with variable rates, returns the current measured rate
        double SampleRate { get; }
        //Connect to the data source, i.e. open serial port, USB , audio, etc.
        void Connect();
        //disconnect from the data source, i.e. close serial port, USB , audio, etc.
        void Disconnect();
        //Starts sampling from the data source
        void StartStreaming();
        //Stops sampling from the data source
        void StopStreaming();
        //Returns the latest n samples from the specified channel
        int CopyLatestTo(int channel, double[] destination, int n);
        void clearData();
    }

    /// <summary>
    /// Interface for data streams that support per-channel configuration
    /// </summary>
    public interface IChannelConfigurable
    {
        /// <summary>
        /// Set channel-specific settings for processing (gain, offset, filtering)
        /// </summary>
        /// <param name="channelIndex">Index of the channel (0-based)</param>
        /// <param name="settings">Channel settings to apply</param>
        void SetChannelSetting(int channelIndex, ChannelSettings settings);
        
        /// <summary>
        /// Update all channel settings at once
        /// </summary>
        /// <param name="channelSettings">Array or collection of channel settings</param>
        void UpdateChannelSettings(IReadOnlyList<ChannelSettings> channelSettings);
        
        /// <summary>
        /// Reset all filters to their initial state
        /// </summary>
        void ResetChannelFilters();
    }

    /// <summary>
    /// Interface for data streams that support runtime buffer size changes
    /// </summary>
    public interface IBufferResizable
    {
        /// <summary>
        /// Gets or sets the buffer size (capacity of ring buffers)
        /// Setting this will clear existing data and recreate ring buffers
        /// </summary>
        int BufferSize { get; set; }
    }
}
