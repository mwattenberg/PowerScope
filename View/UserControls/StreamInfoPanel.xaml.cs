using System;
using System.ComponentModel;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ScottPlot.Plottables;
using PowerScope.Model;
using System.IO;

namespace PowerScope.View.UserControls
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
        private long _prevBitsCount = 0; // Removed _prevSampleCount since we now use the stream's SampleRate property

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
                else if (_associatedStreamSettings.StreamSource == Model.StreamSource.AudioInput)
                {
                    displayText = $"Audio: {_associatedStreamSettings.AudioDevice ?? "Default Device"}";
                }
                else if (_associatedStreamSettings.StreamSource == Model.StreamSource.File)
                {
                    displayText = $"{Path.GetFileName(_associatedStreamSettings.FilePath)}";
                }
                else if (_associatedStreamSettings.StreamSource == Model.StreamSource.FTDI)
                {
                    displayText = $"FTDI: {_associatedStreamSettings.FtdiSelectedDevice ?? "Unknown Device"}";
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

            // Handle sample rate changes for immediate UI updates (especially useful for serial streams)
            if (e.PropertyName == nameof(IDataStream.SampleRate))
            {
                // Trigger an immediate UI update for sample rate display
                Application.Current?.Dispatcher?.BeginInvoke(() =>
                {
                    if (AssociatedDataStream.IsStreaming)
                    {
                        double streamSampleRate = AssociatedDataStream.SampleRate;
                        long samplesPerSecond = (long)Math.Round(streamSampleRate);
                        SamplesPerSecondTextBlock.Text = samplesPerSecond.ToString();
                    }
                });
            }

        }

        private void UpdateButtonAppearance()
        {
            if (AssociatedDataStream.IsConnected || AssociatedDataStream.IsStreaming)
            {
                Button_Connect.Foreground = new SolidColorBrush(Colors.OrangeRed);
                Button_Connect.Content = "⏸️";
                Button_Connect.ToolTip = "Disconnect";
            }
            else
            {
                Button_Connect.Foreground = new SolidColorBrush(Colors.LimeGreen);
                Button_Connect.Content = "▶️";
                Button_Connect.ToolTip = "Connect";
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
                    // Use the data stream's built-in sample rate calculation
                    // This provides better accuracy and consistency across different stream types
                    double streamSampleRate = AssociatedDataStream.SampleRate;
                    long samplesPerSecond = (long)Math.Round(streamSampleRate);

                    // Calculate bits per second and port usage percentage
                    long currentBits = AssociatedDataStream.TotalBits;
                    long bitsPerSecond = currentBits - _prevBitsCount;
                    _prevBitsCount = currentBits;

                    double portUsagePercent;
                    if (AssociatedDataStream.StreamType == "Demo" || AssociatedDataStream.StreamType == "Audio")
                    {
                        // For demo and audio streams, show a different metric or just show N/A
                        portUsagePercent = 0; // Demo and Audio don't have port usage
                    }
                    else
                    {
                        // For serial streams, calculate based on baud rate
                        // Get baud rate from associated stream settings
                        portUsagePercent = _associatedStreamSettings?.Baud > 0 ? 
                            (100.0 * bitsPerSecond / _associatedStreamSettings.Baud) : 0;
                    }

                    // Update UI with the stream's calculated sample rate
                    SamplesPerSecondTextBlock.Text = samplesPerSecond.ToString();
                    if (AssociatedDataStream.StreamType == "Demo" || AssociatedDataStream.StreamType == "Audio")
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
                    _prevBitsCount = 0;
                }
            });
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
