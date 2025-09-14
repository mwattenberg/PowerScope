using System;
using System.Windows;
using System.Windows.Controls;
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
            
            //// Subscribe to DataContext changes
            //DataContextChanged += VerticalControl_DataContextChanged;
        }

        //private void VerticalControl_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        //{
        //    // Unsubscribe from old settings
        //    if (e.OldValue is PlotSettings oldSettings)
        //    {
        //        oldSettings.PropertyChanged -= Settings_PropertyChanged;
        //    }

        //    // Subscribe to new settings
        //    if (e.NewValue is PlotSettings newSettings)
        //    {
        //        newSettings.PropertyChanged += Settings_PropertyChanged;
        //        UpdateUIFromSettings();
        //    }
        //}

        private void Settings_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PlotSettings.Ymin) || 
                e.PropertyName == nameof(PlotSettings.Ymax) || 
                e.PropertyName == nameof(PlotSettings.YAutoScale))
            {
                Dispatcher.BeginInvoke(() => UpdateUIFromSettings());
            }
        }

        private void UpdateUIFromSettings()
        {
            if (Settings != null)
            {
                // Update text boxes without triggering change events
                if (MaxTextBox != null)
                    MaxTextBox.Text = Settings.Ymax.ToString();
                if (MinTextBox != null)
                    MinTextBox.Text = Settings.Ymin.ToString();
                if (AutoScaleCheckBox != null)
                    AutoScaleCheckBox.IsChecked = Settings.YAutoScale;
            }
        }

        private void MinTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (int.TryParse(MinTextBox.Text, out int minValue) && Settings != null)
            {
                if (minValue != Settings.Ymin)
                {
                    Settings.Ymin = Math.Min(minValue, Settings.Ymax - 1); // Ensure Min is less than Max
                }
            }
        }

        private void MaxTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (int.TryParse(MaxTextBox.Text, out int maxValue) && Settings != null)
            {
                if (maxValue != Settings.Ymax)
                {
                    Settings.Ymax = Math.Max(maxValue, Settings.Ymin + 1); // Ensure Max is greater than Min
                }
            }
        }

        private void AutoScaleCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (Settings != null)
            {
                bool isChecked = AutoScaleCheckBox.IsChecked ?? false;
                if (isChecked != Settings.YAutoScale)
                {
                    Settings.YAutoScale = isChecked;
                }
            }
        }

        private void ButtonGrow_Click(object sender, RoutedEventArgs e)
        {
            if (Settings != null)
            {
                // Calculate the current range center
                int center = (Settings.Ymax + Settings.Ymin) / 2;
                int halfRange = (Settings.Ymax - Settings.Ymin) / 2;
                
                // Double the range while keeping the center the same
                int newHalfRange = halfRange * 2;
                Settings.Ymin = center - newHalfRange;
                Settings.Ymax = center + newHalfRange;
            }
        }

        private void ButtonShrink_Click(object sender, RoutedEventArgs e)
        {
            if (Settings != null)
            {
                // Calculate the current range center
                int center = (Settings.Ymax + Settings.Ymin) / 2;
                int halfRange = (Settings.Ymax - Settings.Ymin) / 2;
                
                // Halve the range while keeping the center the same (minimum range of 2)
                int newHalfRange = Math.Max(halfRange / 2, 1);
                Settings.Ymin = center - newHalfRange;
                Settings.Ymax = center + newHalfRange;
            }
        }
    }
}
