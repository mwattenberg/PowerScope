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

        public HorizontalControl()
        {
            InitializeComponent();
        }

        private void ButtonGrow_Click(object sender, RoutedEventArgs e)
        {
            if (Settings != null)
                Settings.Xmax = Math.Min(Settings.Xmax * 2, Settings.BufferSize);
        }

        private void ButtonShrink_Click(object sender, RoutedEventArgs e)
        {
            if (Settings != null)
                Settings.Xmax = Math.Max(Settings.Xmax / 2, 128);
        }

        private void BufferSizeTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (int.TryParse(BufferSizeTextBox.Text, out int bufferSize) && Settings != null && bufferSize != Settings.BufferSize)
            {
                Settings.BufferSize = Math.Clamp(bufferSize, 1000, 100000000);
            }
        }

        private void SamplesTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (int.TryParse(SamplesTextBox.Text, out int samples) && Settings != null && samples != Settings.Xmax)
                Settings.Xmax = Math.Max(samples, 1);
        }
    }
}
