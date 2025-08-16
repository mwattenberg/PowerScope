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
        public DataStreamManager Manager { get; set; }

        public StreamInfoPanel()
        {
            InitializeComponent();
        }

        private void Button_Configure_Click(object sender, RoutedEventArgs e)
        {
            Window myWindow = new SerialConfigWindow();
            if (DataContext is DataStreamViewModel vm)
            {
                // Initialize the window with the current DataContext
                vm.ApplyToWindow((SerialConfigWindow)myWindow);
                
            }
            myWindow.ShowDialog();
        }

        private void Button_Connect_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is DataStreamViewModel vm && Manager != null)
            {
                if (vm.IsConnected)
                {
                    Manager.Disconnect(vm);
                    Button_Connect.Content = "Connect";
                    Button_Connect.Background = new SolidColorBrush(Colors.LightGreen);
                }
                else
                {
                    Manager.Connect(vm);
                    Button_Connect.Content = "Disconnect";
                    Button_Connect.Background = new SolidColorBrush(Colors.OrangeRed);   
                }
            }
        }
    }
}
