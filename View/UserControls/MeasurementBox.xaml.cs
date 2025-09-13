using System;
using System.Windows;
using System.Windows.Controls;
using SerialPlotDN_WPF.Model;

namespace SerialPlotDN_WPF.View.UserControls
{
    /// <summary>
    /// Interaction logic for MeasurementBox.xaml
    /// Now works directly with Measurement objects via DataTemplate
    /// </summary>
    public partial class MeasurementBox : UserControl
    {
        /// <summary>
        /// Gets the Measurement from DataContext
        /// </summary>
        public Measurement Measurement => DataContext as Measurement;

        public MeasurementBox()
        {
            InitializeComponent();
        }

        private void Button_Remove_Click(object sender, RoutedEventArgs e)
        {
            // Request removal through the Measurement object
            Measurement?.RequestRemove();
        }
    }
}
