using System.Windows;
using System.Windows.Controls;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Media;
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
            
            // Subscribe to channel collection changes to track disposal
            Channels.CollectionChanged += Channels_CollectionChanged;
        }

        /// <summary>
        /// Handles collection changes to subscribe to channel disposal events
        /// </summary>
        private void Channels_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
            {
                foreach (Channel channel in e.NewItems)
                {
                    // Subscribe to the channel's owner stream disposal
                    if (channel.OwnerStream != null)
                    {
                        channel.OwnerStream.Disposing += OnStreamDisposing;
                    }
                }
            }
            else if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Remove)
            {
                foreach (Channel channel in e.OldItems)
                {
                    // Unsubscribe when channel is removed
                    if (channel.OwnerStream != null)
                    {
                        channel.OwnerStream.Disposing -= OnStreamDisposing;
                    }
                }
            }
        }

        /// <summary>
        /// Called when any stream (physical or virtual) is disposing
        /// Automatically removes all channels that belong to that stream
        /// </summary>
        private void OnStreamDisposing(object sender, EventArgs e)
        {
            if (sender is IDataStream disposingStream)
            {
                // Must run on UI thread
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    // Find all channels that belong to this stream
                    List<Channel> channelsToRemove = new List<Channel>();
                    foreach (Channel channel in Channels)
                    {
                        if (channel.OwnerStream == disposingStream)
                        {
                            channelsToRemove.Add(channel);
                        }
                    }

                    // Remove them (triggers cascade for dependent virtual channels)
                    foreach (Channel channel in channelsToRemove)
                    {
                        Channels.Remove(channel);
                    }

                    // Update UI
                    OnPropertyChanged(nameof(TotalChannelCount));
 
                    // Also remove the StreamInfoPanel
                    RemoveStreamInfoPanelForStream(disposingStream);
                });
            }
        }

        /// <summary>
        /// Finds the StreamInfoPanel associated with a specific stream, or null if none exists.
        /// </summary>
        private StreamInfoPanel FindPanelForStream(IDataStream dataStream)
        {
            foreach (UIElement child in Panel_Streams.Children)
            {
                if (child is StreamInfoPanel panel && panel.AssociatedDataStream == dataStream)
                    return panel;
            }
            return null;
        }

        /// <summary>
        /// Removes the StreamInfoPanel for a specific stream
        /// </summary>
        private void RemoveStreamInfoPanelForStream(IDataStream dataStream)
        {
            StreamInfoPanel panel = FindPanelForStream(dataStream);
            if (panel != null)
                Panel_Streams.Children.Remove(panel);
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
                else if (dataStream is VirtualDataStream virtualStream)
                {
                    // Virtual channels inherit their source channel's color
                    // This maintains visual consistency and avoids forced palette colors
                    Channel sourceChannel = virtualStream.GetPrimarySourceChannel();
                    if (sourceChannel != null)
                    {
                        channelSettings.Color = sourceChannel.Settings.Color;
                    }
                    else
                    {
                        // Fallback if no source available (all constant operands)
                        channelSettings.Color = PlotManager.GetColor(globalIndex);
                    }
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
        /// Now works with automatic disposal cascade - disposing the stream triggers auto-removal
        /// </summary>
        /// <param name="dataStream">The data stream whose channels should be removed</param>
        private void RemoveChannelsForStream(IDataStream dataStream)
        {
            if (dataStream == null)
                return;

            // Unsubscribe from disposal event before disposing
            dataStream.Disposing -= OnStreamDisposing;

            // Clean up the stream - this will trigger OnStreamDisposing for any dependent virtual channels
            dataStream.StopStreaming();
            dataStream.Disconnect();
            dataStream.Dispose(); // This triggers cascade disposal of virtual channels
 
            // Note: Channels will be auto-removed via OnStreamDisposing event
            // But we still need to clean up channels that belong directly to this stream
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
            return vm.CreateDataStream();
        }

        public void AddStreamInfoPanel(StreamSettings settings, IDataStream datastream)
        {
            StreamInfoPanel panel = new StreamInfoPanel(datastream, settings);
            panel.OnRemoveClickedEvent += (s, args) => RemoveStreamByDataStream(datastream);
            panel.OnReconfigureClickedEvent += (s, args) => ReconfigureStream(datastream);
            Panel_Streams.Children.Add(panel);
        }

        /// <summary>
        /// Reconfigures a running stream: reopens the stream config dialog pre-filled with its
        /// current settings, then on acceptance tears down the old stream and rebuilds it with
        /// the edited settings (port/baud, channel count, data format, resampler factor, etc.),
        /// reapplying each channel's label/color/gain/offset/filter/measurements by index - the
        /// same snapshot/restore convention Serializer already uses when loading a saved session.
        /// Cancelling the dialog leaves the original stream running untouched.
        /// </summary>
        public void ReconfigureStream(IDataStream dataStream)
        {
            StreamInfoPanel panel = FindPanelForStream(dataStream);
            StreamSettings settings = panel?.AssociatedStreamSettings;
            if (settings == null)
                return;

            // Snapshot per-channel settings + measurement types; reapplied by index after rebuild
            var channelSnapshots = GetChannelsForStream(dataStream)
                .Select(c => (Settings: c.Settings, Measurements: c.Measurements.Select(m => m.Type).ToList()))
                .ToList();

            SerialConfigWindow window = new SerialConfigWindow(settings);
            window.TextBlock_Title.Text = "Reconfigure Stream";
            window.Owner = Window.GetWindow(this);
            if (window.ShowDialog() != true)
                return;

            settings.UpdateFromWindow(window);
            IDataStream newStream = settings.CreateDataStream();
            newStream.Connect();
            newStream.StartStreaming();

            // Stops/disconnects/disposes the old stream and cascades dependent virtual-channel cleanup
            RemoveChannelsForStream(dataStream);
            RemoveStreamInfoPanelForStream(dataStream);

            Color[] colors = channelSnapshots.Select(s => s.Settings.Color).ToArray();
            AddChannelsForStream(newStream, colors);

            List<Channel> newChannels = GetChannelsForStream(newStream).ToList();
            int count = Math.Min(channelSnapshots.Count, newChannels.Count);
            for (int i = 0; i < count; i++)
                Serializer.ApplyChannelSettings(newChannels[i], channelSnapshots[i].Settings, channelSnapshots[i].Measurements);

            AddStreamInfoPanel(settings, newStream);
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
            RemoveStreamInfoPanelForStream(dataStream);

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
