using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PowerScope.Model;

namespace PowerScope.View.UserForms
{
    /// <summary>
    /// Interaction logic for MeasurementSelection.xaml
    /// </summary>
    public partial class MeasurementSelection : Window
    {
        /// <summary>
        /// The selected measurement type, or null if dialog was cancelled
        /// </summary>
        public MeasurementType? SelectedMeasurementType { get; private set; }

        public MeasurementSelection()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Handle measurement button clicks
        /// </summary>
        private void MeasurementButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string measurementTypeString)
            {
                // Parse the measurement type from the button's Tag
                if (Enum.TryParse<MeasurementType>(measurementTypeString, out var measurementType))
                {
                    SelectedMeasurementType = measurementType;
                    DialogResult = true;
                    Close();
                }
            }
        }

        /// <summary>
        /// Handle close button click
        /// </summary>
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedMeasurementType = null;
            DialogResult = false;
            Close();
        }

        /// <summary>
        /// Handle title bar drag
        /// </summary>
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }
    }
}
