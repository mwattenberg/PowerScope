using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SerialPlotDN_WPF.Model;
using SerialPlotDN_WPF.View.UserControls;

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

        /// <summary>
        /// Event raised when a measurement is requested for a specific channel
        /// </summary>
        public event System.EventHandler<MeasurementRequestEventArgs> ChannelMeasurementRequested;

        public ChannelControlBar()
        {
            InitializeComponent();
            ChannelItemsControl.ItemsSource = ChannelSettings;
            
            // Subscribe to collection changes to update channel indices
            ChannelItemsControl.Loaded += ChannelItemsControl_Loaded;
        }

        private void ChannelItemsControl_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateChannelIndices();
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
            
            // Update channel indices after changes
            Dispatcher.BeginInvoke(() => UpdateChannelIndices());
        }

        /// <summary>
        /// Updates channel indices and wire up event handlers for all ChannelControl instances
        /// </summary>
        private void UpdateChannelIndices()
        {
            for (int i = 0; i < ChannelItemsControl.Items.Count; i++)
            {
                var container = ChannelItemsControl.ItemContainerGenerator.ContainerFromIndex(i) as ContentPresenter;
                if (container != null)
                {
                    var channelControl = FindChildOfType<ChannelControl>(container);
                    if (channelControl != null)
                    {
                        // Set channel index
                        channelControl.ChannelIndex = i;
                        
                        // Remove existing event handler to prevent duplicates
                        channelControl.MeasurementRequested -= ChannelControl_MeasurementRequested;
                        // Add event handler
                        channelControl.MeasurementRequested += ChannelControl_MeasurementRequested;
                    }
                }
            }
        }

        /// <summary>
        /// Handle measurement request from individual channel controls
        /// </summary>
        private void ChannelControl_MeasurementRequested(object sender, MeasurementRequestEventArgs e)
        {
            // Forward the event to parent controls
            ChannelMeasurementRequested?.Invoke(this, e);
        }

        /// <summary>
        /// Helper method to find child control of specific type
        /// </summary>
        private static T FindChildOfType<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;

            T foundChild = null;
            int childrenCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childrenCount; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T)
                {
                    foundChild = (T)child;
                    break;
                }
                else
                {
                    foundChild = FindChildOfType<T>(child);
                    if (foundChild != null) break;
                }
            }
            return foundChild;
        }
    }
}
