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
                UpdateParametersPanel("None");
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
            
            // Highlight the selected button by its name
            Button selectedButton = FindName($"Button_{filterType}") as Button;

            if (selectedButton != null)
            {
                object accentColor = Application.Current.Resources["AccentColor"];
                if (accentColor != null)
                {
                    selectedButton.BorderBrush = new SolidColorBrush((Color)accentColor);
                    selectedButton.BorderThickness = new Thickness(3);
                }
            }
        }

        private void ResetButtonStyles()
        {
            var buttonNames = new[] 
            { 
                "Button_None", "Button_LowPass", "Button_HighPass", "Button_MovingAverage", 
                "Button_Median", "Button_Notch", "Button_Absolute", "Button_Squared" 
            };
            
            foreach (var buttonName in buttonNames)
            {
                Button button = FindName(buttonName) as Button;
                if (button != null)
                {
                    button.ClearValue(Button.BorderBrushProperty);
                    button.BorderThickness = new Thickness(2);
                }
            }
        }

        private void UpdateParametersPanel(string filterType)
        {
            ParametersStackPanel.Children.Clear();
            
            if (filterType == "None" || filterType == "Absolute" || filterType == "Squared")
            {
                ApplyFilter(filterType, null);
                ParametersPanel.Visibility = Visibility.Collapsed;
                return;
            }

            ParametersPanel.Visibility = Visibility.Visible;

            switch (filterType)
            {
                case "LowPass":
                case "HighPass":
                    CreateAlphaSliderControls(filterType, ParametersStackPanel);
                    break;
                case "MovingAverage":
                case "Median":
                    CreateWindowSizeControls(filterType, ParametersStackPanel);
                    break;
                case "Notch":
                    CreateNotchControls(ParametersStackPanel);
                    break;
            }
        }

        private void CreateAlphaSliderControls(string filterType, StackPanel parametersStackPanel)
        {
            var alphaPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            
            var alphaLabel = new Label { Content = "Alpha:", Width = 80, VerticalAlignment = VerticalAlignment.Center };
            var alphaSlider = new Slider 
            { 
                Name = "AlphaSlider",
                Width = 200, 
                Minimum = 0.01, 
                Maximum = 1.0, 
                Value = GetCurrentAlpha(filterType),
                TickFrequency = 0.01,
                IsSnapToTickEnabled = false,
            };
            var alphaValueLabel = new Label { Name = "AlphaValueLabel", Width = 60, Content = alphaSlider.Value.ToString("F2"), VerticalAlignment = VerticalAlignment.Center };
            
            alphaSlider.ValueChanged += (s, e) => 
            {
                alphaValueLabel.Content = e.NewValue.ToString("F2");
                ApplyFilter(filterType, e.NewValue);
            };
            
            alphaPanel.Children.Add(alphaLabel);
            alphaPanel.Children.Add(alphaSlider);
            alphaPanel.Children.Add(alphaValueLabel);
            parametersStackPanel.Children.Add(alphaPanel);
            
            ApplyFilter(filterType, alphaSlider.Value);
        }

        private void CreateWindowSizeControls(string filterType, StackPanel parametersStackPanel)
        {
            var windowPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            
            var windowLabel = new Label { Content = "Window Size:", Width = 100, VerticalAlignment = VerticalAlignment.Center };
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
            
            upButton.Click += (s, e) => 
            {
                if (int.TryParse(windowTextBox.Text, out int current))
                {
                    int newValue = Math.Min(current + 1, 100);
                    windowTextBox.Text = newValue.ToString();
                }
            };
            
            downButton.Click += (s, e) => 
            {
                if (int.TryParse(windowTextBox.Text, out int current))
                {
                    int newValue = Math.Max(current - 1, 1);
                    windowTextBox.Text = newValue.ToString();
                }
            };
            
            windowTextBox.TextChanged += (s, e) => 
            {
                if (int.TryParse(windowTextBox.Text, out int value) && value >= 1 && value <= 100)
                {
                    ApplyFilter(filterType, value);
                }
            };
            
            buttonPanel.Children.Add(upButton);
            buttonPanel.Children.Add(downButton);
            
            windowPanel.Children.Add(windowLabel);
            windowPanel.Children.Add(windowTextBox);
            windowPanel.Children.Add(buttonPanel);
            parametersStackPanel.Children.Add(windowPanel);
            
            if (int.TryParse(windowTextBox.Text, out int windowSize))
            {
                ApplyFilter(filterType, windowSize);
            }
        }

        private void CreateNotchControls(StackPanel parametersStackPanel)
        {
            var freqPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            freqPanel.Children.Add(new Label { Content = "Notch Freq (Hz):", Width = 120, VerticalAlignment = VerticalAlignment.Center });
            var freqTextBox = new TextBox { Name = "NotchFreqTextBox", Width = 100, Text = GetCurrentNotchFreq().ToString("F1"), VerticalContentAlignment = VerticalAlignment.Center };
            freqPanel.Children.Add(freqTextBox);
            parametersStackPanel.Children.Add(freqPanel);
            
            var samplePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            samplePanel.Children.Add(new Label { Content = "Sample Rate (Hz):", Width = 120, VerticalAlignment = VerticalAlignment.Center });
            var sampleTextBox = new TextBox { Name = "SampleRateTextBox", Width = 100, Text = GetCurrentSampleRate().ToString("F1"), VerticalContentAlignment = VerticalAlignment.Center };
            samplePanel.Children.Add(sampleTextBox);
            parametersStackPanel.Children.Add(samplePanel);
            
            var bandwidthPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            bandwidthPanel.Children.Add(new Label { Content = "Bandwidth (Hz):", Width = 120, VerticalAlignment = VerticalAlignment.Center });
            var bandwidthTextBox = new TextBox { Name = "BandwidthTextBox", Width = 100, Text = GetCurrentBandwidth().ToString("F1"), VerticalContentAlignment = VerticalAlignment.Center };
            bandwidthPanel.Children.Add(bandwidthTextBox);
            parametersStackPanel.Children.Add(bandwidthPanel);
            
            TextChangedEventHandler updateNotchFilter = (s, e) => ApplyNotchFilter(freqTextBox, sampleTextBox, bandwidthTextBox);
            freqTextBox.TextChanged += updateNotchFilter;
            sampleTextBox.TextChanged += updateNotchFilter;
            bandwidthTextBox.TextChanged += updateNotchFilter;
            
            ApplyNotchFilter(freqTextBox, sampleTextBox, bandwidthTextBox);
        }

        private void ApplyFilter(string filterType, double? parameter)
        {
            if (_channelSettings == null) return;

            IDigitalFilter newFilter = null;
            try
            {
                newFilter = filterType switch
                {
                    "LowPass" => new ExponentialLowPassFilter(parameter.Value),
                    "HighPass" => new ExponentialHighPassFilter(parameter.Value),
                    "MovingAverage" => new MovingAverageFilter((int)parameter.Value),
                    "Median" => new MedianFilter((int)parameter.Value),
                    "Absolute" => new AbsoluteFilter(),
                    "Squared" => new SquaredFilter(),
                    "Downsampling" => new DownsamplingFilter((int)parameter.Value),
                    _ => null
                };
            }
            catch
            {
                newFilter = null;
            }
            _channelSettings.Filter = newFilter;
        }

        private void ApplyNotchFilter(TextBox freqBox, TextBox sampleBox, TextBox bandwidthBox)
        {
            if (_channelSettings == null) return;

            if (double.TryParse(freqBox.Text, out double freq) &&
                double.TryParse(sampleBox.Text, out double sample) &&
                double.TryParse(bandwidthBox.Text, out double bandwidth) &&
                freq > 0 && sample > 0 && bandwidth > 0)
            {
                _channelSettings.Filter = new NotchFilter(freq, sample, bandwidth);
            }
            else
            {
                _channelSettings.Filter = null;
            }
        }

        private double GetCurrentAlpha(string filterType)
        {
            if (_channelSettings?.Filter != null)
            {
                var parameters = _channelSettings.Filter.GetFilterParameters();
                if (parameters.ContainsKey("Alpha")) return parameters["Alpha"];
            }
            return 0.1;
        }

        private int GetCurrentWindowSize(string filterType)
        {
            if (_channelSettings?.Filter != null)
            {
                var parameters = _channelSettings.Filter.GetFilterParameters();
                if (parameters.ContainsKey("WindowSize")) return (int)parameters["WindowSize"];
            }
            return 5;
        }

        private int GetCurrentDownsamplingRate()
        {
            if (_channelSettings?.Filter != null)
            {
                var parameters = _channelSettings.Filter.GetFilterParameters();
                if (parameters.ContainsKey("Rate")) return (int)parameters["Rate"];
            }
            return 3;
        }

        private double GetCurrentNotchFreq()
        {
            if (_channelSettings?.Filter != null)
            {
                var parameters = _channelSettings.Filter.GetFilterParameters();
                if (parameters.ContainsKey("NotchFreq")) return parameters["NotchFreq"];
            }
            return 50.0;
        }

        private double GetCurrentSampleRate()
        {
            if (_channelSettings?.Filter != null)
            {
                var parameters = _channelSettings.Filter.GetFilterParameters();
                if (parameters.ContainsKey("SampleRate")) return parameters["SampleRate"];
            }
            return 1000.0;
        }

        private double GetCurrentBandwidth()
        {
            if (_channelSettings?.Filter != null)
            {
                var parameters = _channelSettings.Filter.GetFilterParameters();
                if (parameters.ContainsKey("Bandwidth")) return parameters["Bandwidth"];
            }
            return 2.0;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) this.DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}