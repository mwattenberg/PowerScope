using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using PowerScope.Model;

namespace PowerScope.View.UserForms
{
    /// <summary>
    /// Interaction logic for FilterConfigWindow.xaml
    /// </summary>
    public partial class FilterConfigWindow : Window
    {
        private ChannelSettings _channelSettings;
        private string _selectedFilterType = "None";

        public FilterConfigWindow()
        {
            InitializeComponent();
            Loaded += FilterConfigWindow_Loaded;
        }

        private void FilterConfigWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _channelSettings = DataContext as ChannelSettings;
            if (_channelSettings != null)
            {
                // Set the current filter type based on existing filter
                SetCurrentFilterType();
            }
            else
            {
                // Default to None if no channel settings
                HighlightSelectedButton("None");
            }
        }

        private void SetCurrentFilterType()
        {
            if (_channelSettings.Filter == null)
            {
                _selectedFilterType = "None";
            }
            else
            {
                string filterType = _channelSettings.Filter.GetFilterType();
                _selectedFilterType = GetFilterTag(filterType);
            }
            
            HighlightSelectedButton(_selectedFilterType);
            UpdateParametersPanel(_selectedFilterType);
        }

        private string GetFilterTag(string filterType)
        {
            return filterType switch
            {
                "Exponential Low Pass" => "LowPass",
                "Exponential High Pass" => "HighPass",
                "Moving Average" => "MovingAverage",
                "Median" => "Median",
                "Notch" => "Notch",
                "Absolute" => "Absolute",
                "Squared" => "Squared",
                _ => "None"
            };
        }

        private void FilterButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string filterType)
            {
                _selectedFilterType = filterType;
                HighlightSelectedButton(filterType);
                UpdateParametersPanel(filterType);
            }
        }

        private void HighlightSelectedButton(string filterType)
        {
            // Reset all button styles
            ResetButtonStyles();
            
            // Highlight the selected button
            string buttonName = filterType switch
            {
                "None" => "Button_None",
                "LowPass" => "Button_LowPass",  
                "HighPass" => "Button_HighPass",
                "MovingAverage" => "Button_MovingAverage",
                "Median" => "Button_Median",
                "Notch" => "Button_Notch",
                "Absolute" => "Button_Absolute",
                "Squared" => "Button_Squared",
                _ => "Button_None"
            };

            Button selectedButton = this.FindName(buttonName) as Button;
            if (selectedButton != null)
            {
                selectedButton.BorderBrush = new SolidColorBrush(Colors.Orange);
                selectedButton.BorderThickness = new Thickness(3);
            }
        }

        private void ResetButtonStyles()
        {
            var buttonNames = new[] { "Button_None", "Button_LowPass", "Button_HighPass", "Button_MovingAverage", 
                                     "Button_Median", "Button_Notch", "Button_Absolute", "Button_Squared" };
            
            foreach (var buttonName in buttonNames)
            {
                Button button = this.FindName(buttonName) as Button;
                if (button != null)
                {
                    button.ClearValue(Button.BorderBrushProperty);
                    button.BorderThickness = new Thickness(2);
                }
            }
        }

        private void UpdateParametersPanel(string filterType)
        {
            var parametersStackPanel = this.FindName("ParametersStackPanel") as StackPanel;
            var parametersPanel = this.FindName("ParametersPanel") as Border;
            
            if (parametersStackPanel == null || parametersPanel == null) return;
            
            parametersStackPanel.Children.Clear();
            
            if (filterType == "None")
            {
                parametersPanel.Visibility = Visibility.Collapsed;
                ApplyFilter(null);
                return;
            }

            // For Absolute and Squared filters, no parameters are needed
            if (filterType == "Absolute" || filterType == "Squared")
            {
                parametersPanel.Visibility = Visibility.Collapsed;
                ApplyFilterWithoutParameters(filterType);
                return;
            }

            parametersPanel.Visibility = Visibility.Visible;

            switch (filterType)
            {
                case "LowPass":
                case "HighPass":
                    CreateAlphaSliderControls(filterType, parametersStackPanel);
                    break;
                case "MovingAverage":
                case "Median":
                    CreateWindowSizeControls(filterType, parametersStackPanel);
                    break;
                case "Notch":
                    CreateNotchControls(parametersStackPanel);
                    break;
            }
        }

        private void CreateAlphaSliderControls(string filterType, StackPanel parametersStackPanel)
        {
            // Alpha parameter (0.01 to 1.0)
            var alphaPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            
            var alphaLabel = new Label { Content = "Alpha:", Width = 80 };
            var alphaSlider = new Slider 
            { 
                Name = "AlphaSlider",
                Width = 200, 
                Minimum = 0.01, 
                Maximum = 1.0, 
                Value = GetCurrentAlpha(filterType),
                TickFrequency = 0.1,
                IsSnapToTickEnabled = false,
            };
            var alphaValueLabel = new Label { Name = "AlphaValueLabel", Width = 60, Content = alphaSlider.Value.ToString("F2") };
            
            alphaSlider.ValueChanged += (s, e) => 
            {
                alphaValueLabel.Content = e.NewValue.ToString("F2");
                ApplyFilterWithParameter(filterType, e.NewValue);
            };
            
            alphaPanel.Children.Add(alphaLabel);
            alphaPanel.Children.Add(alphaSlider);
            alphaPanel.Children.Add(alphaValueLabel);
            parametersStackPanel.Children.Add(alphaPanel);
            
            // Apply initial filter
            ApplyFilterWithParameter(filterType, alphaSlider.Value);
        }

        private void CreateWindowSizeControls(string filterType, StackPanel parametersStackPanel)
        {
            var windowPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            
            var windowLabel = new Label { Content = "Window Size:", Width = 100 };
            var windowTextBox = new TextBox 
            { 
                Name = "WindowSizeTextBox",
                Width = 80, 
                Text = GetCurrentWindowSize(filterType).ToString(),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            
            var buttonPanel = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(5, 0, 0, 0) };
            var upButton = new Button { Content = "▲", Width = 25, Height = 15, Margin = new Thickness(0, 0, 0, 2), ToolTip = "Increase" };
            var downButton = new Button { Content = "▼", Width = 25, Height = 15, ToolTip = "Decrease" };
            //▲
            upButton.Click += (s, e) => 
            {
                if (int.TryParse(windowTextBox.Text, out int current))
                {
                    int newValue = Math.Min(current + 1, 100);
                    windowTextBox.Text = newValue.ToString();
                    ApplyFilterWithParameter(filterType, newValue);
                }
            };
            
            downButton.Click += (s, e) => 
            {
                if (int.TryParse(windowTextBox.Text, out int current))
                {
                    int newValue = Math.Max(current - 1, 1);
                    windowTextBox.Text = newValue.ToString();
                    ApplyFilterWithParameter(filterType, newValue);
                }
            };
            
            windowTextBox.TextChanged += (s, e) => 
            {
                if (int.TryParse(windowTextBox.Text, out int value) && value >= 1 && value <= 100)
                {
                    ApplyFilterWithParameter(filterType, value);
                }
            };
            
            buttonPanel.Children.Add(upButton);
            buttonPanel.Children.Add(downButton);
            
            windowPanel.Children.Add(windowLabel);
            windowPanel.Children.Add(windowTextBox);
            windowPanel.Children.Add(buttonPanel);
            parametersStackPanel.Children.Add(windowPanel);
            
            // Apply initial filter
            if (int.TryParse(windowTextBox.Text, out int windowSize))
            {
                ApplyFilterWithParameter(filterType, windowSize);
            }
        }

        private void CreateNotchControls(StackPanel parametersStackPanel)
        {
            // Notch Frequency
            var freqPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            freqPanel.Children.Add(new Label { Content = "Notch Freq (Hz):", Width = 120 });
            var freqTextBox = new TextBox { Name = "NotchFreqTextBox", Width = 100, Text = GetCurrentNotchFreq().ToString("F1") };
            freqPanel.Children.Add(freqTextBox);
            parametersStackPanel.Children.Add(freqPanel);
            
            // Sample Rate
            var samplePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            samplePanel.Children.Add(new Label { Content = "Sample Rate (Hz):", Width = 120 });
            var sampleTextBox = new TextBox { Name = "SampleRateTextBox", Width = 100, Text = GetCurrentSampleRate().ToString("F1") };
            samplePanel.Children.Add(sampleTextBox);
            parametersStackPanel.Children.Add(samplePanel);
            
            // Bandwidth
            var bandwidthPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            bandwidthPanel.Children.Add(new Label { Content = "Bandwidth (Hz):", Width = 120 });
            var bandwidthTextBox = new TextBox { Name = "BandwidthTextBox", Width = 100, Text = GetCurrentBandwidth().ToString("F1") };
            bandwidthPanel.Children.Add(bandwidthTextBox);
            parametersStackPanel.Children.Add(bandwidthPanel);
            
            // Update filter when any parameter changes
            TextChangedEventHandler updateNotchFilter = (s, e) => ApplyNotchFilter(freqTextBox, sampleTextBox, bandwidthTextBox);
            freqTextBox.TextChanged += updateNotchFilter;
            sampleTextBox.TextChanged += updateNotchFilter;
            bandwidthTextBox.TextChanged += updateNotchFilter;
            
            // Apply initial filter
            ApplyNotchFilter(freqTextBox, sampleTextBox, bandwidthTextBox);
        }

        /// <summary>
        /// Apply filter with no parameters (for Absolute and Squared filters)
        /// </summary>
        private void ApplyFilterWithoutParameters(string filterType)
        {
            try
            {
                IDigitalFilter newFilter = filterType switch
                {
                    "Absolute" => new AbsoluteFilter(),
                    "Squared" => new SquaredFilter(),
                    _ => null
                };
                
                ApplyFilter(newFilter);
            }
            catch
            {
                // If filter creation fails, apply null filter (no filtering)
                ApplyFilter(null);
            }
        }

        /// <summary>
        /// Apply filter with a single parameter (for Alpha and WindowSize filters)
        /// </summary>
        private void ApplyFilterWithParameter(string filterType, double parameter)
        {
            try
            {
                IDigitalFilter newFilter = filterType switch
                {
                    "LowPass" => new ExponentialLowPassFilter(parameter),
                    "HighPass" => new ExponentialHighPassFilter(parameter),
                    "MovingAverage" => new MovingAverageFilter((int)parameter),
                    "Median" => new MedianFilter((int)parameter),
                    _ => null
                };
                
                ApplyFilter(newFilter);
            }
            catch
            {
                // If filter creation fails, apply null filter (no filtering)
                ApplyFilter(null);
            }
        }

        /// <summary>
        /// Apply notch filter with multiple parameters
        /// </summary>
        private void ApplyNotchFilter(TextBox freqBox, TextBox sampleBox, TextBox bandwidthBox)
        {
            try
            {
                if (double.TryParse(freqBox.Text, out double freq) &&
                    double.TryParse(sampleBox.Text, out double sample) &&
                    double.TryParse(bandwidthBox.Text, out double bandwidth) &&
                    freq > 0 && sample > 0 && bandwidth > 0)
                {
                    var notchFilter = new NotchFilter(freq, sample, bandwidth);
                    ApplyFilter(notchFilter);
                }
                else
                {
                    ApplyFilter(null);
                }
            }
            catch
            {
                ApplyFilter(null);
            }
        }

        /// <summary>
        /// Apply the filter directly to the channel settings (immediate application)
        /// </summary>
        private void ApplyFilter(IDigitalFilter filter)
        {
            if (_channelSettings != null)
            {
                _channelSettings.Filter = filter;
            }
        }

        private double GetCurrentAlpha(string filterType)
        {
            if (_channelSettings?.Filter != null)
            {
                var parameters = _channelSettings.Filter.GetFilterParameters();
                if (parameters.ContainsKey("Alpha"))
                {
                    return parameters["Alpha"];
                }
            }
            return 0.1; // Default value
        }

        private int GetCurrentWindowSize(string filterType)
        {
            if (_channelSettings?.Filter != null)
            {
                var parameters = _channelSettings.Filter.GetFilterParameters();
                if (parameters.ContainsKey("WindowSize"))
                {
                    return (int)parameters["WindowSize"];
                }
            }
            return 5; // Default value
        }

        private double GetCurrentNotchFreq()
        {
            if (_channelSettings?.Filter != null)
            {
                var parameters = _channelSettings.Filter.GetFilterParameters();
                if (parameters.ContainsKey("NotchFreq"))
                {
                    return parameters["NotchFreq"];
                }
            }
            return 50.0; // Default 50Hz
        }

        private double GetCurrentSampleRate()
        {
            if (_channelSettings?.Filter != null)
            {
                var parameters = _channelSettings.Filter.GetFilterParameters();
                if (parameters.ContainsKey("SampleRate"))
                {
                    return parameters["SampleRate"];
                }
            }
            return 1000.0; // Default 1kHz
        }

        private double GetCurrentBandwidth()
        {
            if (_channelSettings?.Filter != null)
            {
                var parameters = _channelSettings.Filter.GetFilterParameters();
                if (parameters.ContainsKey("Bandwidth"))
                {
                    return parameters["Bandwidth"];
                }
            }
            return 2.0; // Default 2Hz
        }

        // Event handlers
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}