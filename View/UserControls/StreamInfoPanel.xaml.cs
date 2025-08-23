using System;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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

        private readonly System.Timers.Timer _updateTimer;
        private long _prevSampleCount = 0;
        private long _prevBitsCount = 0;

        public StreamInfoPanel()
        {
            InitializeComponent();
            
            // Initialize timer for updating port usage and samples
            _updateTimer = new System.Timers.Timer(1000) { AutoReset = true };
            _updateTimer.Elapsed += UpdatePortStatistics;
            _updateTimer.Start();
            
            // Handle unloaded event to clean up timer
            Unloaded += StreamInfoPanel_Unloaded;
            
            // Subscribe to DataContext changes to handle property change notifications
            DataContextChanged += StreamInfoPanel_DataContextChanged;
        }

        private void StreamInfoPanel_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // Unsubscribe from old DataContext if it exists
            if (e.OldValue is DataStreamViewModel oldVm)
            {
                oldVm.PropertyChanged -= ViewModel_PropertyChanged;
            }
            
            // Subscribe to new DataContext if it exists
            if (e.NewValue is DataStreamViewModel newVm)
            {
                newVm.PropertyChanged += ViewModel_PropertyChanged;
                UpdateButtonAppearance(); // Update button immediately
            }
        }

        private void ViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DataStreamViewModel.IsConnected))
            {
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    UpdateButtonAppearance();
                });
            }
        }

        private void UpdateButtonAppearance()
        {
            if (DataContext is DataStreamViewModel vm)
            {
                // Update button content
                Button_Connect.Content = vm.IsConnected ? "Disconnect" : "Connect";
                
                // Update button background
                Button_Connect.Background = vm.IsConnected ? 
                    new SolidColorBrush(Colors.OrangeRed) : 
                    new SolidColorBrush(Colors.LimeGreen);
            }
        }

        private void StreamInfoPanel_Unloaded(object sender, RoutedEventArgs e)
        {
            _updateTimer?.Stop();
            _updateTimer?.Dispose();
            
            // Unsubscribe from property changes
            if (DataContext is DataStreamViewModel vm)
            {
                vm.PropertyChanged -= ViewModel_PropertyChanged;
            }
        }

        private void UpdatePortStatistics(object sender, ElapsedEventArgs e)
        {
            if(Application.Current == null) 
                return;

            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                if (DataContext is DataStreamViewModel vm && vm.IsConnected && vm.SerialDataStream != null)
                {
                    try
                    {
                        var dataStream = vm.SerialDataStream;
                        
                        // Calculate samples per second
                        long currentSamples = dataStream.TotalSamples;
                        long samplesPerSecond = currentSamples - _prevSampleCount;
                        _prevSampleCount = currentSamples;
                        
                        // Calculate bits per second and port usage percentage
                        long currentBits = dataStream.TotalBits;
                        long bitsPerSecond = currentBits - _prevBitsCount;
                        _prevBitsCount = currentBits;
                        
                        double portUsagePercent = vm.Baud > 0 ? (double)bitsPerSecond / vm.Baud * 100.0 : 0.0;
                        
                        // Update UI
                        SamplesPerSecondTextBlock.Text = samplesPerSecond.ToString();
                        PortUsageTextBlock.Text = $"{portUsagePercent:F1}%";
                    }
                    catch
                    {
                        // Reset display on error
                        SamplesPerSecondTextBlock.Text = "0";
                        PortUsageTextBlock.Text = "0%";
                    }
                }
                else
                {
                    // Reset counters and display when disconnected
                    _prevSampleCount = 0;
                    _prevBitsCount = 0;
                    SamplesPerSecondTextBlock.Text = "0";
                    PortUsageTextBlock.Text = "0%";
                }
            });
        }

        private void Button_Configure_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is DataStreamViewModel vm)
            {
                // Disconnect the serial stream before configuring
                if (vm.IsConnected)
                {
                    vm.Disconnect();
                }

                var configWindow = new SerialConfigWindow(vm);
                
                if (configWindow.ShowDialog() == true)
                    vm.Connect();
            }
        }

        private void Button_Connect_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is DataStreamViewModel vm)
            {
                if (vm.IsConnected)
                {
                    // Disconnect
                    vm.Disconnect();
                }
                else
                {
                    // Connect
                    vm.Connect();
                }
                
                // Fire the event for any external handlers
                OnConnectClickedEvent?.Invoke(this, EventArgs.Empty);
            }
        }

        private void Button_Close_Click(object sender, RoutedEventArgs e)
        {
            //if (DataContext is DataStreamViewModel vm)
            //{
            //    vm.Disconnect();
            //    vm.Dispose();
            //}
            OnRemoveClickedEvent?.Invoke(this, EventArgs.Empty);
        }
    }
}
