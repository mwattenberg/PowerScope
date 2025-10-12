using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
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

        private void GainOffset_KeyDown(object sender, KeyEventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox == null || Settings == null) return;

            if (e.Key == Key.Enter)
            {
                // Force the binding to update by updating the source immediately
                var bindingExpression = textBox.GetBindingExpression(TextBox.TextProperty);
                bindingExpression?.UpdateSource();
                e.Handled = true;
            }
        }

        /// <summary>
        /// Handles mouse wheel events for gain and offset textboxes
        /// </summary>
        private void GainOffset_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox == null || Settings == null) return;
            
            // Only respond to mouse wheel when the textbox has focus or mouse is over it
            if (!textBox.IsFocused && !textBox.IsMouseOver)
                return;

            // Positive Delta means wheel up (increase), negative means wheel down (decrease)
            bool increase;
            
            // Parse the current textbox value with culture invariance
            if (double.TryParse(textBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double currentValue))
            {
                if (currentValue > 0)
                {
                    increase = e.Delta > 0;
                }
                else
                {
                    increase = e.Delta < 0;
                }
            }
            else
            {
                // If parsing fails, fall back to normal behavior
                increase = e.Delta > 0;
            }

            // Check for modifier keys to determine increment size
            bool ctrlPressed = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
            bool altPressed = Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt);
            
            AdjustValue(textBox, increase, ctrlPressed, altPressed);
            e.Handled = true;
        }

        /// <summary>
        /// Common method to adjust gain or offset values
        /// </summary>
        /// <param name="textBox">The textbox being adjusted</param>
        /// <param name="increase">True to increase, false to decrease</param>
        /// <param name="ctrlPressed">True if Ctrl is pressed (10% increments)</param>
        /// <param name="altPressed">True if Alt is pressed (0.1% increments)</param>
        private void AdjustValue(TextBox textBox, bool increase, bool ctrlPressed = false, bool altPressed = false)
        {
            if (textBox == null || Settings == null) return;

            // Determine increment multiplier based on modifier keys
            double incrementMultiplier;
            if (ctrlPressed)
                incrementMultiplier = 1.10; // 10% increment for Ctrl
            else if (altPressed)
                incrementMultiplier = 1.001; // 0.1% increment for Alt
            else
                incrementMultiplier = 1.01; // Default 1% increment

            double decrementMultiplier = 2.0 - incrementMultiplier; // Mathematical inverse for decrements

            bool isGainTextBox = textBox.Name == "GainTextBox";

            if (isGainTextBox)
            {
                if (increase)
                {
                    if (Math.Abs(Settings.Gain) < 0.00001)
                        Settings.Gain = -Math.Sign(Settings.Gain) * 0.001; // Avoid zero gain
                    else
                        Settings.Gain = Settings.Gain * incrementMultiplier;
                }
                else
                {
                    if (Settings.Gain == 0.0)
                        Settings.Gain = 0.01; // Avoid zero gain
                    else
                        Settings.Gain = Settings.Gain * decrementMultiplier;
                }
            }
            else // OffsetTextBox
            {
                if (increase)
                {
                    if (Settings.Offset == 0.0)
                        Settings.Offset = 0.01;
                    else
                        Settings.Offset = Settings.Offset * incrementMultiplier;
                }
                else
                {
                    if (Settings.Offset == 0.0)
                        Settings.Offset = 0.01;
                    else
                        Settings.Offset = Settings.Offset * decrementMultiplier;
                }
            }
        }

        private void SignedDecimalOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox == null) return;

            // Get current state
            string inputChar = e.Text;
            
            // Allow digits always
            if (char.IsDigit(inputChar, 0))
            {
                e.Handled = false;
                return;
            }

            // Allow decimal point if not already present
            if (inputChar == "." && !textBox.Text.Contains('.'))
            {
                e.Handled = false;
                return;
            }

            // Allow minus sign only at the beginning and if not already present
            if (inputChar == "-" && textBox.CaretIndex == 0 && !textBox.Text.Contains('-'))
            {
                e.Handled = false;
                return;
            }

            // Allow 'e' or 'E' for exponential notation
            if ((inputChar.ToLower() == "e") && !textBox.Text.ToLower().Contains('e') && 
                textBox.Text.Length > 0 && textBox.Text.Any(char.IsDigit))
            {
                e.Handled = false;
                return;
            }

            // Allow '+' or '-' after 'e' or 'E' for exponential notation
            if ((inputChar == "+" || inputChar == "-") && textBox.CaretIndex > 0)
            {
                char prevChar = textBox.Text[textBox.CaretIndex - 1];
                if (prevChar == 'e' || prevChar == 'E')
                {
                    e.Handled = false;
                    return;
                }
            }

            // Block all other characters
            e.Handled = true;
        }
    }
}
