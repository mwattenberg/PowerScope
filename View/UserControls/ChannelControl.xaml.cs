using System;
using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using PowerScope.Model;
using PowerScope.View.UserForms;

namespace PowerScope.View.UserControls
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
            // Subscribe to DataContext changes to update play/pause button
            DataContextChanged += ChannelControl_DataContextChanged;
            // Subscribe to Loaded event to ensure initial state is set
            Loaded += ChannelControl_Loaded;
        }

        private void ChannelControl_Loaded(object sender, RoutedEventArgs e)
        {
            UpdatePlayPauseButton();
        }

        private void ChannelControl_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // Unsubscribe from old settings
            if (e.OldValue is ChannelSettings oldSettings)
            {
                oldSettings.PropertyChanged -= Settings_PropertyChanged;
            }

            // Subscribe to new settings
            if (e.NewValue is ChannelSettings newSettings)
            {
                newSettings.PropertyChanged += Settings_PropertyChanged;
                UpdatePlayPauseButton(); // Update button when DataContext changes
            }
        }

        private void Settings_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ChannelSettings.IsEnabled))
            {
                UpdatePlayPauseButton();
            }
        }

        private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            // Toggle the enabled state
            if (Settings != null)
                Settings.IsEnabled = !Settings.IsEnabled;

            // Prevent the event from bubbling up to RootGrid
            e.Handled = true;
        }

        private void UpdatePlayPauseButton()
        {
            var playPauseIcon = this.FindName("PlayPauseIcon") as TextBlock;
            var playPauseButton = this.FindName("PlayPauseButton") as Button;
            
            if (playPauseIcon != null && playPauseButton != null && Settings != null)
            {
                if (Settings.IsEnabled)
                {
                    playPauseIcon.Text = "⏸"; // Pause symbol for enabled (running) state
                    playPauseButton.ToolTip = "Disable channel";
                }
                else
                {
                    playPauseIcon.Text = "▶"; // Play symbol for disabled (stopped) state
                    playPauseButton.ToolTip = "Enable channel";
                }
            }
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
            if (Settings != null)
            {
                // Show measurement selection dialog directly in ChannelControl
                View.UserForms.MeasurementSelection measurementSelection = new View.UserForms.MeasurementSelection();
                measurementSelection.Owner = Window.GetWindow(this);
                
                if (measurementSelection.ShowDialog() == true && 
                    measurementSelection.SelectedMeasurementType.HasValue)
                {
                    // Request measurement through ChannelSettings event
                    // The Channel that owns these settings will receive the event and add the measurement
                    Settings.RequestMeasurement(measurementSelection.SelectedMeasurementType.Value);
                }
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
