using System;
using System.Windows;
using System.Windows.Controls;
using PowerScope.Model;

namespace PowerScope.View.UserControls
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
            get 
            { 
                return DataContext as PlotSettings; 
            }
            set 
            { 
                DataContext = value; 
            }
        }

        // Events
        public event EventHandler<int> BufferSizeChanged;

        // Properties
        private int _bufferSize = 1000;

        public int BufferSize
        {
            get 
            { 
                return _bufferSize; 
            }
            set
            {
                _bufferSize = Math.Clamp(value, 1000, 5000000);
                BufferSizeTextBox.Text = _bufferSize.ToString();
                if (BufferSizeChanged != null)
                    BufferSizeChanged.Invoke(this, _bufferSize);
            }
        }

        public HorizontalControl()
        {
            InitializeComponent();
        }



        private void Settings_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PlotSettings.Xmax))
                Dispatcher.BeginInvoke(new Action(UpdateUIFromSettings));
        }

        private void UpdateUIFromSettings()
        {
            if (Settings != null && SamplesTextBox != null)
                SamplesTextBox.Text = Settings.Xmax.ToString();
        }

        private void ButtonGrow_Click(object sender, RoutedEventArgs e)
        {
            if (Settings != null)
                Settings.Xmax = Math.Min(Settings.Xmax * 2, BufferSize);
        }

        private void ButtonShrink_Click(object sender, RoutedEventArgs e)
        {
            if (Settings != null)
                Settings.Xmax = Math.Max(Settings.Xmax / 2, 128);
        }

        private void BufferSizeTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            int bufferSize;
            if (int.TryParse(BufferSizeTextBox.Text, out bufferSize) && bufferSize != _bufferSize)
                BufferSize = bufferSize;
        }

        private void SamplesTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            int samples;
            if (int.TryParse(SamplesTextBox.Text, out samples) && Settings != null && samples != Settings.Xmax)
                Settings.Xmax = Math.Max(samples, 1);
        }
    }
}
