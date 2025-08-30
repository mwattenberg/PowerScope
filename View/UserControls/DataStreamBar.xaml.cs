using System.IO;
using System.Windows;
using System.Windows.Controls;
using ScottPlot.DataViews;
using SerialPlotDN_WPF.Model;
using SerialPlotDN_WPF.View.UserForms;


namespace SerialPlotDN_WPF.View.UserControls
{
    /// <summary>
    /// Interaction logic for DataStreamBar.xaml
    /// </summary>

    public partial class DataStreamBar : UserControl
    {
        public List<StreamSettings> ConfiguredDataStreams { get; private set; } = new List<StreamSettings>();
        public List<IDataStream> ConnectedDataStreams { get; } = new List<IDataStream>();

        // Event to notify when channels need to be updated
        public event System.Action<int> ChannelsChanged;
        
        // Event to notify when streams have changed (added/removed)
        public event System.Action StreamsChanged;

        public DataStreamBar()
        {
            InitializeComponent();
            
            // No need to initialize a single DataStreamManager
            //ItemsControl_Streams.ItemsSource = DataStreamManagers.SelectMany(m => m.StreamViewModels);
        }

        private void Button_AddStream_Click(object sender, RoutedEventArgs e)
        {
            StreamSettings settings = new StreamSettings();
            SerialConfigWindow configWindow = new SerialConfigWindow(settings);
            if (configWindow.ShowDialog() == true)
            {
                ConfiguredDataStreams.Add(settings);
                // Create and connect IDataStream from config
                var dataStream = CreateDataStreamFromUserInput(settings);
                ConnectedDataStreams.Add(dataStream);

                dataStream.Connect();
                dataStream.StartStreaming();

                UpdateChannels();
                
                // Notify that streams have changed so PlotManager can update channel settings
                StreamsChanged?.Invoke();
            }
        }

        public IDataStream CreateDataStreamFromUserInput(StreamSettings vm)
        {
            // Determine stream type based on StreamSource property
            switch (vm.StreamSource)
            {
                case StreamSource.Demo:
                    var demoSettings = new DemoSettings(vm.NumberOfChannels, vm.DemoSampleRate, vm.DemoSignalType);
                    var demoStream = new DemoDataStream(demoSettings);
                    AddStreamInfoPanel(vm, demoStream);
                    return demoStream;
                    
                case StreamSource.SerialPort:
                default:
                    // Default to SerialDataStream
                    var sourceSetting = new SourceSetting(vm.Port, vm.Baud, vm.DataBits, vm.StopBits, vm.Parity);
                    DataParser dataParser;
                    if (vm.DataFormat == DataFormatType.RawBinary)
                    {
                        byte[] frameStartBytes = ParseFrameStartBytes(vm.FrameStart);
                        if (frameStartBytes != null && frameStartBytes.Length > 0)
                            dataParser = new DataParser(DataParser.BinaryFormat.uint16_t, vm.NumberOfChannels, frameStartBytes);
                        else
                            dataParser = new DataParser(DataParser.BinaryFormat.uint16_t, vm.NumberOfChannels);
                    }
                    else
                    {
                        char frameEnd = '\n';
                        char separator = ParseDelimiter(vm.Delimiter);
                        dataParser = new DataParser(vm.NumberOfChannels, frameEnd, separator);
                    }
                    var stream = new SerialDataStream(sourceSetting, dataParser);
                    AddStreamInfoPanel(vm, stream);
                    return stream;
            }
        }

        private byte[] ParseFrameStartBytes(string frameStart)
        {
            if (string.IsNullOrEmpty(frameStart))
                return new byte[] { 0xAA, 0xAA };
            try
            {
                string[] parts = frameStart.Split(new char[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                List<byte> bytes = new List<byte>();
                foreach (string part in parts)
                {
                    string cleanPart = part.Trim().Replace("0x", "").Replace("0X", "");
                    if (byte.TryParse(cleanPart, System.Globalization.NumberStyles.HexNumber, null, out byte b))
                        bytes.Add(b);
                }
                return bytes.Count > 0 ? bytes.ToArray() : new byte[] { 0xAA, 0xAA };
            }
            catch { return new byte[] { 0xAA, 0xAA }; }
        }

        private char ParseDelimiter(string delimiter)
        {
            if (string.IsNullOrEmpty(delimiter)) return ',';
            string lowerDelimiter = delimiter.ToLower();
            if (lowerDelimiter == "comma" || lowerDelimiter == ",") return ',';
            else if (lowerDelimiter == "space" || lowerDelimiter == " ") return ' ';
            else if (lowerDelimiter == "tab" || lowerDelimiter == "\t") return '\t';
            else if (lowerDelimiter == "semicolon" || lowerDelimiter == ";") return ';';
            else return delimiter[0];
        }

        private void AddStreamInfoPanel(StreamSettings viewModel, IDataStream datastream)
        {
            StreamInfoPanel panel = new StreamInfoPanel(datastream, viewModel);
            panel.OnRemoveClickedEvent += (s, args) => RemoveStream(viewModel);
            Panel_Streams.Children.Add(panel);
        }
                
        /// <summary>
        /// Removes a stream and disposes its resources
        /// </summary>
        /// <param name="viewModel">The stream view model to remove</param>
        public void RemoveStream(StreamSettings viewModel)
        {
            if (ConfiguredDataStreams.Contains(viewModel))
            {
                viewModel.PropertyChanged -= DataStreamViewModel_PropertyChanged;
                // Find and remove corresponding IDataStream
                IDataStream streamToRemove = null;
                
                if (viewModel.StreamSource == StreamSource.Demo)
                {
                    streamToRemove = ConnectedDataStreams.FirstOrDefault(ds => ds != null && ds.StreamType == "Demo");
                }
                else if (viewModel.StreamSource == StreamSource.SerialPort)
                {
                    streamToRemove = ConnectedDataStreams.FirstOrDefault(ds => ds != null && ds.StreamType == "Serial" && ds is SerialDataStream sds && sds.SourceSetting.PortName == viewModel.Port);
                }
                // Add other stream types as needed
                
                if (streamToRemove != null)
                {
                    streamToRemove.StopStreaming();
                    streamToRemove.Disconnect();
                    streamToRemove.Dispose();
                    ConnectedDataStreams.Remove(streamToRemove);
                }
                ConfiguredDataStreams.Remove(viewModel);
                for (int i = Panel_Streams.Children.Count - 1; i >= 0; i--)
                {
                    if (Panel_Streams.Children[i] is StreamInfoPanel panel && panel.DataContext == viewModel)
                    {
                        Panel_Streams.Children.RemoveAt(i);
                        break;
                    }
                }
                UpdateChannels();
                
                // Notify that streams have changed
                StreamsChanged?.Invoke();
            }
        }

        /// <summary>
        /// Disposes all streams and cleans up resources
        /// </summary>
        public void Dispose()
        {
            foreach (var ds in ConnectedDataStreams.ToList())
            {
                ds.StopStreaming();
                ds.Disconnect();
                ds.Dispose();
            }
            ConnectedDataStreams.Clear();
            foreach (var stream in ConfiguredDataStreams.ToList())
            {
                stream.PropertyChanged -= DataStreamViewModel_PropertyChanged;
            }
            ConfiguredDataStreams.Clear();
            Panel_Streams.Children.Clear();
            ChannelsChanged?.Invoke(0);
        }

        /// <summary>
        /// Handles property changes in DataStreamViewModel to update channels when needed
        /// </summary>
        private void DataStreamViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // Update channels when NumberOfChannels changes
            if (e.PropertyName == nameof(StreamSettings.NumberOfChannels))
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
            foreach(var vm in ConfiguredDataStreams)
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
            foreach (var vm in ConfiguredDataStreams)
            {
                totalChannels = totalChannels + vm.NumberOfChannels;
            }
            return totalChannels;
        }
    }
}
