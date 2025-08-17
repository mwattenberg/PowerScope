using System.Collections.Generic;
using System.Linq;
using SerialPlotDN_WPF.Model;
using SerialPlotDN_WPF.View.UserForms;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SerialPlotDN_WPF.View.UserControls
{
    /// <summary>
    /// Interaction logic for DataStreamBar.xaml
    /// </summary>

    public partial class DataStreamBar : UserControl
    {
        public List<DataStreamManager> DataStreamManagers { get; private set; } = new();

        public bool IsRunning { get; set; }

        public DataStreamBar()
        {
            InitializeComponent();
            // No need to initialize a single DataStreamManager
            //ItemsControl_Streams.ItemsSource = DataStreamManagers.SelectMany(m => m.StreamViewModels);
        }

        private void Button_AddStream_Click(object sender, RoutedEventArgs e)
        {
            var configWindow = new SerialConfigWindow();
            
            if (configWindow.ShowDialog() == true)
            {
                var manager = new DataStreamManager();
                DataStreamManagers.Add(manager);
                var vm = manager.AddStream(configWindow.SelectedPort, configWindow.SelectedBaud);

                var panel = new StreamInfoPanel
                {
                    DataContext = vm,
                    Manager = manager
                };

                Panel_Streams.Children.Add(panel);
            }
        }
    }
}
