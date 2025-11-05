using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using PowerScope.Model;
using PowerScope.View.UserForms;

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
        /// 
        // Looking at this I find it strange that we provide a reference to DataStreamBar but it has been useful
        // Maybe in future we can remove this or find a cleaner way to link them
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

        /// <summary>
        /// Handles the Add Virtual Channel button click
        /// Opens VirtualChannelSettingsWindow for configuration
        /// </summary>
        private void AddVirtualChannelButton_Click(object sender, RoutedEventArgs e)
        {
            // Get list of available channels from DataStreamBar
            var availableChannels = new List<Channel>();
            if (DataStreamBar != null)
            {
                availableChannels.AddRange(DataStreamBar.Channels);
            }

            // If no channels available, show message
            if (availableChannels.Count == 0)
            {
                MessageBox.Show("No input channels available. Please add a data stream first.",
                    "No Channels Available",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            // Create a new virtual channel settings
            var virtualChannelSettings = new ChannelSettings
            {
                Label = "Virtual Channel",
                Color = System.Windows.Media.Colors.LimeGreen,
                IsEnabled = true,
                Gain = 1.0,
                Offset = 0.0
            };

            // Create the view model with the available channels and target settings
            var virtualChannelConfig = new VirtualChannelConfig(availableChannels, virtualChannelSettings);

            // Create and show the virtual channel settings window
            var virtualChannelWindow = new VirtualChannelSettingsWindow(virtualChannelConfig);
            bool? dialogResult = virtualChannelWindow.ShowDialog();

            // Only add the virtual channel if the user clicked Apply
            if (dialogResult == true)
            {
                // Update the channel settings label from the config
                virtualChannelSettings.Label = virtualChannelConfig.Label;

                // Add to the collection
                ChannelSettings.Add(virtualChannelSettings);
            }
        }
    }
}
