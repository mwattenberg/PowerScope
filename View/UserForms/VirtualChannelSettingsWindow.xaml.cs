using System;
using System.Windows;
using PowerScope.Model;

namespace PowerScope.View.UserForms
{
    /// <summary>
 /// Interaction logic for VirtualChannelSettingsWindow.xaml
 /// Uses VirtualChannelConfig view model for MVVM-style binding
 /// ComboBoxes display available channels with their colors
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
         // Validate selections
 if (InputAComboBox.SelectedItem == null || InputBComboBox.SelectedItem == null)
        {
       MessageBox.Show("Please select both Input A and Input B channels.", "Validation Error", 
            MessageBoxButton.OK, MessageBoxImage.Warning);
       return;
      }

      // Get selected channels
           var inputA = InputAComboBox.SelectedItem as Channel;
           var inputB = InputBComboBox.SelectedItem as Channel;

         if (inputA == inputB)
  {
       MessageBox.Show("Input A and Input B cannot be the same channel.", "Validation Error",
  MessageBoxButton.OK, MessageBoxImage.Warning);
       return;
    }

    // Update the config from UI selections
       _config.InputA = inputA;
        _config.InputB = inputB;
         _config.Label = LabelTextBox.Text;

         // Update operation type based on selection
            int operationIndex = OperationComboBox.SelectedIndex;
  _config.Operation = (VirtualChannelOperationType)operationIndex;

       DialogResult = true;
     Close();
  }

 private void CancelButton_Click(object sender, RoutedEventArgs e)
  {
        DialogResult = false;
 Close();
    }
   }
}
