using System;
using System.Windows;
using System.Windows.Input;
using System.Text.RegularExpressions;
using SerialPlotDN_WPF.Model;

namespace SerialPlotDN_WPF.View.UserForms
{
    /// <summary>
    /// Interaction logic for PlotSettingsWindow.xaml
    /// </summary>
    public partial class PlotSettingsWindow : Window
    {
        /// <summary>
        /// PlotSettings instance used as DataContext - direct reference, no cloning
        /// </summary>
        public PlotSettings Settings => DataContext as PlotSettings;

        public PlotSettingsWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Constructor that accepts existing PlotSettings to edit directly
        /// </summary>
        /// <param name="existingSettings">Existing settings to edit directly</param>
        public PlotSettingsWindow(PlotSettings existingSettings)
        {
            InitializeComponent();
            DataContext = existingSettings;
        }

        // Input validation handlers for numeric-only fields
        private void NumbersOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !IsNumeric(e.Text);
        }

        private void DecimalOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            var textBox = sender as System.Windows.Controls.TextBox;
            var newText = textBox.Text.Insert(textBox.SelectionStart, e.Text);
            e.Handled = !IsDecimal(newText);
        }

        private void SignedNumbersOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            var textBox = sender as System.Windows.Controls.TextBox;
            var newText = textBox.Text.Insert(textBox.SelectionStart, e.Text);
            e.Handled = !IsSignedNumeric(newText);
        }

        private static bool IsNumeric(string text)
        {
            return Regex.IsMatch(text, @"^[0-9]+$");
        }

        private static bool IsDecimal(string text)
        {
            return Regex.IsMatch(text, @"^[0-9]*\.?[0-9]*$") && text != "." && !text.EndsWith("..");
        }

        private static bool IsSignedNumeric(string text)
        {
            return Regex.IsMatch(text, @"^-?[0-9]*$") && text != "-";
        }

        // Custom window event handlers
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // Plot FPS Up/Down button handlers
        private void ButtonPlotFPSUp_Click(object sender, RoutedEventArgs e)
        {
            if (Settings != null)
                Settings.PlotUpdateRateFPS = Math.Min(120, Settings.PlotUpdateRateFPS + 0.1);
        }

        private void ButtonPlotFPSDown_Click(object sender, RoutedEventArgs e)
        {
            if (Settings != null)
                Settings.PlotUpdateRateFPS = Math.Max(1, Settings.PlotUpdateRateFPS - 0.1);
        }

        // Serial Hz Up/Down button handlers
        private void ButtonSerialHzUp_Click(object sender, RoutedEventArgs e)
        {
            if (Settings != null)
                Settings.SerialPortUpdateRateHz = Math.Min(10000, Settings.SerialPortUpdateRateHz + 100);
        }

        private void ButtonSerialHzDown_Click(object sender, RoutedEventArgs e)
        {
            if (Settings != null)
                Settings.SerialPortUpdateRateHz = Math.Max(1, Settings.SerialPortUpdateRateHz - 100);
        }

        // Line Width Up/Down button handlers
        private void ButtonLineWidthUp_Click(object sender, RoutedEventArgs e)
        {
            if (Settings != null)
                Settings.LineWidth = Math.Min(10, Settings.LineWidth + 1);
        }

        private void ButtonLineWidthDown_Click(object sender, RoutedEventArgs e)
        {
            if (Settings != null)
                Settings.LineWidth = Math.Max(1, Settings.LineWidth - 1);
        }

        // X Min Up/Down button handlers
        private void ButtonXMinUp_Click(object sender, RoutedEventArgs e)
        {
            if (Settings != null)
                Settings.Xmin = Math.Min(Settings.Xmax - 1, Settings.Xmin + 100);
        }

        private void ButtonXMinDown_Click(object sender, RoutedEventArgs e)
        {
            if (Settings != null)
                Settings.Xmin = Math.Max(0, Settings.Xmin - 100);
        }

        // X Max Up/Down button handlers
        private void ButtonXMaxUp_Click(object sender, RoutedEventArgs e)
        {
            if (Settings != null)
                Settings.Xmax = Math.Min(100000, Settings.Xmax + 100);
        }

        private void ButtonXMaxDown_Click(object sender, RoutedEventArgs e)
        {
            if (Settings != null)
                Settings.Xmax = Math.Max(Settings.Xmin + 1, Settings.Xmax - 100);
        }

        // Y Min Up/Down button handlers
        private void ButtonYMinUp_Click(object sender, RoutedEventArgs e)
        {
            if (Settings != null)
                Settings.Ymin = Math.Min(Settings.Ymax - 1, Settings.Ymin + 100);
        }

        private void ButtonYMinDown_Click(object sender, RoutedEventArgs e)
        {
            if (Settings != null)
                Settings.Ymin = Settings.Ymin - 100;
        }

        // Y Max Up/Down button handlers
        private void ButtonYMaxUp_Click(object sender, RoutedEventArgs e)
        {
            if (Settings != null)
                Settings.Ymax = Settings.Ymax + 100;
        }

        private void ButtonYMaxDown_Click(object sender, RoutedEventArgs e)
        {
            if (Settings != null)
                Settings.Ymax = Math.Max(Settings.Ymin + 1, Settings.Ymax - 100);
        }
    }
}
