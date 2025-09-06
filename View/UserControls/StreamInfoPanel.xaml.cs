using System;
using System.ComponentModel;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ScottPlot.Plottables;
using SerialPlotDN_WPF.Model;
using SerialPlotDN_WPF.View.UserForms;

namespace SerialPlotDN_WPF.View.UserControls
{
    public partial class StreamInfoPanel : UserControl
    {
        //public delegate void OnConnectedClicked(object sender, EventArgs e);
        //public event OnConnectedClicked OnConnectClickedEvent;

        public delegate void OnRemoveClicked(object sender, EventArgs e);
        public event OnRemoveClicked OnRemoveClickedEvent;

        public IDataStream AssociatedDataStream { get; set; }
        public StreamSettings AssociatedStreamSettings => _associatedStreamSettings;
        private StreamSettings _associatedStreamSettings;

        private readonly System.Timers.Timer _updateTimer;
        private long _prevSampleCount = 0;
        private long _prevBitsCount = 0;

        public StreamInfoPanel(IDataStream dataStream, StreamSettings streamSettings)
        {
            InitializeComponent();
            
            // Initialize timer for updating port usage and samples
            _updateTimer = new System.Timers.Timer(1000) { AutoReset = true };
            _updateTimer.Elapsed += UpdatePortStatistics;
            _updateTimer.Start();
            
            // Handle unloaded event to clean up timer
            Unloaded += StreamInfoPanel_Unloaded;
           
            this.AssociatedDataStream = dataStream;
            _associatedStreamSettings = streamSettings;

            // Set the DataStream as DataContext for direct binding
            DataContext = dataStream;
            
            // Set the port and baud display manually since we changed DataContext
            UpdatePortAndBaudDisplay();

            //// Subscribe to property changes if the data stream supports it
            if (dataStream is INotifyPropertyChanged notifyPropertyChanged)
            {
                notifyPropertyChanged.PropertyChanged += DataStream_PropertyChanged;
            }

            UpdateButtonAppearance(); // Initial update
        }

        private void UpdatePortAndBaudDisplay()
        {
            if (_associatedStreamSettings != null)
            {
                string displayText;
                if (_associatedStreamSettings.StreamSource == Model.StreamSource.Demo)
                {
                    displayText = $"Demo ({_associatedStreamSettings.DemoSignalType})";
                }
                else if (_associatedStreamSettings.StreamSource == Model.StreamSource.SerialPort)
                {
                    displayText = $"{_associatedStreamSettings.Port} @ {_associatedStreamSettings.Baud}";
                }
                else
                {
                    displayText = "Unknown";
                }
                
                // Find the TextBlock by name and set its text
                var portBaudTextBlock = this.FindName("PortBaudTextBlock") as TextBlock;
                if (portBaudTextBlock != null)
                {
                    portBaudTextBlock.Text = displayText;
                }
            }
        }

        private void DataStream_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // Update UI when connection state changes
            if (e.PropertyName == nameof(IDataStream.IsConnected) ||
                e.PropertyName == nameof(IDataStream.IsStreaming))
            {
                UpdateButtonAppearance();
            }

            // Handle status message changes to provide user feedback about disconnections
            if (e.PropertyName == nameof(IDataStream.StatusMessage))
            {
                // If the status message indicates an error or disconnection, we could show it
                // For now, we'll let the existing timer-based UI handle most status updates
                // but this provides a hook for future status message display

                // Example: If we wanted to show error messages immediately:
                // if (AssociatedDataStream.StatusMessage?.Contains("Disconnected:") == true ||
                //     AssociatedDataStream.StatusMessage?.Contains("Error") == true)
                // {
                //     // Could show a tooltip or update a status indicator here
                // }
            }
        }

        private void UpdateButtonAppearance()
        {
            if (AssociatedDataStream.IsConnected || AssociatedDataStream.IsStreaming)
            {
                Button_Connect.Background = new SolidColorBrush(Colors.OrangeRed);
                Button_Connect.Content = "Disconnect";
            }
            else
            {
                Button_Connect.Background = new SolidColorBrush(Colors.LimeGreen);
                Button_Connect.Content = "Connect";
            }
        }

        private void StreamInfoPanel_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_updateTimer != null)
            {
                _updateTimer.Stop();
                _updateTimer.Dispose();
            }
            
            // Unsubscribe from property change events
            //if (AssociatedDataStream is INotifyPropertyChanged notifyPropertyChanged)
            //{
            //    notifyPropertyChanged.PropertyChanged -= DataStream_PropertyChanged;
            //}
        }

        private void UpdatePortStatistics(object sender, ElapsedEventArgs e)
        {
            if (Application.Current == null)
                return;

            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                if (AssociatedDataStream.IsStreaming)
                {
                    // Calculate samples per second
                    long currentSamples = AssociatedDataStream.TotalSamples;
                    long samplesPerSecond = currentSamples - _prevSampleCount;
                    _prevSampleCount = currentSamples;

                    // Calculate bits per second and port usage percentage
                    long currentBits = AssociatedDataStream.TotalBits;
                    long bitsPerSecond = currentBits - _prevBitsCount;
                    _prevBitsCount = currentBits;

                    double portUsagePercent;
                    if (AssociatedDataStream.StreamType == "Demo")
                    {
                        // For demo streams, show a different metric or just show N/A
                        portUsagePercent = 0; // Demo doesn't have port usage
                    }
                    else
                    {
                        // For serial streams, calculate based on baud rate
                        // Get baud rate from associated stream settings
                        portUsagePercent = _associatedStreamSettings?.Baud > 0 ? 
                            (100.0 * bitsPerSecond / _associatedStreamSettings.Baud) : 0;
                    }

                    // Update UI
                    SamplesPerSecondTextBlock.Text = samplesPerSecond.ToString();
                    if (AssociatedDataStream.StreamType == "Demo")
                    {
                        PortUsageTextBlock.Text = "N/A";
                    }
                    else
                    {
                        PortUsageTextBlock.Text = $"{portUsagePercent:F1}%";
                    }
                }
                else
                {
                    SamplesPerSecondTextBlock.Text = "0";
                    PortUsageTextBlock.Text = "0%";
                    _prevSampleCount = 0;
                    _prevBitsCount = 0;
                }
            });
        }

        private void Button_Configure_Click(object sender, RoutedEventArgs e)
        {
            if (_associatedStreamSettings != null)
            {
                SerialConfigWindow configWindow = new SerialConfigWindow(_associatedStreamSettings);
                configWindow.ShowDialog();
            }
        }

        private void Button_Connect_Click(object sender, RoutedEventArgs e)
        {
            if (AssociatedDataStream.IsConnected)
            {
                AssociatedDataStream.StopStreaming();
                AssociatedDataStream.Disconnect();   
            }
            else
            {
                AssociatedDataStream.Connect();
                AssociatedDataStream.StartStreaming();
            }
        }

        private void Button_Close_Click(object sender, RoutedEventArgs e)
        {
            AssociatedDataStream.StopStreaming();
            AssociatedDataStream.Disconnect();

            if (OnRemoveClickedEvent != null)
                OnRemoveClickedEvent.Invoke(this, EventArgs.Empty);
        }
    }
}
