using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SerialPlotDN_WPF.Model;

namespace SerialPlotDN_WPF.View.UserControls
{
    /// <summary>
    /// Event arguments for measurement requests
    /// </summary>
    public class MeasurementRequestEventArgs : EventArgs
    {
        public int ChannelIndex { get; }
        public ChannelSettings ChannelSettings { get; }
        public Channel Channel { get; }

        public MeasurementRequestEventArgs(int channelIndex, ChannelSettings channelSettings, Channel channel)
        {
            ChannelIndex = channelIndex;
            ChannelSettings = channelSettings;
            Channel = channel;
        }
    }

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
    /// Works with DataStreamBar for clean channel-centric architecture
    /// </summary>
    public partial class ChannelControlBar : UserControl
    {
        /// <summary>
        /// Collection of ChannelSettings that drives the UI directly via DataTemplate
        /// Synced with DataStreamBar channels
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

        /// <summary>
        /// Reference to the DataStreamBar for direct channel access
        /// </summary>
        public DataStreamBar DataStreamBar { get; set; }

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
            if (channelSettings != null && DataStreamBar != null)
            {
                // Get the corresponding channel object
                Channel channel = DataStreamBar.GetChannelByIndex(channelSettings.ChannelIndex);
                if (channel != null)
                {
                    MeasurementRequestEventArgs args = new MeasurementRequestEventArgs(channelSettings.ChannelIndex, channelSettings, channel);
                    ChannelMeasurementRequested?.Invoke(this, args);
                }
            }
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
            foreach (Channel channel in dataStreamBar.Channels)
            {
                ChannelSettings.Add(channel.Settings);
            }

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
