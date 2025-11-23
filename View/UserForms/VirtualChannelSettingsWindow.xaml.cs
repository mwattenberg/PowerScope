using System;
using System.Windows;
using System.Windows.Media;
using PowerScope.Model;

namespace PowerScope.View.UserForms
{
    /// <summary>
    /// Interaction logic for VirtualChannelSettingsWindow.xaml
    /// Uses VirtualChannelSelectionBar for visual channel selection with constant input support
    /// </summary>
    public partial class VirtualChannelSettingsWindow : Window
    {
        private VirtualChannelConfig _config;
        private VirtualChannelOperationType _selectedOperation = VirtualChannelOperationType.Add;
        private double _inputAConstantValue = 0.0;
        private double _inputBConstantValue = 0.0;
        private bool _inputAIsConstant = false;
        private bool _inputBIsConstant = false;

        public VirtualChannelConfig VirtualChannelConfig
        {
            get { return _config; }
        }

        public VirtualChannelSettingsWindow(VirtualChannelConfig config)
        {
            InitializeComponent();

            _config = config ?? new VirtualChannelConfig();

            // Set the data context for MVVM binding
            DataContext = _config;

            // Initialize the selection bars with available channels
            InputASelector.AvailableChannels = _config.AvailableChannels;
            InputBSelector.AvailableChannels = _config.AvailableChannels;

            // Subscribe to selection change events
            InputASelector.SelectionChanged += (s, source) => 
            {
                _config.InputA = source;
                _inputAIsConstant = false;
                UpdateConstantButtonHighlight();
            };
            InputBSelector.SelectionChanged += (s, source) => 
            {
                _config.InputB = source;
                _inputBIsConstant = false;
                UpdateConstantButtonHighlight();
            };

            // Set initial selections if config already has values
            if (_config.InputA != null)
            {
                if (_config.InputA.IsConstant)
                {
                    _inputAConstantValue = _config.InputA.ConstantValue;
                    _inputAIsConstant = true;
                    InputAConstantButton.Content = _inputAConstantValue.ToString("G4");
                }
                else
                {
                    InputASelector.SetSelectedSource(_config.InputA);
                }
            }

            if (_config.InputB != null)
            {
                if (_config.InputB.IsConstant)
                {
                    _inputBConstantValue = _config.InputB.ConstantValue;
                    _inputBIsConstant = true;
                    InputBConstantButton.Content = _inputBConstantValue.ToString("G4");
                }
                else
                {
                    InputBSelector.SetSelectedSource(_config.InputB);
                }
            }

            // Update button highlights
            UpdateConstantButtonHighlight();

            // Set initial operation selection
            _selectedOperation = _config.Operation;
            HighlightSelectedOperationButton(_selectedOperation);
        }

        /// <summary>
        /// Handles operation button clicks
        /// </summary>
        private void OperationButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button && button.Tag is string operationTag)
            {
                _selectedOperation = operationTag switch
                {
                    "Add" => VirtualChannelOperationType.Add,
                    "Subtract" => VirtualChannelOperationType.Subtract,
                    "Multiply" => VirtualChannelOperationType.Multiply,
                    "Divide" => VirtualChannelOperationType.Divide,
                    _ => VirtualChannelOperationType.Add
                };

                HighlightSelectedOperationButton(_selectedOperation);
            }
        }

        /// <summary>
        /// Highlights the selected operation button (similar to FilterConfigWindow)
        /// </summary>
        private void HighlightSelectedOperationButton(VirtualChannelOperationType operation)
        {
            // Reset all button backgrounds to transparent
            Button_Add.Background = Brushes.Transparent;
            Button_Subtract.Background = Brushes.Transparent;
            Button_Multiply.Background = Brushes.Transparent;
            Button_Divide.Background = Brushes.Transparent;

            // Highlight the selected button
            System.Windows.Controls.Button selectedButton = operation switch
            {
                VirtualChannelOperationType.Add => Button_Add,
                VirtualChannelOperationType.Subtract => Button_Subtract,
                VirtualChannelOperationType.Multiply => Button_Multiply,
                VirtualChannelOperationType.Divide => Button_Divide,
                _ => Button_Add
            };

            // Apply background color to selected button
            object buttonPressedBrush = Application.Current.Resources["PlotSettings_ButtonPressedBrush"];
            if (buttonPressedBrush != null)
            {
                selectedButton.Background = (Brush)buttonPressedBrush;
            }
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            // Get Input A from selector or constant
            IVirtualSource inputA = null;
            if (_inputAIsConstant)
            {
                inputA = new ConstantOperand(_inputAConstantValue);
            }
            else
            {
                inputA = InputASelector.SelectedSource;
            }

            // Get Input B from selector or constant
            IVirtualSource inputB = null;
            if (_inputBIsConstant)
            {
                inputB = new ConstantOperand(_inputBConstantValue);
            }
            else
            {
                inputB = InputBSelector.SelectedSource;
            }

            // Validate selections
            if (inputA == null)
            {
                MessageBox.Show("Please select Input A channel or enter a constant.", "Validation Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (inputB == null)
            {
                MessageBox.Show("Please select Input B channel or enter a constant.", "Validation Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Update config from selections
            _config.InputA = inputA;
            _config.InputB = inputB;
            _config.Operation = _selectedOperation;

            // Validate the complete configuration
            string validationError = _config.Validate();
            if (!string.IsNullOrEmpty(validationError))
            {
                MessageBox.Show(validationError, "Configuration Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        }

        /// <summary>
        /// Handles the constant button click for Input A
        /// </summary>
        private void InputAConstantButton_Click(object sender, RoutedEventArgs e)
        {
            InputAConstantButton.Visibility = Visibility.Collapsed;
            InputAConstantTextBox.Visibility = Visibility.Visible;
            InputAConstantTextBox.Text = _inputAConstantValue.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
            InputAConstantTextBox.Focus();
            InputAConstantTextBox.SelectAll();
        }

        /// <summary>
        /// Handles the constant button click for Input B
        /// </summary>
        private void InputBConstantButton_Click(object sender, RoutedEventArgs e)
        {
            InputBConstantButton.Visibility = Visibility.Collapsed;
            InputBConstantTextBox.Visibility = Visibility.Visible;
            InputBConstantTextBox.Text = _inputBConstantValue.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
            InputBConstantTextBox.Focus();
            InputBConstantTextBox.SelectAll();
        }

        /// <summary>
        /// Handles key presses in constant textboxes
        /// </summary>
        private void ConstantTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                System.Windows.Input.Keyboard.ClearFocus();
            }
            else if (e.Key == System.Windows.Input.Key.Escape)
            {
                if (sender == InputAConstantTextBox)
                {
                    InputAConstantTextBox.Visibility = Visibility.Collapsed;
                    InputAConstantButton.Visibility = Visibility.Visible;
                }
                else if (sender == InputBConstantTextBox)
                {
                    InputBConstantTextBox.Visibility = Visibility.Collapsed;
                    InputBConstantButton.Visibility = Visibility.Visible;
                }
            }
        }

        /// <summary>
        /// Handles when constant textbox loses focus
        /// </summary>
        private void ConstantTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender == InputAConstantTextBox)
            {
                string inputText = InputAConstantTextBox.Text.Replace(',', '.');
                if (double.TryParse(inputText,
                    System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowExponent,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double value))
                {
                    _inputAConstantValue = value;
                    _inputAIsConstant = true;
                    InputAConstantButton.Content = _inputAConstantValue.ToString("G4", System.Globalization.CultureInfo.InvariantCulture);
                    InputASelector.SetSelectedSource(null);
                }
                else
                {
                    InputAConstantTextBox.Text = _inputAConstantValue.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
                }

                InputAConstantTextBox.Visibility = Visibility.Collapsed;
                InputAConstantButton.Visibility = Visibility.Visible;
                UpdateConstantButtonHighlight();
            }
            else if (sender == InputBConstantTextBox)
            {
                string inputText = InputBConstantTextBox.Text.Replace(',', '.');
                if (double.TryParse(inputText,
                    System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowExponent,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double value))
                {
                    _inputBConstantValue = value;
                    _inputBIsConstant = true;
                    InputBConstantButton.Content = _inputBConstantValue.ToString("G4", System.Globalization.CultureInfo.InvariantCulture);
                    InputBSelector.SetSelectedSource(null);
                }
                else
                {
                    InputBConstantTextBox.Text = _inputBConstantValue.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
                }

                InputBConstantTextBox.Visibility = Visibility.Collapsed;
                InputBConstantButton.Visibility = Visibility.Visible;
                UpdateConstantButtonHighlight();
            }
        }

        /// <summary>
        /// Updates the green border highlight on constant buttons
        /// </summary>
        private void UpdateConstantButtonHighlight()
        {
            if (_inputAIsConstant)
            {
                InputAConstantButton.BorderBrush = new SolidColorBrush(System.Windows.Media.Colors.White);
                InputAConstantButton.BorderThickness = new Thickness(3);
            }
            else
            {
                InputAConstantButton.BorderBrush = new SolidColorBrush(System.Windows.Media.Colors.Gray);
                InputAConstantButton.BorderThickness = new Thickness(2);
            }

            if (_inputBIsConstant)
            {
                InputBConstantButton.BorderBrush = new SolidColorBrush(System.Windows.Media.Colors.White);
                InputBConstantButton.BorderThickness = new Thickness(3);
            }
            else
            {
                InputBConstantButton.BorderBrush = new SolidColorBrush(System.Windows.Media.Colors.Gray);
                InputBConstantButton.BorderThickness = new Thickness(2);
            }
        }
    }
}
