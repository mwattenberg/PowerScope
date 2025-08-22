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
        public List<DataStreamViewModel> _dataStreamModels = new List<DataStreamViewModel>();

        public DataStreamBar()
        {
            InitializeComponent();
            // No need to initialize a single DataStreamManager
            //ItemsControl_Streams.ItemsSource = DataStreamManagers.SelectMany(m => m.StreamViewModels);
        }

        private void Button_AddStream_Click(object sender, RoutedEventArgs e)
        {
            var vm = new DataStreamViewModel();
            var configWindow = new SerialConfigWindow(vm);
            if (configWindow.ShowDialog() == true)
            {
                _dataStreamModels.Add(vm);

                
                var panel = new StreamInfoPanel
                {
                    DataContext = vm,
                    
                };
                Panel_Streams.Children.Add(panel);
            }
        }
    }
}
