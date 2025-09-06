using System;
using System.Windows;
using System.Windows.Controls;

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

        private void Button_Remove_Click(object sender, RoutedEventArgs e)
        {
            OnRemoveClickedEvent?.Invoke(this, EventArgs.Empty);
        }
    }
}
