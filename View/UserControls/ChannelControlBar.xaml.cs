using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SerialPlotDN_WPF.Model;
using SerialPlotDN_WPF.View.UserControls;

namespace SerialPlotDN_WPF.View.UserControls
{
    /// <summary>
    /// Simple relay command implementation for MVVM pattern
    /// </summary>
    public class RelayCommand<T> : ICommand
    {
        private readonly System.Action<T> _execute;
        private readonly System.Func<T, bool> _canExecute;

        public RelayCommand(System.Action<T> execute, System.Func<T, bool> canExecute = null)
        {
            _execute = execute ?? throw new System.ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter)
        {
            return _canExecute == null || _canExecute((T)parameter);
        }

        public void Execute(object parameter)
        {
            _execute((T)parameter);
        }

        public event System.EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }

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

        /// <summary>
        /// Command to handle measurement requests from ChannelControl instances
        /// </summary>
        public ICommand MeasurementCommand { get; private set; }

        public ChannelControlBar()
        {
            InitializeComponent();
            ChannelItemsControl.ItemsSource = ChannelSettings;
            
            // Create command to handle measurement requests
            MeasurementCommand = new RelayCommand<ChannelSettings>(OnMeasurementRequested);
            
            // Subscribe to collection changes to update channel indices
            ChannelSettings.CollectionChanged += ChannelSettings_CollectionChanged;
        }

        private void ChannelSettings_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateChannelIndices();
        }

        private void OnMeasurementRequested(ChannelSettings channelSettings)
        {
            if (channelSettings != null)
            {
                var args = new MeasurementRequestEventArgs(channelSettings.ChannelIndex, channelSettings);
                ChannelMeasurementRequested?.Invoke(this, args);
            }
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
                channelSettings.ChannelIndex = channelIndex; // Set the channel index

                ChannelSettings.Add(channelSettings);
            }
            
            // Update channel indices after changes
            UpdateChannelIndices();
        }

        /// <summary>
        /// Updates channel indices using data binding instead of visual tree traversal
        /// </summary>
        private void UpdateChannelIndices()
        {
            for (int i = 0; i < ChannelSettings.Count; i++)
            {
                // Set the index directly on the data model
                ChannelSettings[i].ChannelIndex = i;
            }
        }
    }
}
