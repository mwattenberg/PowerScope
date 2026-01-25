using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PowerScope.Model;

namespace PowerScope.View.UserControls
{
    /// <summary>
    /// Interaction logic for VerticalControl.xaml
    /// </summary>
    public partial class VerticalControl : UserControl
    {
        /// <summary>
        /// PlotSettings instance used as DataContext
        /// </summary>
        public PlotSettings Settings
        {
            get => DataContext as PlotSettings;
            set => DataContext = value;
        }

        public VerticalControl()
        {
            InitializeComponent();
            
            DataContextChanged += VerticalControl_DataContextChanged;
        }

        private void VerticalControl_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is PlotSettings oldSettings)
            {
                oldSettings.PropertyChanged -= Settings_PropertyChanged;
            }

            if (e.NewValue is PlotSettings newSettings)
            {
                newSettings.PropertyChanged += Settings_PropertyChanged;
                UpdateAutoScaleButtonStyle();
            }
        }

        private void Settings_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PlotSettings.Ymin) || 
                e.PropertyName == nameof(PlotSettings.Ymax) || 
                e.PropertyName == nameof(PlotSettings.YAutoScale))
            {
                Dispatcher.BeginInvoke(() => UpdateAutoScaleButtonStyle());
            }
        }

        private void UpdateAutoScaleButtonStyle()
        {
            if (AutoScaleButton != null && Settings != null)
            {
                if (Settings.YAutoScale)
                {
                    AutoScaleButton.Background = new SolidColorBrush(Colors.LimeGreen);
                }
                else
                {
                    object defaultBrush = Application.Current.Resources["PlotSettings_TextBoxBackgroundBrush"];
                    if (defaultBrush != null)
                    {
                        AutoScaleButton.Background = (Brush)defaultBrush;
                    }
                    else
                    {
                        AutoScaleButton.Background = new SolidColorBrush(Colors.Gray);
                    }
                }
            }
        }

        private void MinTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (int.TryParse(MinTextBox.Text, out int minValue) && Settings != null)
            {
                if (minValue != Settings.Ymin)
                {
                    Settings.Ymin = Math.Min(minValue, Settings.Ymax - 1);
                }
            }
        }

        private void MaxTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (int.TryParse(MaxTextBox.Text, out int maxValue) && Settings != null)
            {
                if (maxValue != Settings.Ymax)
                {
                    Settings.Ymax = Math.Max(maxValue, Settings.Ymin + 1);
                }
            }
        }

        private void AutoScaleButton_Click(object sender, RoutedEventArgs e)
        {
            if (Settings != null)
            {
                Settings.YAutoScale = !Settings.YAutoScale;
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            if (Settings != null)
            {
                int currentMin = Settings.Ymin;
                int currentMax = Settings.Ymax;

                //This is a dirty hack to force the UI to refresh
                //We like it dirty
                Settings.Ymin = currentMin + 1;
                Settings.Ymax = currentMax + 1;

                Settings.Ymin = currentMin - 1;
                Settings.Ymax = currentMax - 1;

                if (MinTextBox != null)
                    MinTextBox.Text = currentMin.ToString();
                if (MaxTextBox != null)
                    MaxTextBox.Text = currentMax.ToString();
            }
        }

        private void ButtonGrow_Click(object sender, RoutedEventArgs e)
        {
            if (Settings != null)
            {
                int center = (Settings.Ymax + Settings.Ymin) / 2;
                int halfRange = (Settings.Ymax - Settings.Ymin) / 2;
                
                int newHalfRange = halfRange * 2;
                Settings.Ymin = center - newHalfRange;
                Settings.Ymax = center + newHalfRange;
            }
        }

        private void ButtonShrink_Click(object sender, RoutedEventArgs e)
        {
            if (Settings != null)
            {
                int center = (Settings.Ymax + Settings.Ymin) / 2;
                int halfRange = (Settings.Ymax - Settings.Ymin) / 2;
                
                int newHalfRange = Math.Max(halfRange / 2, 1);
                Settings.Ymin = center - newHalfRange;
                Settings.Ymax = center + newHalfRange;
            }
        }
    }
}
