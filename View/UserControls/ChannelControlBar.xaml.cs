using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Media;
using SerialPlotDN_WPF.Model;

namespace SerialPlotDN_WPF.View.UserControls
{
    /// <summary>
    /// Interaction logic for ChannelControlBar.xaml
    /// Modernized: Single collection with direct data binding - no redundancy!
    /// </summary>
    public partial class ChannelControlBar : UserControl
    {
        /// <summary>
        /// Single source of truth: ChannelSettings collection that drives the UI directly via DataTemplate
        /// </summary>
        public ObservableCollection<ChannelSettings> ChannelSettings { get; private set; } = new ObservableCollection<ChannelSettings>();

        public ChannelControlBar()
        {
            InitializeComponent();
            // Direct binding - WPF automatically creates ChannelControl for each ChannelSettings via DataTemplate
            ChannelItemsControl.ItemsSource = ChannelSettings;
        }

        /// <summary>
        /// Constructor that accepts an existing collection of ChannelSettings
        /// </summary>
        /// <param name="channelSettings">Collection of ChannelSettings to use</param>
        public ChannelControlBar(IEnumerable<ChannelSettings> channelSettings) : this()
        {
            SetChannelSettings(channelSettings);
        }

        /// <summary>
        /// Sets the ChannelSettings collection, replacing the current one
        /// </summary>
        /// <param name="channelSettings">New collection of ChannelSettings</param>
        public void SetChannelSettings(IEnumerable<ChannelSettings> channelSettings)
        {
            // Create new ObservableCollection with the provided settings
            ChannelSettings = new ObservableCollection<ChannelSettings>(channelSettings ?? Enumerable.Empty<ChannelSettings>());
            
            // Update ItemsSource to point to new collection
            ChannelItemsControl.ItemsSource = ChannelSettings;
        }

        /// <summary>
        /// Replaces the current ChannelSettings collection with a new one
        /// </summary>
        /// <param name="channelSettings">New collection of ChannelSettings</param>
        public void ReplaceChannelSettings(IEnumerable<ChannelSettings> channelSettings)
        {
            SetChannelSettings(channelSettings);
        }

        /// <summary>
        /// Adds a new channel setting
        /// </summary>
        /// <param name="settings">ChannelSettings to add</param>
        public void AddChannelSetting(ChannelSettings settings)
        {
            ChannelSettings.Add(settings);
        }

        /// <summary>
        /// Removes a channel setting
        /// </summary>
        /// <param name="settings">ChannelSettings to remove</param>
        public void RemoveChannelSetting(ChannelSettings settings)
        {
            ChannelSettings.Remove(settings);
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
                
                // Set color if provided, otherwise use default color scheme
                Color channelColor;
                if (channelColors != null && channelIndex < channelColors.Length)
                {
                    channelColor = channelColors[channelIndex];
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
                    channelColor = defaultColors[channelIndex % defaultColors.Length];
                }

                var channelSettings = new ChannelSettings($"CH{channelIndex + 1}", channelColor)
                {
                    Gain = 1.0,
                    Offset = 0.0
                };

                ChannelSettings.Add(channelSettings);
            }
        }

        /// <summary>
        /// Clears all channel settings
        /// </summary>
        public void ClearChannels()
        {
            ChannelSettings.Clear();
        }

        /// <summary>
        /// Gets the number of channels
        /// </summary>
        public int ChannelCount => ChannelSettings.Count;

        /// <summary>
        /// Gets a specific channel setting by index
        /// </summary>
        /// <param name="index">Channel index</param>
        /// <returns>ChannelSettings at the specified index, or null if index is out of range</returns>
        public ChannelSettings GetChannelSettings(int index)
        {
            return index >= 0 && index < ChannelSettings.Count ? ChannelSettings[index] : null;
        }

        /// <summary>
        /// Gets all channel settings as a list (for external use)
        /// </summary>
        /// <returns>List of all ChannelSettings</returns>
        public List<ChannelSettings> GetAllChannelSettings()
        {
            return ChannelSettings.ToList();
        }

        /// <summary>
        /// Updates a specific channel setting by index
        /// </summary>
        /// <param name="index">Channel index</param>
        /// <param name="newSettings">New ChannelSettings to replace the existing one</param>
        public void UpdateChannelSettings(int index, ChannelSettings newSettings)
        {
            if (index >= 0 && index < ChannelSettings.Count && newSettings != null)
            {
                ChannelSettings[index] = newSettings;
            }
        }

        /// <summary>
        /// Inserts a channel setting at a specific index
        /// </summary>
        /// <param name="index">Index to insert at</param>
        /// <param name="settings">ChannelSettings to insert</param>
        public void InsertChannelSetting(int index, ChannelSettings settings)
        {
            if (index >= 0 && index <= ChannelSettings.Count && settings != null)
            {
                ChannelSettings.Insert(index, settings);
            }
        }

        /// <summary>
        /// Removes a channel setting at a specific index
        /// </summary>
        /// <param name="index">Index to remove</param>
        public void RemoveChannelSettingAt(int index)
        {
            if (index >= 0 && index < ChannelSettings.Count)
            {
                ChannelSettings.RemoveAt(index);
            }
        }
    }
}
