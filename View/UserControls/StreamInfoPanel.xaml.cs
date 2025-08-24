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
            if (e.OldValue is StreamSettings oldVm)
            {
                oldVm.PropertyChanged -= ViewModel_PropertyChanged;
            }
            
            // Subscribe to new DataContext if it exists
            if (e.NewValue is StreamSettings newVm)
            {
                newVm.PropertyChanged += ViewModel_PropertyChanged;
                UpdateButtonAppearance(); // Update button immediately
            }
        }

        private void ViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // Only update UI for configuration changes, not connection state
            UpdateButtonAppearance();
        }

        private void UpdateButtonAppearance()
        {
            // This should be updated to use IDataStream state, not StreamSettings
            // You will need to pass the IDataStream reference to the panel, or bind to its state
            Button_Connect.Content = "Connect";
            Button_Connect.Background = new SolidColorBrush(Colors.LimeGreen);
        }

        private void StreamInfoPanel_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_updateTimer != null)
            {
                _updateTimer.Stop();
                _updateTimer.Dispose();
            }
            
            // Unsubscribe from property changes
            if (DataContext is StreamSettings vm)
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
                // This should be updated to use IDataStream reference, not StreamSettings
                SamplesPerSecondTextBlock.Text = "0";
                PortUsageTextBlock.Text = "0%";
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
            // Connection logic should be handled in DataStreamBar, not here
            if (OnConnectClickedEvent != null)
                OnConnectClickedEvent.Invoke(this, EventArgs.Empty);
        }

        private void Button_Close_Click(object sender, RoutedEventArgs e)
        {
            //if (DataContext is DataStreamViewModel vm)
            //{
            //    vm.Disconnect();
            //    vm.Dispose();
            //}
            if (OnRemoveClickedEvent != null)
                OnRemoveClickedEvent.Invoke(this, EventArgs.Empty);
        }
    }
}
