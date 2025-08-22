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


        public StreamInfoPanel()
        {
            InitializeComponent();
        }

        private void Button_Configure_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is DataStreamViewModel vm)
            {
                var configWindow = new SerialConfigWindow(vm);
                configWindow.ShowDialog();
            }
        }

        private void Button_Connect_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is DataStreamViewModel vm)
            {
                if (vm.IsConnected)
                {

                    Button_Connect.Content = "Connect";
                    Button_Connect.Background = new SolidColorBrush(Colors.LightGreen);
                }
                else
                {
                    
                    Button_Connect.Content = "Disconnect";
                    Button_Connect.Background = new SolidColorBrush(Colors.OrangeRed);   
                }
            }
        }
    }
}
