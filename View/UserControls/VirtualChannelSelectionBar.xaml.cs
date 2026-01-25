using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using PowerScope.Model;

namespace PowerScope.View.UserControls
{
    /// <summary>
    /// Interaction logic for VirtualChannelSelectionBar.xaml
    /// Displays available channels as selectable boxes in a horizontal row
    /// Similar to ChannelControlBar but optimized for selection in virtual channel configuration
    /// </summary>
    public partial class VirtualChannelSelectionBar : UserControl
    {
        /// <summary>
        /// View model wrapper for channel selection
        /// </summary>
        public class SelectableChannel : INotifyPropertyChanged
        {
            private bool _isSelected;

            public Channel Channel { get; set; }

            public bool IsSelected
            {
                get { return _isSelected; }
                set
                {
                    if (_isSelected != value)
                    {
                        _isSelected = value;
                        OnPropertyChanged(nameof(IsSelected));
                    }
                }
            }

            public SelectableChannel(Channel channel)
            {
                Channel = channel;
            }

            public event PropertyChangedEventHandler PropertyChanged;

            protected virtual void OnPropertyChanged(string propertyName)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        /// <summary>
        /// Special marker for constant input option
        /// </summary>
        public class ConstantInputOption : INotifyPropertyChanged
        {
            private bool _isSelected;
            private double _constantValue;

            public bool IsSelected
            {
                get { return _isSelected; }
                set
                {
                    if (_isSelected != value)
                    {
                        _isSelected = value;
                        OnPropertyChanged(nameof(IsSelected));
                    }
                }
            }

            public double ConstantValue
            {
                get { return _constantValue; }
                set
                {
                    if (Math.Abs(_constantValue - value) > 1e-15)
                    {
                        _constantValue = value;
                        OnPropertyChanged(nameof(ConstantValue));
                        OnPropertyChanged(nameof(DisplayValue));
                    }
                }
            }

            public string DisplayValue
            {
                get { return _constantValue == 0.0 ? "Const" : _constantValue.ToString("G4"); }
            }

            public event PropertyChangedEventHandler PropertyChanged;

            protected virtual void OnPropertyChanged(string propertyName)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        private ObservableCollection<SelectableChannel> _selectableChannels = new ObservableCollection<SelectableChannel>();
        private ConstantInputOption _constantOption = new ConstantInputOption();
        private List<Channel> _availableChannels;

        /// <summary>
        /// Event raised when a channel or constant is selected
        /// </summary>
        public event EventHandler<Channel> SelectionChanged;

        public VirtualChannelSelectionBar()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Sets the available channels to display
        /// </summary>
        public List<Channel> AvailableChannels
        {
            get { return _availableChannels; }
            set
            {
                _availableChannels = value;
                RebuildChannelList();
            }
        }

        /// <summary>
        /// Gets the currently selected source as a Channel
        /// Converts constant input to a ConstantDataStream-backed channel
        /// </summary>
        public Channel GetSelectedChannel()
        {
            var selectedChannel = _selectableChannels.FirstOrDefault(sc => sc.IsSelected);
            if (selectedChannel != null)
                return selectedChannel.Channel;

            if (_constantOption.IsSelected)
            {
                ConstantDataStream stream = new ConstantDataStream(_constantOption.ConstantValue);
                ChannelSettings settings = new ChannelSettings
                {
                    Label = $"Constant: {_constantOption.ConstantValue:G}",
                    Color = System.Windows.Media.Colors.DarkGray,
                    IsEnabled = true
                };
                return new Channel(stream, 0, settings);
            }

            return null;
        }

        /// <summary>
        /// Sets the selected source programmatically
        /// Supports both regular channels and constant-backed channels
        /// </summary>
        public void SetSelectedChannel(Channel channel)
        {
            ClearAllSelections();

            if (channel == null)
            {
                UpdateConstantButtonStyle();
                return;
            }

            if (channel.OwnerStream is ConstantDataStream constantStream)
            {
                _constantOption.IsSelected = true;
                string label = channel.Label;
                if (label.StartsWith("Constant: "))
                {
                    label = label.Substring("Constant: ".Length);
                }
                if (double.TryParse(label, System.Globalization.NumberStyles.Float, 
                    System.Globalization.CultureInfo.InvariantCulture, out double value))
                {
                    _constantOption.ConstantValue = value;
                    ConstantButton.Content = value.ToString("G4", System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            else
            {
                var matchingChannel = _selectableChannels.FirstOrDefault(sc => sc.Channel == channel);
                if (matchingChannel != null)
                    matchingChannel.IsSelected = true;
            }

            UpdateConstantButtonStyle();
        }

        /// <summary>
        /// Rebuilds the channel list with available channels + constant option
        /// </summary>
        private void RebuildChannelList()
        {
            _selectableChannels.Clear();

            if (_availableChannels == null)
                return;

            // Add all available channels
            foreach (var channel in _availableChannels)
            {
                _selectableChannels.Add(new SelectableChannel(channel));
            }

            // Add constant option at the end
            // Note: We'll handle this separately in the ItemsControl for different styling

            ChannelItemsControl.ItemsSource = _selectableChannels;
        }

        /// <summary>
        /// Handles channel box click
        /// </summary>
        private void ChannelBox_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is SelectableChannel selectableChannel)
            {
                ClearAllSelections();
                selectableChannel.IsSelected = true;
                UpdateConstantButtonStyle();
                SelectionChanged?.Invoke(this, selectableChannel.Channel);
            }
        }

        /// <summary>
        /// Handles constant box click - removed, use inline editing instead
        /// </summary>
        public void SelectConstant()
        {
            var dialog = new ConstantValueInputDialog(_constantOption.ConstantValue);
            dialog.Owner = Window.GetWindow(this);

            if (dialog.ShowDialog() == true)
            {
                ClearAllSelections();
                _constantOption.IsSelected = true;
                _constantOption.ConstantValue = dialog.ConstantValue;

                Channel constantChannel = GetSelectedChannel();
                SelectionChanged?.Invoke(this, constantChannel);
            }
        }

        /// <summary>
        /// Handles constant button click
        /// </summary>
        private void ConstantButton_Click(object sender, RoutedEventArgs e)
        {
            ConstantButton.Visibility = Visibility.Collapsed;
            ConstantTextBox.Visibility = Visibility.Visible;
            ConstantTextBox.Text = _constantOption.ConstantValue.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
            ConstantTextBox.Focus();
            ConstantTextBox.SelectAll();
        }

        /// <summary>
        /// Handles key presses in constant textbox
        /// </summary>
        private void ConstantTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                System.Windows.Input.Keyboard.ClearFocus();
            }
            else if (e.Key == System.Windows.Input.Key.Escape)
            {
                ConstantTextBox.Visibility = Visibility.Collapsed;
                ConstantButton.Visibility = Visibility.Visible;
                e.Handled = true;
            }
        }

        /// <summary>
        /// Handles when constant textbox loses focus
        /// </summary>
        private void ConstantTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            string inputText = ConstantTextBox.Text.Replace(',', '.');
            if (double.TryParse(inputText,
                System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowExponent,
                System.Globalization.CultureInfo.InvariantCulture,
                out double value))
            {
                _constantOption.ConstantValue = value;
                _constantOption.IsSelected = true;
                ClearChannelSelections();
                ConstantButton.Content = _constantOption.ConstantValue.ToString("G4", System.Globalization.CultureInfo.InvariantCulture);
                ConstantButton.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.LimeGreen);
                ConstantButton.BorderThickness = new Thickness(3);

                Channel constantChannel = GetSelectedChannel();
                SelectionChanged?.Invoke(this, constantChannel);
            }
            else
            {
                ConstantTextBox.Text = _constantOption.ConstantValue.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
            }

            ConstantTextBox.Visibility = Visibility.Collapsed;
            ConstantButton.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Clears all selections (both channels and constant)
        /// </summary>
        private void ClearAllSelections()
        {
            foreach (var sc in _selectableChannels)
                sc.IsSelected = false;

            _constantOption.IsSelected = false;
        }

        /// <summary>
        /// Clears channel selections (used when constant is selected)
        /// </summary>
        private void ClearChannelSelections()
        {
            foreach (var sc in _selectableChannels)
                sc.IsSelected = false;
        }

        /// <summary>
        /// Updates constant button styling based on selection state
        /// </summary>
        private void UpdateConstantButtonStyle()
        {
            if (_constantOption.IsSelected)
            {
                ConstantButton.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.LimeGreen);
                ConstantButton.BorderThickness = new Thickness(3);
            }
            else
            {
                ConstantButton.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Gray);
                ConstantButton.BorderThickness = new Thickness(2);
            }
        }
    }

    /// <summary>
    /// Dialog for entering constant numeric values
    /// </summary>
    public class ConstantValueInputDialog : Window
    {
        private TextBox _valueTextBox;

        public double ConstantValue { get; private set; }

        public ConstantValueInputDialog(double initialValue = 0.0)
        {
            Title = "Enter Constant Value";
            Width = 300;
            Height = 150;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 30, 30));

            ConstantValue = initialValue;

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var stackPanel = new StackPanel
            {
                Margin = new Thickness(20),
                VerticalAlignment = VerticalAlignment.Center
            };

            var label = new TextBlock
            {
                Text = "Enter a numeric value:",
                Margin = new Thickness(0, 0, 0, 10),
                Foreground = System.Windows.Media.Brushes.White
            };

            _valueTextBox = new TextBox
            {
                Text = initialValue.ToString("G"),
                Margin = new Thickness(0, 0, 0, 10),
                Padding = new Thickness(5),
                FontSize = 14
            };

            _valueTextBox.KeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter)
                    OkButton_Click(null, null);
            };

            stackPanel.Children.Add(label);
            stackPanel.Children.Add(_valueTextBox);
            grid.Children.Add(stackPanel);
            Grid.SetRow(stackPanel, 0);

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(20, 10, 20, 20)
            };

            var okButton = new Button
            {
                Content = "OK",
                Width = 75,
                Margin = new Thickness(0, 0, 10, 0),
                Padding = new Thickness(5)
            };
            okButton.Click += OkButton_Click;

            var cancelButton = new Button
            {
                Content = "Cancel",
                Width = 75,
                Padding = new Thickness(5)
            };
            cancelButton.Click += (s, e) => DialogResult = false;

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            grid.Children.Add(buttonPanel);
            Grid.SetRow(buttonPanel, 1);

            Content = grid;

            Loaded += (s, e) => _valueTextBox.Focus();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(_valueTextBox.Text,
                System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowExponent,
                System.Globalization.CultureInfo.InvariantCulture,
                out double value))
            {
                ConstantValue = value;
                DialogResult = true;
            }
            else
            {
                MessageBox.Show("Please enter a valid numeric value.", "Invalid Input",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
