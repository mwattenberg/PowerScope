using System;
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
        public delegate void OnConnectedClicked(object sender, EventArgs e);
        public event OnConnectedClicked OnConnectClickedEvent;

        public delegate void OnRemoveClicked(object sender, EventArgs e);
        public event OnRemoveClicked OnRemoveClickedEvent;
        public IDataStream AssociatedDataStream { get; set; }
        private StreamSettings _viewModel;

        private readonly System.Timers.Timer _updateTimer;
        private long _prevSampleCount = 0;
        private long _prevBitsCount = 0;

        public StreamInfoPanel(IDataStream associatedDataStream, StreamSettings vm)
        {
            InitializeComponent();
            
            // Initialize timer for updating port usage and samples
            _updateTimer = new System.Timers.Timer(1000) { AutoReset = true };
            _updateTimer.Elapsed += UpdatePortStatistics;
            _updateTimer.Start();
            
            // Handle unloaded event to clean up timer
            Unloaded += StreamInfoPanel_Unloaded;
           
            this.AssociatedDataStream = associatedDataStream;
            _viewModel = vm;

            DataContext = vm;
            vm.PropertyChanged += ViewModel_PropertyChanged;
            UpdateButtonAppearance(); // Initial update

        }

        private void ViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // Only update UI for configuration changes, not connection state
            UpdateButtonAppearance();
        }

        private void UpdateButtonAppearance()
        {
            if (AssociatedDataStream.IsConnected)
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

                    double portUsagePercent = (100.0 * bitsPerSecond / _viewModel.Baud);

                    // Update UI
                    SamplesPerSecondTextBlock.Text = samplesPerSecond.ToString();
                    PortUsageTextBlock.Text = $"{portUsagePercent:F1}%";
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
            if (DataContext is StreamSettings vm)
            {
                SerialConfigWindow configWindow = new SerialConfigWindow(vm);
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

            UpdateButtonAppearance();
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
