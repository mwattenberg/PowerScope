using System;
using System.Windows;
using System.Windows.Input;
using System.Text.RegularExpressions;
using SerialPlotDN_WPF.Model;
using System.Windows.Threading;

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
        public PlotSettings Settings 
        { 
            get 
            { 
                return DataContext as PlotSettings; 
            } 
        }

        private readonly DispatcherTimer _fpsUpTimer;
        private readonly DispatcherTimer _fpsDownTimer;
        private const int FpsStepIntervalMs = 100; // Adjustable speed (ms)

        private readonly DispatcherTimer _serialHzUpTimer;
        private readonly DispatcherTimer _serialHzDownTimer;
        private const int SerialHzStepIntervalMs = 100; // Adjustable speed (ms)
        private const int SerialHzStep = 50; // Step size for SerialPortUpdateRateHz

        public PlotSettingsWindow()
        {
            InitializeComponent();

            _fpsUpTimer = new DispatcherTimer();
            _fpsUpTimer.Interval = TimeSpan.FromMilliseconds(FpsStepIntervalMs);
            _fpsUpTimer.Tick += FpsUpTimer_Tick;

            _fpsDownTimer = new DispatcherTimer();
            _fpsDownTimer.Interval = TimeSpan.FromMilliseconds(FpsStepIntervalMs);
            _fpsDownTimer.Tick += FpsDownTimer_Tick;

            ButtonPlotFPSUp.PreviewMouseLeftButtonDown += ButtonPlotFPSUp_PreviewMouseLeftButtonDown;
            ButtonPlotFPSUp.PreviewMouseLeftButtonUp += ButtonPlotFPSUp_PreviewMouseLeftButtonUp;
            ButtonPlotFPSUp.MouseLeave += ButtonPlotFPSUp_MouseLeave;

            ButtonPlotFPSDown.PreviewMouseLeftButtonDown += ButtonPlotFPSDown_PreviewMouseLeftButtonDown;
            ButtonPlotFPSDown.PreviewMouseLeftButtonUp += ButtonPlotFPSDown_PreviewMouseLeftButtonUp;
            ButtonPlotFPSDown.MouseLeave += ButtonPlotFPSDown_MouseLeave;

            _serialHzUpTimer = new DispatcherTimer();
            _serialHzUpTimer.Interval = TimeSpan.FromMilliseconds(SerialHzStepIntervalMs);
            _serialHzUpTimer.Tick += SerialHzUpTimer_Tick;

            _serialHzDownTimer = new DispatcherTimer();
            _serialHzDownTimer.Interval = TimeSpan.FromMilliseconds(SerialHzStepIntervalMs);
            _serialHzDownTimer.Tick += SerialHzDownTimer_Tick;

            ButtonSerialHzUp.PreviewMouseLeftButtonDown += ButtonSerialHzUp_PreviewMouseLeftButtonDown;
            ButtonSerialHzUp.PreviewMouseLeftButtonUp += ButtonSerialHzUp_PreviewMouseLeftButtonUp;
            ButtonSerialHzUp.MouseLeave += ButtonSerialHzUp_MouseLeave;

            ButtonSerialHzDown.PreviewMouseLeftButtonDown += ButtonSerialHzDown_PreviewMouseLeftButtonDown;
            ButtonSerialHzDown.PreviewMouseLeftButtonUp += ButtonSerialHzDown_PreviewMouseLeftButtonUp;
            ButtonSerialHzDown.MouseLeave += ButtonSerialHzDown_MouseLeave;
        }

        /// <summary>
        /// Constructor that accepts existing PlotSettings to edit directly
        /// </summary>
        /// <param name="existingSettings">Existing settings to edit directly</param>
        public PlotSettingsWindow(PlotSettings existingSettings) : this()
        {
            DataContext = existingSettings;
        }

        private void FpsUpTimer_Tick(object sender, EventArgs e)
        {
            ButtonPlotFPSUp_Click(null, null);
        }

        private void FpsDownTimer_Tick(object sender, EventArgs e)
        {
            ButtonPlotFPSDown_Click(null, null);
        }

        private void SerialHzUpTimer_Tick(object sender, EventArgs e)
        {
            ButtonSerialHzUp_Click(null, null);
        }

        private void SerialHzDownTimer_Tick(object sender, EventArgs e)
        {
            ButtonSerialHzDown_Click(null, null);
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
                Settings.SerialPortUpdateRateHz = Math.Min(10000, Settings.SerialPortUpdateRateHz + SerialHzStep);
        }

        private void ButtonSerialHzDown_Click(object sender, RoutedEventArgs e)
        {
            if (Settings != null)
                Settings.SerialPortUpdateRateHz = Math.Max(1, Settings.SerialPortUpdateRateHz - SerialHzStep);
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

        // FPS Up continuous handlers
        private void ButtonPlotFPSUp_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            ButtonPlotFPSUp_Click(sender, e);
            _fpsUpTimer.Start();
        }
        private void ButtonPlotFPSUp_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _fpsUpTimer.Stop();
        }
        private void ButtonPlotFPSUp_MouseLeave(object sender, MouseEventArgs e)
        {
            _fpsUpTimer.Stop();
        }

        // FPS Down continuous handlers
        private void ButtonPlotFPSDown_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            ButtonPlotFPSDown_Click(sender, e);
            _fpsDownTimer.Start();
        }
        private void ButtonPlotFPSDown_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _fpsDownTimer.Stop();
        }
        private void ButtonPlotFPSDown_MouseLeave(object sender, MouseEventArgs e)
        {
            _fpsDownTimer.Stop();
        }

        // Serial Hz Up continuous handlers
        private void ButtonSerialHzUp_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            ButtonSerialHzUp_Click(sender, e);
            _serialHzUpTimer.Start();
        }
        private void ButtonSerialHzUp_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _serialHzUpTimer.Stop();
        }
        private void ButtonSerialHzUp_MouseLeave(object sender, MouseEventArgs e)
        {
            _serialHzUpTimer.Stop();
        }

        // Serial Hz Down continuous handlers
        private void ButtonSerialHzDown_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            ButtonSerialHzDown_Click(sender, e);
            _serialHzDownTimer.Start();
        }
        private void ButtonSerialHzDown_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _serialHzDownTimer.Stop();
        }
        private void ButtonSerialHzDown_MouseLeave(object sender, MouseEventArgs e)
        {
            _serialHzDownTimer.Stop();
        }
    }
}
