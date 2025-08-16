using System;
using System.ComponentModel;
using System.Windows;
using System.Globalization;
using System.Windows.Input;

namespace SerialPlotDN_WPF.View.UserForms
{
    /// <summary>
    /// Interaction logic for PlotSettingsWindow.xaml
    /// </summary>
    public partial class PlotSettingsWindow : Window, INotifyPropertyChanged
    {
        // Settings properties that can be set from MainWindow
        private double _plotUpdateRateFPS = 30;
        private double _serialPortUpdateRateHz = 1000;
        private double _lineWidth = 1;
        private bool _antiAliasing = false;

        public double PlotUpdateRateFPS 
        { 
            get => _plotUpdateRateFPS; 
            set 
            { 
                if (_plotUpdateRateFPS != value)
                {
                    _plotUpdateRateFPS = Math.Max(1, Math.Min(120, value)); // Clamp between 1-120 FPS
                    OnPropertyChanged(nameof(PlotUpdateRateFPS));
                }
            } 
        }

        public double SerialPortUpdateRateHz 
        { 
            get => _serialPortUpdateRateHz; 
            set 
            { 
                if (_serialPortUpdateRateHz != value)
                {
                    _serialPortUpdateRateHz = Math.Max(1, Math.Min(10000, value)); // Clamp between 1-10000 Hz
                    OnPropertyChanged(nameof(SerialPortUpdateRateHz));
                }
            } 
        }

        public double LineWidth 
        { 
            get => _lineWidth; 
            set 
            { 
                if (_lineWidth != value)
                {
                    _lineWidth = Math.Max(0.1, Math.Min(10, value)); // Clamp between 0.1-10
                    OnPropertyChanged(nameof(LineWidth));
                }
            } 
        }

        public bool AntiAliasing 
        { 
            get => _antiAliasing; 
            set 
            { 
                if (_antiAliasing != value)
                {
                    _antiAliasing = value;
                    OnPropertyChanged(nameof(AntiAliasing));
                }
            } 
        }

        public bool DialogResult { get; private set; }

        public event PropertyChangedEventHandler PropertyChanged;

        public PlotSettingsWindow()
        {
            InitializeComponent();
            DataContext = this; // Set data context for binding
        }

        private void ButtonOK_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void ButtonCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
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
            DialogResult = false;
            Close();
        }

        // Plot FPS Up/Down button handlers
        private void ButtonPlotFPSUp_Click(object sender, RoutedEventArgs e)
        {
            PlotUpdateRateFPS = Math.Min(120, PlotUpdateRateFPS + 1);
        }

        private void ButtonPlotFPSDown_Click(object sender, RoutedEventArgs e)
        {
            PlotUpdateRateFPS = Math.Max(1, PlotUpdateRateFPS - 1);
        }

        // Serial Hz Up/Down button handlers
        private void ButtonSerialHzUp_Click(object sender, RoutedEventArgs e)
        {
            SerialPortUpdateRateHz = Math.Min(10000, SerialPortUpdateRateHz + 100);
        }

        private void ButtonSerialHzDown_Click(object sender, RoutedEventArgs e)
        {
            SerialPortUpdateRateHz = Math.Max(1, SerialPortUpdateRateHz - 100);
        }

        // Line Width Up/Down button handlers
        private void ButtonLineWidthUp_Click(object sender, RoutedEventArgs e)
        {
            LineWidth = Math.Min(10, LineWidth + 0.1);
        }

        private void ButtonLineWidthDown_Click(object sender, RoutedEventArgs e)
        {
            LineWidth = Math.Max(0.1, LineWidth - 0.1);
        }

        // Method to initialize values from MainWindow
        public void InitializeFromMainWindow(double plotFPS, double serialHz, double lineWidth, bool antiAliasing)
        {
            PlotUpdateRateFPS = plotFPS;
            SerialPortUpdateRateHz = serialHz;
            LineWidth = lineWidth;
            AntiAliasing = antiAliasing;
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private bool ValidateInputs()
        {
            try
            {
                // Parse and validate PlotFPS
                if (double.TryParse(TextBoxPlotFPS.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double fps))
                {
                    PlotUpdateRateFPS = fps;
                }
                else
                {
                    MessageBox.Show("Invalid Plot Update Rate (FPS). Please enter a valid number.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                // Parse and validate SerialHz
                if (double.TryParse(TextBoxSerialHz.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double hz))
                {
                    SerialPortUpdateRateHz = hz;
                }
                else
                {
                    MessageBox.Show("Invalid Serial Update Rate (Hz). Please enter a valid number.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                // Parse and validate LineWidth
                if (double.TryParse(TextBoxLineWidth.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double width))
                {
                    LineWidth = width;
                }
                else
                {
                    MessageBox.Show("Invalid Line Width. Please enter a valid number.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Validation error: {ex.Message}", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }
    }
}
