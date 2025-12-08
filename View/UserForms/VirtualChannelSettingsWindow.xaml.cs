using System;
using System.Windows;
using System.Windows.Media;
using PowerScope.Model;
using System.Collections.Generic;

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

            // Set initial selections if config already has values
            if (_config.InputA != null)
            {
                InputASelector.SetSelectedSource(_config.InputA);
            }

            if (_config.InputB != null)
            {
                InputBSelector.SetSelectedSource(_config.InputB);
            }

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

            // Map operations to their SVG icon pairs (normal and selected)
            var iconMap = new Dictionary<VirtualChannelOperationType, (string normal, string selected)>
            {
                { VirtualChannelOperationType.Add, ("/Icons/plus.svg", "/Icons/plus_selected.svg") },
                { VirtualChannelOperationType.Subtract, ("/Icons/minus.svg", "/Icons/minus_selected.svg") },
                { VirtualChannelOperationType.Multiply, ("/Icons/multiply.svg", "/Icons/multiply_selected.svg") },
                { VirtualChannelOperationType.Divide, ("/Icons/divide.svg", "/Icons/divide_selected.svg") }
            };

            // Reset all buttons to normal icons
            ResetAllOperationIcons();

            // Set the selected button's icon to the selected (lime green) version
            if (iconMap.TryGetValue(operation, out var icons))
            {
                System.Windows.Controls.Button selectedButton = operation switch
                {
                    VirtualChannelOperationType.Add => Button_Add,
                    VirtualChannelOperationType.Subtract => Button_Subtract,
                    VirtualChannelOperationType.Multiply => Button_Multiply,
                    VirtualChannelOperationType.Divide => Button_Divide,
                    _ => Button_Add
                };

                // Update the SVG icon to the selected (lime green) version
                UpdateOperationButtonIcon(selectedButton, icons.selected);
            }
        }

        /// <summary>
        /// Resets all operation buttons to their normal (non-selected) icons
        /// </summary>
        private void ResetAllOperationIcons()
        {
            UpdateOperationButtonIcon(Button_Add, "/Icons/plus.svg");
            UpdateOperationButtonIcon(Button_Subtract, "/Icons/minus.svg");
            UpdateOperationButtonIcon(Button_Multiply, "/Icons/multiply.svg");
            UpdateOperationButtonIcon(Button_Divide, "/Icons/divide.svg");
        }

        /// <summary>
        /// Updates the SVG icon for an operation button
        /// Finds and updates the SvgViewbox within the button's content
        /// </summary>
        private void UpdateOperationButtonIcon(System.Windows.Controls.Button button, string svgPath)
        {
            // Find the SvgViewbox element in the button's content
            if (button.Content is SharpVectors.Converters.SvgViewbox svgViewbox)
            {
                svgViewbox.Source = new Uri(svgPath, UriKind.Relative);
            }
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            // Get inputs directly from SelectionBars (they return channel OR constant)
            IVirtualSource inputA = InputASelector.SelectedSource;
            IVirtualSource inputB = InputBSelector.SelectedSource;

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
    }
}
