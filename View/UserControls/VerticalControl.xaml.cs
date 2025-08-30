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
using System.Windows.Navigation;
using System.Windows.Shapes;
using SerialPlotDN_WPF.Model;

namespace SerialPlotDN_WPF.View.UserControls
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

        // Legacy properties for backward compatibility (delegate to PlotSettings when available)
        public int Max
        {
            get => Settings?.Ymax ?? 1000;
            set
            {
                if (Settings != null)
                    Settings.Ymax = value;
            }
        }

        public int Min
        {
            get => Settings?.Ymin ?? -1000;
            set
            {
                if (Settings != null)
                    Settings.Ymin = value;
            }
        }

        public bool IsAutoScale
        {
            get => Settings?.YAutoScale ?? false;
            set
            {
                if (Settings != null)
                    Settings.YAutoScale = value;
            }
        }

        // Events for backward compatibility
        public event EventHandler<int>? MaxValueChanged;
        public event EventHandler<int>? MinValueChanged;
        public event EventHandler<bool>? AutoScaleChanged;

        public VerticalControl()
        {
            InitializeComponent();
            
            // Subscribe to DataContext changes
            DataContextChanged += VerticalControl_DataContextChanged;
        }

        private void VerticalControl_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // Unsubscribe from old settings
            if (e.OldValue is PlotSettings oldSettings)
            {
                oldSettings.PropertyChanged -= Settings_PropertyChanged;
            }

            // Subscribe to new settings
            if (e.NewValue is PlotSettings newSettings)
            {
                newSettings.PropertyChanged += Settings_PropertyChanged;
                UpdateUIFromSettings();
            }
        }

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
                    MinValueChanged?.Invoke(this, Settings.Ymin); // Fire legacy event
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
                    MaxValueChanged?.Invoke(this, Settings.Ymax); // Fire legacy event
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
                    AutoScaleChanged?.Invoke(this, Settings.YAutoScale); // Fire legacy event
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
