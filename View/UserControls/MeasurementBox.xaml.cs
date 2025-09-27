using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media.Animation;
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
    /// Enhanced with expandable details section
    /// </summary>
    public partial class MeasurementBox : UserControl
    {
        private bool _isExpanded = false;

        /// <summary>
        /// Gets the Measurement from DataContext
        /// </summary>
        public Measurement Measurement => DataContext as Measurement;

        public MeasurementBox()
        {
            InitializeComponent();
            DataContextChanged += MeasurementBox_DataContextChanged;
        }

        private void MeasurementBox_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // When the DataContext changes, we need to enable/disable statistics tracking
            if (e.OldValue is Measurement oldMeasurement)
            {
                oldMeasurement.CalculateStatistics = false;
            }
            if (e.NewValue is Measurement newMeasurement)
            {
                newMeasurement.CalculateStatistics = _isExpanded;
                UpdateDetailLabels();
            }
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

        private void Button_Expand_Click(object sender, RoutedEventArgs e)
        {
            ToggleExpansion();
        }

        private void Button_Clear_Click(object sender, RoutedEventArgs e)
        {
            // Clear measurement history/statistics
            Measurement?.ClearStatistics();
        }

        private void ToggleExpansion()
        {
            _isExpanded = !_isExpanded;
            
            // Enable or disable statistics calculation based on the expanded state
            if (Measurement != null)
            {
                Measurement.CalculateStatistics = _isExpanded;
            }

            if (_isExpanded)
            {
                // Expand
                ExpandButton.Content = "▲";
                ExpandButton.ToolTip = "Hide details";
                DetailsPanel.Visibility = Visibility.Visible;
                
                UpdateDetailLabels();
                
                // Simple animation using DoubleAnimation on Height
                var heightAnimation = new DoubleAnimation(0, 80, TimeSpan.FromMilliseconds(200));
                heightAnimation.Completed += (s, e) =>
                {
                    DetailsRow.Height = new GridLength(80, GridUnitType.Pixel);
                };
                
                // Set initial height for animation
                DetailsRow.Height = new GridLength(0);
                DetailsPanel.BeginAnimation(FrameworkElement.HeightProperty, heightAnimation);
            }
            else
            {
                // Collapse
                ExpandButton.Content = "▼";
                ExpandButton.ToolTip = "Show details";
                
                // Simple animation using DoubleAnimation on Height
                var heightAnimation = new DoubleAnimation(80, 0, TimeSpan.FromMilliseconds(200));
                heightAnimation.Completed += (s, e) =>
                {
                    DetailsPanel.Visibility = Visibility.Collapsed;
                    DetailsRow.Height = new GridLength(0);
                };
                
                DetailsPanel.BeginAnimation(FrameworkElement.HeightProperty, heightAnimation);
            }
        }

        private void UpdateDetailLabels()
        {
            if (Measurement == null) return;

            switch (Measurement.Type)
            {
                case MeasurementType.FFT:
                    Detail1Label.Text = "Peak 1:";
                    Detail2Label.Text = "Peak 2:";
                    Detail3Label.Text = "Peak 3:";
                    Detail4Label.Text = "Peak 4:";
                    break;
                    
                default: // RMS and other measurements
                    Detail1Label.Text = "Min:";
                    Detail2Label.Text = "Max:";
                    Detail3Label.Text = "Mean:";
                    Detail4Label.Text = "Count:";
                    break;
            }
        }
    }
}
