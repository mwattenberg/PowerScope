using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SerialPlotDN_WPF.View.UserControls
{
    /// <summary>
    /// Interaction logic for HorizontalControl.xaml
    /// </summary>
    public partial class HorizontalControl : UserControl
    {
        // Events for button clicks
        public event EventHandler<int>? BufferSizeChanged;
        public event EventHandler<int>? WindowSizeChanged;

        // Properties
        private int _bufferSize = 1000;
        private int _windowSize = 500;

        public int BufferSize
        {
            get => _bufferSize;
            set
            {
                _bufferSize = Math.Clamp(value,1000,5000000);
                BufferSizeTextBox.Text = _bufferSize.ToString();
                BufferSizeChanged?.Invoke(this, _bufferSize);
            }
        }

        public int WindowSize
        {
            get => _windowSize;
            set
            {
                _windowSize = Math.Clamp(value,100,this._bufferSize);
                WindowSizeTextBox.Text = value.ToString();
                WindowSizeChanged?.Invoke(this, value);
            }
        }

        public HorizontalControl()
        {
            InitializeComponent();           
        }

        private void ButtonGrow_Click(object sender, RoutedEventArgs e)
        {
            // Double the window size
            this.WindowSize = Math.Min(WindowSize * 2, 100000); // Cap at 100,000 to prevent overflow
        }

        private void ButtonShrink_Click(object sender, RoutedEventArgs e)
        {
            // Halve the window size (divide by 2)
            this.WindowSize = Math.Max(WindowSize / 2, 1); // Minimum value of 1
        }

        private void BufferSizeTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (int.TryParse(BufferSizeTextBox.Text, out int bufferSize))
            {
                if (bufferSize != _bufferSize)
                {
                    this.BufferSize = bufferSize;
                    BufferSizeChanged?.Invoke(this, this.BufferSize);
                }
            }
        }

        private void WindowSizeTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (int.TryParse(WindowSizeTextBox.Text, out int windowSize))
            {
                if (windowSize != _windowSize)
                {
                    _windowSize = windowSize;
                    WindowSizeChanged?.Invoke(this, windowSize);
                }
            }
        }
    }
}
