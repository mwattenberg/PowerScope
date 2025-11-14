using System;
using System.Windows;
using PowerScope.Model;

namespace PowerScope.View.UserForms
{
    /// <summary>
    /// Interaction logic for VirtualChannelSettingsWindow.xaml
    /// Uses VirtualChannelConfig view model for MVVM-style binding
    /// ComboBoxes display available channels with their colors or accept constant numbers
    /// </summary>
    public partial class VirtualChannelSettingsWindow : Window
    {
        private VirtualChannelConfig _config;

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
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
   // Parse Input A - could be a channel or a constant number
            var operandA = ParseOperand(InputAComboBox);
            if (operandA == null)
          {
     MessageBox.Show("Please select Input A channel or enter a valid number.", "Validation Error",
MessageBoxButton.OK, MessageBoxImage.Warning);
      return;
     }

   // Parse Input B - could be a channel or a constant number
  var operandB = ParseOperand(InputBComboBox);
            if (operandB == null)
            {
              MessageBox.Show("Please select Input B channel or enter a valid number.", "Validation Error",
         MessageBoxButton.OK, MessageBoxImage.Warning);
         return;
            }

  // Update the config from UI selections
         _config.InputA = operandA;
            _config.InputB = operandB;

         // Update operation type based on selection
   int operationIndex = OperationComboBox.SelectedIndex;
            _config.Operation = (VirtualChannelOperationType)operationIndex;

            DialogResult = true;
Close();
        }

        /// <summary>
        /// Parses a ComboBox value as either a Channel or a constant number
  /// Returns IOperandSource (ChannelOperand or ConstantOperand) or null if invalid
        /// </summary>
        private IOperandSource ParseOperand(System.Windows.Controls.ComboBox comboBox)
        {
   // If a Channel is selected from dropdown
            if (comboBox.SelectedItem is Channel channel)
     {
         return new ChannelOperand(channel);
            }

      // If text was entered, try to parse as a number
   string text = comboBox.Text?.Trim();
            if (!string.IsNullOrEmpty(text))
            {
     if (double.TryParse(text, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowExponent,
  System.Globalization.CultureInfo.InvariantCulture, out double constantValue))
     {
      return new ConstantOperand(constantValue);
         }
}

            return null;
  }
    }
}
