using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Media;
using System.Linq;
using PowerScope.Model;
using PowerScope.View.UserForms;

namespace PowerScope.View.UserControls
{
    /// <summary>
    /// Interaction logic for DataStreamBar.xaml
    /// Simplified to focus on channel management - streams are accessed through channels
    /// ObservableCollection.CollectionChanged provides automatic change notifications
    /// </summary>
    public partial class DataStreamBar : UserControl, INotifyPropertyChanged, IDisposable
    {
        // Core channel management - channels contain stream references
        public ObservableCollection<Channel> Channels { get; private set; } = new ObservableCollection<Channel>();

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Gets the total number of channels across all streams
        /// </summary>
        public int TotalChannelCount 
        { 
            get { return Channels.Count; } 
        }

        /// <summary>
        /// Gets all unique data streams from channels
        /// </summary>
        public List<IDataStream> ConnectedDataStreams
        {
            get
            {
                List<IDataStream> uniqueStreams = new List<IDataStream>();
                foreach (Channel channel in Channels)
                {
                    if (!uniqueStreams.Contains(channel.OwnerStream))
                    {
                        uniqueStreams.Add(channel.OwnerStream);
                    }
                }
                return uniqueStreams;
            }
        }

        public DataStreamBar()
        {
            InitializeComponent();
        }

        private void Button_AddStream_Click(object sender, RoutedEventArgs e)
        {
            StreamSettings settings = new StreamSettings();
            SerialConfigWindow configWindow = new SerialConfigWindow(settings);
            
            if (configWindow.ShowDialog() == true)
            {
                // Use the existing UpdateFromWindow method to ensure all properties are properly transferred
                settings.UpdateFromWindow(configWindow);
                
                // Create and connect IDataStream from config
                IDataStream dataStream = CreateDataStreamFromUserInput(settings);
                dataStream.Connect();
                dataStream.StartStreaming();

                // Add channels for the stream - this automatically manages the stream
                AddChannelsForStream(dataStream);

                // Add UI panel for the stream
                AddStreamInfoPanel(settings, dataStream);

                // ObservableCollection.CollectionChanged handles all notifications automatically
            }
        }

        /// <summary>
        /// Creates channels for a data stream and adds them to the collection
        /// </summary>
        /// <param name="dataStream">The data stream to create channels for</param>
        /// <param name="channelColors">Optional array of colors for the channels</param>
        public void AddChannelsForStream(IDataStream dataStream, Color[] channelColors = null)
        {
            if (dataStream == null)
                return;

            // Create channels for this stream
            for (int localIndex = 0; localIndex < dataStream.ChannelCount; localIndex++)
            {
                int globalIndex = Channels.Count;
                
                // Create channel settings
                ChannelSettings channelSettings = new ChannelSettings();
                channelSettings.Label = $"CH{globalIndex + 1}";
                channelSettings.Gain = 1.0;
                channelSettings.Offset = 0.0;
                channelSettings.IsEnabled = true;

                // Set color if provided, otherwise use default palette
                if (channelColors != null && globalIndex < channelColors.Length)
                {
                    channelSettings.Color = channelColors[globalIndex];
                }
                else
                {
                    channelSettings.Color = PlotManager.GetColor(globalIndex);
                }

                // Create the channel
                Channel channel = new Channel(dataStream, localIndex, channelSettings);
                Channels.Add(channel);
            }

            OnPropertyChanged(nameof(TotalChannelCount));
        }

        /// <summary>
        /// Removes channels associated with a specific data stream
        /// </summary>
        /// <param name="dataStream">The data stream whose channels should be removed</param>
        private void RemoveChannelsForStream(IDataStream dataStream)
        {
            if (dataStream == null)
                return;

            // Find and remove all channels belonging to this stream
            List<Channel> channelsToRemove = new List<Channel>();
            foreach (Channel channel in Channels)
            {
                if (channel.OwnerStream == dataStream)
                {
                    channelsToRemove.Add(channel);
                }
            }

            foreach (Channel channel in channelsToRemove)
            {
                Channels.Remove(channel);
                channel.Dispose();
            }

            // Clean up the stream
            dataStream.StopStreaming();
            dataStream.Disconnect();
            dataStream.Dispose();

            // Update channel indices after removal
            UpdateChannelLabels();
            OnPropertyChanged(nameof(TotalChannelCount));
        }

        /// <summary>
        /// Gets a channel by its global index
        /// </summary>
        /// <param name="globalIndex">Global channel index</param>
        /// <returns>The channel at the specified index, or null if not found</returns>
        public Channel GetChannelByIndex(int globalIndex)
        {
            if (globalIndex < 0 || globalIndex >= Channels.Count)
                return null;

            return Channels[globalIndex];
        }

        /// <summary>
        /// Gets all channels belonging to a specific data stream
        /// </summary>
        /// <param name="dataStream">The data stream</param>
        /// <returns>Collection of channels belonging to the stream</returns>
        public IEnumerable<Channel> GetChannelsForStream(IDataStream dataStream)
        {
            if (dataStream == null)
                return Enumerable.Empty<Channel>();

            List<Channel> streamChannels = new List<Channel>();
            foreach (Channel channel in Channels)
            {
                if (channel.OwnerStream == dataStream)
                {
                    streamChannels.Add(channel);
                }
            }
            return streamChannels;
        }

        /// <summary>
        /// Gets all enabled channels
        /// </summary>
        /// <returns>Collection of enabled channels</returns>
        public IEnumerable<Channel> GetEnabledChannels()
        {
            List<Channel> enabledChannels = new List<Channel>();
            foreach (Channel channel in Channels)
            {
                if (channel.IsEnabled)
                {
                    enabledChannels.Add(channel);
                }
            }
            return enabledChannels;
        }

        /// <summary>
        /// Updates channel labels after changes
        /// </summary>
        private void UpdateChannelLabels()
        {
            for (int i = 0; i < Channels.Count; i++)
            {
                Channels[i].Settings.Label = $"CH{i + 1}";
            }
        }

        public IDataStream CreateDataStreamFromUserInput(StreamSettings vm)
        {
            // Determine stream type based on StreamSource property
            switch (vm.StreamSource)
            {
                case StreamSource.Demo:
                    DemoSettings demoSettings = new DemoSettings(vm.NumberOfChannels, vm.DemoSampleRate, vm.DemoSignalType);
                    DemoDataStream demoStream = new DemoDataStream(demoSettings);
                    return demoStream;
                    
                case StreamSource.AudioInput:
                    AudioDataStream audioStream = new AudioDataStream(vm.AudioDevice, vm.AudioSampleRate);
                    return audioStream;
                    
                case StreamSource.File:
                    FileSettings fileSettings = new FileSettings(
                        filePath: vm.FilePath,
                        sampleRate: vm.FileSampleRate,
                        loopPlayback: vm.FileLoopPlayback,
                        hasHeader: vm.FileHasHeader,
                        delimiter: vm.FileDelimiter
                    );
                    FileDataStream fileStream = new FileDataStream(fileSettings);
                    return fileStream;
                    
                case StreamSource.SerialPort:
                default:
                    // Default to SerialDataStream
                    SourceSetting sourceSetting = new SourceSetting(vm.Port, vm.Baud, vm.DataBits, vm.StopBits, vm.Parity);
                    DataParser dataParser;
                    
                    // Convert NumberType to BinaryFormat
                    DataParser.BinaryFormat binaryFormat = vm.NumberType switch
                    {
                        NumberTypeEnum.Int16 => DataParser.BinaryFormat.int16_t,
                        NumberTypeEnum.Uint16 => DataParser.BinaryFormat.uint16_t,
                        NumberTypeEnum.Int32 => DataParser.BinaryFormat.int32_t,
                        NumberTypeEnum.Uint32 => DataParser.BinaryFormat.uint32_t,
                        NumberTypeEnum.Float => DataParser.BinaryFormat.float_t,
                        _ => DataParser.BinaryFormat.uint16_t // Default fallback
                    };
                    
                    // Simple approach: ASCII or RawBinary
                    if (vm.DataFormat == DataFormatType.ASCII)
                    {
                        char frameEnd = '\n';
                        char separator = ParseDelimiter(vm.Delimiter);
                        dataParser = new DataParser(vm.NumberOfChannels, frameEnd, separator);
                    }
                    else
                    {
                        // RawBinary - check if frame start is provided
                        byte[] frameStartBytes = ParseFrameStartBytes(vm.FrameStart);
                        if (frameStartBytes != null && frameStartBytes.Length > 0)
                            dataParser = new DataParser(binaryFormat, vm.NumberOfChannels, frameStartBytes);
                        else
                            dataParser = new DataParser(binaryFormat, vm.NumberOfChannels);
                    }
                    
                    SerialDataStream stream = new SerialDataStream(sourceSetting, dataParser);
                    return stream;
            }
        }

        private byte[] ParseFrameStartBytes(string frameStart)
        {
            if (string.IsNullOrEmpty(frameStart))
                return new byte[] { 0xAA, 0xAA };
            try
            {
                string[] parts = frameStart.Split(new char[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                List<byte> bytes = new List<byte>();
                foreach (string part in parts)
                {
                    string cleanPart = part.Trim().Replace("0x", "").Replace("0X", "");
                    if (byte.TryParse(cleanPart, System.Globalization.NumberStyles.HexNumber, null, out byte b))
                        bytes.Add(b);
                }
                return bytes.Count > 0 ? bytes.ToArray() : new byte[] { 0xAA, 0xAA };
            }
            catch { return new byte[] { 0xAA, 0xAA }; }
        }

        private char ParseDelimiter(string delimiter)
        {
            if (string.IsNullOrEmpty(delimiter)) 
                return ',';
                
            string lowerDelimiter = delimiter.ToLower();
            switch (lowerDelimiter)
            {
                case "comma":
                case ",":
                    return ',';
                case "space":
                case " ":
                    return ' ';
                case "tab":
                case "\t":
                    return '\t';
                case "semicolon":
                case ";":
                    return ';';
                default:
                    return delimiter[0];
            }
        }

        public void AddStreamInfoPanel(StreamSettings settings, IDataStream datastream)
        {
            StreamInfoPanel panel = new StreamInfoPanel(datastream, settings);
            panel.OnRemoveClickedEvent += (s, args) => RemoveStreamByDataStream(datastream);
            Panel_Streams.Children.Add(panel);
        }
                
        /// <summary>
        /// Removes a stream by its data stream reference
        /// Much simpler than the old approach - no complex searching needed
        /// </summary>
        /// <param name="dataStream">The data stream to remove</param>
        public void RemoveStreamByDataStream(IDataStream dataStream)
        {
            if (dataStream == null)
                return;

            // Remove channels associated with this stream - this handles everything!
            RemoveChannelsForStream(dataStream);

            // Find and remove the StreamInfoPanel
            for (int i = Panel_Streams.Children.Count - 1; i >= 0; i--)
            {
                if (Panel_Streams.Children[i] is StreamInfoPanel panel && 
                    panel.AssociatedDataStream == dataStream)
                {
                    Panel_Streams.Children.RemoveAt(i);
                    break;
                }
            }

            // ObservableCollection.CollectionChanged handles all notifications automatically
        }

        /// <summary>
        /// Disposes all streams and cleans up resources
        /// </summary>
        public void Dispose()
        {
            // Dispose all streams through channels
            List<IDataStream> streamsToDispose = new List<IDataStream>();
            foreach (Channel channel in Channels)
            {
                if (!streamsToDispose.Contains(channel.OwnerStream))
                {
                    streamsToDispose.Add(channel.OwnerStream);
                }
            }

            foreach (IDataStream stream in streamsToDispose)
            {
                stream.StopStreaming();
                stream.Disconnect();
                stream.Dispose();
            }
            
            Panel_Streams.Children.Clear();
            
            // Clean up all channels - ObservableCollection.CollectionChanged handles notifications
            foreach (Channel channel in Channels)
            {
                channel.Dispose();
            }
            Channels.Clear();
        }
        
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
