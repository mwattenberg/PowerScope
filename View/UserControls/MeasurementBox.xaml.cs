using System;
using System.Windows;
using System.Windows.Controls;
using SerialPlotDN_WPF.Model;

namespace SerialPlotDN_WPF.View.UserControls
{
    /// <summary>
    /// Interaction logic for MeasurementBox.xaml
    /// </summary>
    public partial class MeasurementBox : UserControl
    {
        public delegate void OnRemoveClicked(object sender, EventArgs e);
        public event OnRemoveClicked OnRemoveClickedEvent;

        public MeasurementBox()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Set the measurement object - sets up data binding and updates the measurement type label
        /// </summary>
        /// <param name="measurement">The measurement object to bind to</param>
        public void SetMeasurement(Measurement measurement)
        {
            if (measurement != null)
            {
                // Set up data binding for the result value - direct access to named control
                var resultBinding = new System.Windows.Data.Binding("Result")
                {
                    Source = measurement,
                    StringFormat = "F3"
                };
                ResultTextBlock.SetBinding(TextBlock.TextProperty, resultBinding);
                
                // Set the measurement type label - direct access to named control
                MeasurementTypeTextBlock.Text = measurement.Type.ToString();
            }
        }

        private void Button_Remove_Click(object sender, RoutedEventArgs e)
        {
            OnRemoveClickedEvent?.Invoke(this, EventArgs.Empty);
        }
    }
}
