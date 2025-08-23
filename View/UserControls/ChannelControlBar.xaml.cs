using System.Collections.ObjectModel;
using System.Windows.Controls;
using System.Windows.Media;

namespace SerialPlotDN_WPF.View.UserControls
{
    /// <summary>
    /// Interaction logic for ChannelControlBar.xaml
    /// </summary>
    public partial class ChannelControlBar : UserControl
    {
        public ObservableCollection<ChannelControl> Channels { get; } = new ObservableCollection<ChannelControl>();

        public ChannelControlBar()
        {
            InitializeComponent();
            ChannelItemsControl.ItemsSource = Channels;
        }

        // Helper to add a channel control
        public void AddChannel(ChannelControl control)
        {
            Channels.Add(control);
        }

        // Helper to remove a channel control
        public void RemoveChannel(ChannelControl control)
        {
            Channels.Remove(control);
        }

        /// <summary>
        /// Updates the channel controls based on the total number of channels from active streams
        /// </summary>
        /// <param name="totalChannels">Total number of channels needed</param>
        /// <param name="channelColors">Array of colors for each channel (optional)</param>
        public void UpdateChannels(int totalChannels, Color[] channelColors = null)
        {
            // Remove excess channels
            while (Channels.Count > totalChannels)
            {
                Channels.RemoveAt(Channels.Count - 1);
            }

            // Add missing channels
            while (Channels.Count < totalChannels)
            {
                int channelIndex = Channels.Count;
                ChannelControl channel = new ChannelControl
                {
                    Label = $"CH{channelIndex + 1}",
                    Gain = 1.0,
                    Offset = 0.0
                };

                // Set color if provided, otherwise use default color scheme
                if (channelColors != null && channelIndex < channelColors.Length)
                {
                    channel.Color = channelColors[channelIndex];
                }
                else
                {
                    // Use a default color scheme if no colors provided
                    Color[] defaultColors = new Color[]
                    {
                        Colors.Red, Colors.Green, Colors.Blue, Colors.Orange,
                        Colors.Purple, Colors.Brown, Colors.Pink, Colors.Gray,
                        Colors.Yellow, Colors.Cyan, Colors.Magenta, Colors.Lime
                    };
                    channel.Color = defaultColors[channelIndex % defaultColors.Length];
                }

                Channels.Add(channel);
            }
        }

        /// <summary>
        /// Clears all channel controls
        /// </summary>
        public void ClearChannels()
        {
            Channels.Clear();
        }
    }
}
