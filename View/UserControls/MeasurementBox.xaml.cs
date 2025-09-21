using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using PowerScope.Model;

namespace PowerScope.View.UserControls
{
    /// <summary>
    /// Converter to show spectrum button only for FFT measurements
    /// </summary>
    public class FFTVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is MeasurementType measurementType)
            {
                return measurementType == MeasurementType.FFT ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

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

        private void Button_Spectrum_Click(object sender, RoutedEventArgs e)
        {
            // Show spectrum window for FFT measurements
            Measurement?.FFT_ShowSpectrumWindow();
        }
    }
}
