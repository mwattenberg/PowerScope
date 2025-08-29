using System.Collections.ObjectModel;
using System.Windows.Controls;
using System.Windows.Media;
using SerialPlotDN_WPF.Model;

namespace SerialPlotDN_WPF.View.UserControls
{
    /// <summary>
    /// Interaction logic for ChannelControlBar.xaml
    /// Manages channel settings collection with direct data binding via DataTemplate
    /// </summary>
    public partial class ChannelControlBar : UserControl
    {
        /// <summary>
        /// Collection of ChannelSettings that drives the UI directly via DataTemplate
        /// </summary>
        public ObservableCollection<ChannelSettings> ChannelSettings { get; private set; } = new ObservableCollection<ChannelSettings>();

        public ChannelControlBar()
        {
            InitializeComponent();
            ChannelItemsControl.ItemsSource = ChannelSettings;
        }

        /// <summary>
        /// Updates the channel settings based on the total number of channels from active streams
        /// </summary>
        /// <param name="totalChannels">Total number of channels needed</param>
        /// <param name="channelColors">Array of colors for each channel (optional)</param>
        public void UpdateChannels(int totalChannels, Color[] channelColors = null)
        {
            // Remove excess channels
            while (ChannelSettings.Count > totalChannels)
            {
                ChannelSettings.RemoveAt(ChannelSettings.Count - 1);
            }

            // Add missing channels
            while (ChannelSettings.Count < totalChannels)
            {
                int channelIndex = ChannelSettings.Count;
                
                // Set color if provided, otherwise use ScottPlot palette
                Color channelColor;
                if (channelColors != null && channelIndex < channelColors.Length)
                {
                    channelColor = channelColors[channelIndex];
                }
                else
                {
                    // Use ScottPlot Category10 palette
                    channelColor = PlotManager.GetColor(channelIndex);
                }

                ChannelSettings channelSettings = new ChannelSettings();
                channelSettings.Label = string.Format("CH{0}", channelIndex + 1);
                channelSettings.Color = channelColor;
                channelSettings.Gain = 1.0;
                channelSettings.Offset = 0.0;

                ChannelSettings.Add(channelSettings);
            }
        }
    }
}
