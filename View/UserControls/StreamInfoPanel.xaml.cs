using System;
using System.ComponentModel;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
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

            Unloaded += StreamInfoPanel_Unloaded;

            this.AssociatedDataStream = dataStream;
            _associatedStreamSettings = streamSettings;

            // DataContext = the concrete stream object so bindings can reach runtime properties
            DataContext = dataStream;

            UpdatePortAndBaudDisplay();
            UpdateUsageLabelForStreamType();

            if (dataStream is USBDataStream)
            {
                // USB: model owns all metric calculations and fires OnPropertyChanged.
                // Bind directly — no polling timer needed.
                SetupUsbBindings();
            }
            else
            {
                // Serial / Audio / Demo / File: use a 1-second polling timer to compute
                // port-usage % (delta of TotalBits / baud rate) and refresh the UI.
                _updateTimer = new System.Timers.Timer(1000) { AutoReset = true };
                _updateTimer.Elapsed += UpdatePortStatistics;
                _updateTimer.Start();
            }

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
                else if (_associatedStreamSettings.StreamSource == Model.StreamSource.USB)
                {
                    string deviceName = _associatedStreamSettings.UsbSelectedDevice ?? "FX2G3 PowerScope";
                    displayText = $"USB: {deviceName}";
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

        private void UpdateUsageLabelForStreamType()
        {
            TextBlock usageLabel = this.FindName("UsageLabelTextBlock") as TextBlock;
            if (usageLabel == null)
                return;

            usageLabel.Text = AssociatedDataStream.StreamType == "USB" ? "Throughput:" : "Usage:";
        }

        /// <summary>
        /// For USB streams: wires the three metric TextBlocks directly to the model's
        /// <see cref="USBDataStream.SampleRate"/>, <see cref="USBDataStream.ThroughputKBps"/>,
        /// and <see cref="USBDataStream.StatusMessage"/> properties via WPF one-way bindings.
        /// The model fires <see cref="INotifyPropertyChanged"/> whenever values change, so no
        /// polling timer is needed on the view side.
        /// </summary>
        private void SetupUsbBindings()
        {
            // Samples/s  — e.g. "27,500"
            SamplesPerSecondTextBlock.SetBinding(
                TextBlock.TextProperty,
                new Binding(nameof(USBDataStream.SampleRate))
                {
                    StringFormat = "N0",
                    Mode = BindingMode.OneWay
                });

            // Throughput  — e.g. "440 KB/s"
            PortUsageTextBlock.SetBinding(
                TextBlock.TextProperty,
                new Binding(nameof(USBDataStream.ThroughputKBps))
                {
                    StringFormat = "{0:F0} KB/s",
                    Mode = BindingMode.OneWay
                });

            // Status  — "Running", "Stopped", "Err: …", etc.
            StatusTextBlock.SetBinding(
                TextBlock.TextProperty,
                new Binding(nameof(USBDataStream.StatusMessage))
                {
                    Mode = BindingMode.OneWay
                });
        }

        private void DataStream_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // Refresh the connect/disconnect button whenever the connection state changes
            if (e.PropertyName == nameof(IDataStream.IsConnected) ||
                e.PropertyName == nameof(IDataStream.IsStreaming))
            {
                Application.Current?.Dispatcher?.BeginInvoke(UpdateButtonAppearance);
            }
            // Note: SampleRate / ThroughputKBps / StatusMessage for USB are handled by
            // WPF one-way bindings set up in SetupUsbBindings() — no manual TextBlock
            // updates are needed or wanted here (they would break the binding).
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
            _updateTimer?.Stop();
            _updateTimer?.Dispose();
            
            // Unsubscribe from property change events
            //if (AssociatedDataStream is INotifyPropertyChanged notifyPropertyChanged)
            //{
            //    notifyPropertyChanged.PropertyChanged -= DataStream_PropertyChanged;
            //}
        }

        // Called only for non-USB streams (serial, audio, demo, file).
        // USB telemetry is driven entirely by WPF bindings to USBDataStream properties.
        private void UpdatePortStatistics(object sender, ElapsedEventArgs e)
        {
            if (Application.Current == null)
                return;

            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                if (AssociatedDataStream.IsStreaming)
                {
                    long samplesPerSecond = (long)Math.Round(AssociatedDataStream.SampleRate);
                    SamplesPerSecondTextBlock.Text = samplesPerSecond.ToString("N0");

                    long currentBits = AssociatedDataStream.TotalBits;
                    long bitsPerSecond = currentBits - _prevBitsCount;
                    _prevBitsCount = currentBits;

                    if (AssociatedDataStream.StreamType == "Demo" || AssociatedDataStream.StreamType == "Audio")
                    {
                        PortUsageTextBlock.Text = "N/A";
                    }
                    else
                    {
                        double usagePct = _associatedStreamSettings?.Baud > 0
                            ? 100.0 * bitsPerSecond / _associatedStreamSettings.Baud
                            : 0;
                        PortUsageTextBlock.Text = $"{usagePct:F1}%";
                    }

                    StatusTextBlock.Text = "Running";
                }
                else
                {
                    SamplesPerSecondTextBlock.Text = "0";
                    PortUsageTextBlock.Text = "0%";
                    StatusTextBlock.Text = "–";
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
