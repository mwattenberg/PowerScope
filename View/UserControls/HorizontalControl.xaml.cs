using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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

        // Events for button clicks
        public event EventHandler<int> BufferSizeChanged;
        public event EventHandler<int> WindowSizeChanged;

        // Properties
        private int _bufferSize = 1000;

        // Legacy WindowSize property for backward compatibility (now delegates to PlotSettings.Xmax)
        public int WindowSize
        {
            get => Settings?.Xmax ?? 500;
            set
            {
                if (Settings != null)
                {
                    Settings.Xmax = value;
                    WindowSizeChanged?.Invoke(this, value); // Fire legacy event
                }
            }
        }

        public int BufferSize
        {
            get 
            { 
                return _bufferSize; 
            }
            set
            {
                _bufferSize = Math.Clamp(value,1000,5000000);
                BufferSizeTextBox.Text = _bufferSize.ToString();
                if (BufferSizeChanged != null)
                    BufferSizeChanged.Invoke(this, _bufferSize);
            }
        }

        public HorizontalControl()
        {
            InitializeComponent();
            
            // Subscribe to DataContext changes
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
                Dispatcher.BeginInvoke(() => UpdateUIFromSettings());
            }
        }

        private void UpdateUIFromSettings()
        {
            if (Settings != null && SamplesTextBox != null)
            {
                // Update samples text box without triggering change events
                SamplesTextBox.Text = Settings.Xmax.ToString();
            }
        }

        private void ButtonGrow_Click(object sender, RoutedEventArgs e)
        {
            // Double the window size
            if (Settings != null)
            {
                int newSize = Math.Min(Settings.Xmax * 2, BufferSize);
                Settings.Xmax = newSize;
                WindowSizeChanged?.Invoke(this, newSize); // Fire legacy event
            }
        }

        private void ButtonShrink_Click(object sender, RoutedEventArgs e)
        {
            // Halve the window size (divide by 2)
            if (Settings != null)
            {
                int newSize = Math.Max(Settings.Xmax / 2, 128);
                Settings.Xmax = newSize;
                WindowSizeChanged?.Invoke(this, newSize); // Fire legacy event
            }
        }

        private void BufferSizeTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (int.TryParse(BufferSizeTextBox.Text, out int bufferSize))
            {
                if (bufferSize != _bufferSize)
                {
                    this.BufferSize = bufferSize;
                    if (BufferSizeChanged != null)
                        BufferSizeChanged.Invoke(this, this.BufferSize);
                }
            }
        }

        private void SamplesTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (int.TryParse(SamplesTextBox.Text, out int samples) && Settings != null)
            {
                if (samples != Settings.Xmax)
                {
                    Settings.Xmax = Math.Max(samples, 1); // Ensure minimum value
                    WindowSizeChanged?.Invoke(this, Settings.Xmax); // Fire legacy event
                }
            }
        }
    }
}
