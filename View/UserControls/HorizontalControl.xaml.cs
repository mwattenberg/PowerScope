using System;
using System.Windows;
using System.Windows.Controls;
using SerialPlotDN_WPF.Model;

namespace SerialPlotDN_WPF.View.UserControls
{
    /// <summary>
    /// Interaction logic for HorizontalControl.xaml
    /// </summary>
    public partial class HorizontalControl : UserControl
    {
        /// <summary>
        /// PlotSettings instance used as DataContext
        /// </summary>
        public PlotSettings Settings
        {
            get => DataContext as PlotSettings;
            set => DataContext = value;
        }

        // Events
        public event EventHandler<int> BufferSizeChanged;

        // Properties
        private int _bufferSize = 1000;

        public int BufferSize
        {
            get => _bufferSize;
            set
            {
                _bufferSize = Math.Clamp(value, 1000, 5000000);
                BufferSizeTextBox.Text = _bufferSize.ToString();
                BufferSizeChanged?.Invoke(this, _bufferSize);
            }
        }

        public HorizontalControl()
        {
            InitializeComponent();
            DataContextChanged += HorizontalControl_DataContextChanged;
        }

        private void HorizontalControl_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
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
            if (e.PropertyName == nameof(PlotSettings.Xmax))
            {
                Dispatcher.BeginInvoke(UpdateUIFromSettings);
            }
        }

        private void UpdateUIFromSettings()
        {
            if (Settings != null && SamplesTextBox != null)
            {
                SamplesTextBox.Text = Settings.Xmax.ToString();
            }
        }

        private void ButtonGrow_Click(object sender, RoutedEventArgs e)
        {
            if (Settings != null)
            {
                Settings.Xmax = Math.Min(Settings.Xmax * 2, BufferSize);
            }
        }

        private void ButtonShrink_Click(object sender, RoutedEventArgs e)
        {
            if (Settings != null)
            {
                Settings.Xmax = Math.Max(Settings.Xmax / 2, 128);
            }
        }

        private void BufferSizeTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (int.TryParse(BufferSizeTextBox.Text, out int bufferSize) && bufferSize != _bufferSize)
            {
                BufferSize = bufferSize;
            }
        }

        private void SamplesTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (int.TryParse(SamplesTextBox.Text, out int samples) && Settings != null && samples != Settings.Xmax)
            {
                Settings.Xmax = Math.Max(samples, 1);
            }
        }
    }
}
