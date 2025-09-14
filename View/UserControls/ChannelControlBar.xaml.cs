using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using PowerScope.Model;

namespace PowerScope.View.UserControls
{
    /// <summary>
    /// Interaction logic for ChannelControlBar.xaml
    /// Works with DataStreamBar for clean channel-centric architecture
    /// Simplified - no more complex measurement request handling
    /// </summary>
    public partial class ChannelControlBar : UserControl
    {
        /// <summary>
        /// Collection of ChannelSettings that drives the UI directly via DataTemplate
        /// Synced with DataStreamBar channels
        /// </summary>
        public ObservableCollection<ChannelSettings> ChannelSettings { get; private set; } = new ObservableCollection<ChannelSettings>();

        /// <summary>
        /// Reference to the DataStreamBar for direct channel access
        /// </summary>
        public DataStreamBar DataStreamBar { get; set; }

        public ChannelControlBar()
        {
            InitializeComponent();
            ChannelItemsControl.ItemsSource = ChannelSettings;
        }

        /// <summary>
        /// Updates the channel settings based on the DataStreamBar channels
        /// </summary>
        /// <param name="dataStreamBar">The data stream bar to sync with</param>
        public void UpdateFromDataStreamBar(DataStreamBar dataStreamBar)
        {
            if (dataStreamBar == null)
                return;

            DataStreamBar = dataStreamBar;

            // Clear existing settings
            ChannelSettings.Clear();

            // Add settings from all channels in the data stream bar
            // No need for dictionary mapping anymore - Channel subscribes to ChannelSettings events
            foreach (Channel channel in dataStreamBar.Channels)
            {
                ChannelSettings.Add(channel.Settings);
            }
        }
    }
}
