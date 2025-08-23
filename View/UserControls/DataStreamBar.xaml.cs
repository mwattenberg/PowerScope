using SerialPlotDN_WPF.Model;
using SerialPlotDN_WPF.View.UserForms;
using System.Windows;
using System.Windows.Controls;


namespace SerialPlotDN_WPF.View.UserControls
{
    /// <summary>
    /// Interaction logic for DataStreamBar.xaml
    /// </summary>

    public partial class DataStreamBar : UserControl
    {
        public List<DataStreamViewModel> DataStreams { get; private set; } = new List<DataStreamViewModel>();

        // Event to notify when channels need to be updated
        public event System.Action<int> ChannelsChanged;

        public DataStreamBar()
        {
            InitializeComponent();
            
            // No need to initialize a single DataStreamManager
            //ItemsControl_Streams.ItemsSource = DataStreamManagers.SelectMany(m => m.StreamViewModels);
        }

        private void Button_AddStream_Click(object sender, RoutedEventArgs e)
        {
            DataStreamViewModel vm = new DataStreamViewModel();
            SerialConfigWindow configWindow = new SerialConfigWindow(vm);
            if (configWindow.ShowDialog() == true)
            {
                DataStreams.Add(vm);
                AddStreamInfoPanel(vm);
                
                // Subscribe to property changes to monitor NumberOfChannels and IsConnected
                vm.PropertyChanged += DataStreamViewModel_PropertyChanged;

                // Automatically connect the serial stream after successful configuration
                vm.Connect();

                // Update channels after adding new stream
                UpdateChannels();
            }
        }

        private void AddStreamInfoPanel(DataStreamViewModel viewModel)
        {
            StreamInfoPanel panel = new StreamInfoPanel
            {
                DataContext = viewModel,
            };
            panel.OnRemoveClickedEvent += (s, args) => RemoveStream(viewModel);
            Panel_Streams.Children.Add(panel);
        }

        /// <summary>
        /// Adds a stream from settings without showing the configuration dialog
        /// </summary>
        /// <param name="viewModel">The stream view model to add</param>
        public void AddStreamFromSettings(DataStreamViewModel viewModel)
        {
            DataStreams.Add(viewModel);
            AddStreamInfoPanel(viewModel);

            // Subscribe to property changes
            viewModel.PropertyChanged += DataStreamViewModel_PropertyChanged;

            // Update channels after adding stream from settings
            UpdateChannels();
        }

        /// <summary>
        /// Removes a stream and disposes its resources
        /// </summary>
        /// <param name="viewModel">The stream view model to remove</param>
        public void RemoveStream(DataStreamViewModel viewModel)
        {
            if (DataStreams.Contains(viewModel))
            {
                // Unsubscribe from property changes
                viewModel.PropertyChanged -= DataStreamViewModel_PropertyChanged;

                // Disconnect and dispose the stream
                viewModel.Disconnect();
                viewModel.Dispose();
                
                // Remove from collection
                DataStreams.Remove(viewModel);
                
                // Find and remove the corresponding panel
                for (int i = Panel_Streams.Children.Count - 1; i >= 0; i--)
                {
                    if (Panel_Streams.Children[i] is StreamInfoPanel panel && 
                        panel.DataContext == viewModel)
                    {
                        Panel_Streams.Children.RemoveAt(i);
                        break;
                    }
                }

                // Update channels after removing stream
                UpdateChannels();
            }
        }

        /// <summary>
        /// Disposes all streams and cleans up resources
        /// </summary>
        public void Dispose()
        {
            foreach (var stream in DataStreams.ToList())
            {
                stream.PropertyChanged -= DataStreamViewModel_PropertyChanged;
                stream.Disconnect();
                stream.Dispose();
            }
            DataStreams.Clear();
            Panel_Streams.Children.Clear();

            // Clear all channels when disposing
            ChannelsChanged?.Invoke(0);
        }

        /// <summary>
        /// Handles property changes in DataStreamViewModel to update channels when needed
        /// </summary>
        private void DataStreamViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // Update channels when NumberOfChannels or IsConnected changes
            if (e.PropertyName == nameof(DataStreamViewModel.NumberOfChannels))
            {
                UpdateChannels();
            }
        }

        /// <summary>
        /// Calculates total channels from connected streams and notifies listeners
        /// </summary>
        private void UpdateChannels()
        {
            // Sum up channels from all connected streams
            int totalChannels = 0;
            foreach(var vm in DataStreams)
            {
                totalChannels = totalChannels + vm.NumberOfChannels;
            }
            
            // Notify subscribers about the channel count change
            ChannelsChanged?.Invoke(totalChannels);
        }

        /// <summary>
        /// Gets the total number of channels from all connected streams
        /// </summary>
        public int GetTotalChannelCount()
        {
            int totalChannels = 0;
            foreach (var vm in DataStreams)
            {
                totalChannels = totalChannels + vm.NumberOfChannels;
            }
            return totalChannels;
        }

        ///// <summary>
        ///// Gets all connected streams
        ///// </summary>
        public IEnumerable<DataStreamViewModel> GetConnectedStreams()
        {
            List<DataStreamViewModel> connectedStreams = new List<DataStreamViewModel>();
            foreach (var stream in DataStreams)
            {
                if (stream.IsConnected)
                    connectedStreams.Add(stream);
            }
            return connectedStreams;
        }
    }
}
