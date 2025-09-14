using System;
using System.Windows;
using System.Windows.Controls;
using PowerScope.Model;

namespace PowerScope.View.UserControls
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
