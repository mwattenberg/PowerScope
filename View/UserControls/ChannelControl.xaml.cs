using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using SerialPlotDN_WPF.Model;
using SerialPlotDN_WPF.View.UserForms;

namespace SerialPlotDN_WPF.View.UserControls
{
    public class ColorToBrushConverter : IValueConverter
    {
        public static readonly ColorToBrushConverter Instance = new ColorToBrushConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Color color)
                return new SolidColorBrush(color);
            return new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is SolidColorBrush brush)
                return brush.Color;
            return Colors.Gray;
        }
    }

    public partial class ChannelControl : UserControl
    {
        private static readonly Color DisabledColor = Colors.Gray;
        private static readonly Brush SelectedBrush = new SolidColorBrush(Colors.LimeGreen);
        private static readonly Brush DefaultBrush = new SolidColorBrush(DisabledColor);

        /// <summary>
        /// Dependency property for MeasurementCommand
        /// </summary>
        public static readonly DependencyProperty MeasurementCommandProperty =
            DependencyProperty.Register("MeasurementCommand", typeof(ICommand), typeof(ChannelControl), new PropertyMetadata(null));

        /// <summary>
        /// Command to execute when measurement is requested (bound from ChannelControlBar)
        /// </summary>
        public ICommand MeasurementCommand
        {
            get { return (ICommand)GetValue(MeasurementCommandProperty); }
            set { SetValue(MeasurementCommandProperty, value); }
        }

        /// <summary>
        /// Gets the ChannelSettings from DataContext
        /// </summary>
        public ChannelSettings Settings 
        { 
            get 
            { 
                return DataContext as ChannelSettings; 
            } 
        }

        public ChannelControl()
        {
            InitializeComponent();
        }

        private void TopColorBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Toggle the enabled state
            if (Settings != null)
                Settings.IsEnabled = !Settings.IsEnabled;

            // Prevent the event from bubbling up to RootGrid
            e.Handled = true;
        }

        private void ButtonGainUp_Click(object sender, RoutedEventArgs e)
        {
            if (Settings != null)
                Settings.Gain = Settings.Gain * 2;
        }

        private void ButtonGainDown_Click(object sender, RoutedEventArgs e)
        {
            if (Settings != null)
                Settings.Gain = Settings.Gain / 2;
        }

        private void ButtonFilters_Click(object sender, RoutedEventArgs e)
        {
            if (Settings != null)
            {
                FilterConfigWindow filterWindow = new FilterConfigWindow();
                filterWindow.DataContext = Settings;
                filterWindow.Owner = Window.GetWindow(this);
                filterWindow.ShowDialog();
            }
        }

        private void ButtonMeasure_Click(object sender, RoutedEventArgs e)
        {
            // Use the command approach since it's properly bound via XAML DataTemplate
            // This will trigger the MeasurementSelection window through the command chain
            if (MeasurementCommand != null)
            {
                MeasurementCommand.Execute(Settings);
            }
        }

        // Input validation methods
        private void DecimalOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            var textBox = sender as TextBox;
            var newText = textBox.Text.Insert(textBox.SelectionStart, e.Text);
            e.Handled = !IsDecimal(newText);
        }

        private void SignedDecimalOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            var textBox = sender as TextBox;
            var newText = textBox.Text.Insert(textBox.SelectionStart, e.Text);
            e.Handled = !IsSignedDecimal(newText);
        }

        private static bool IsDecimal(string text)
        {
            return Regex.IsMatch(text, @"^[0-9]*\.?[0-9]*$") && text != "." && !text.EndsWith("..");
        }

        private static bool IsSignedDecimal(string text)
        {
            return Regex.IsMatch(text, @"^-?[0-9]*\.?[0-9]*$") && text != "-" && text != "." && !text.EndsWith("..");
        }
    }
}
