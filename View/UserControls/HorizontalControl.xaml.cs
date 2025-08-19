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
        public event RoutedEventHandler? GrowClicked;
        public event RoutedEventHandler? ShrinkClicked;
        public event RoutedEventHandler? BufferSizeChanged;
        public event RoutedEventHandler? WindowSizeChanged;

        // Properties
        private int _bufferSize = 1000;
        private int _windowSize = 500;

        public int BufferSize
        {
            get => _bufferSize;
            set
            {
                _bufferSize = value;
                BufferSizeTextBox.Text = value.ToString();
                BufferSizeChanged?.Invoke(this, new RoutedEventArgs());
            }
        }

        public int WindowSize
        {
            get => _windowSize;
            set
            {
                _windowSize = value;
                WindowSizeTextBox.Text = value.ToString();
                WindowSizeChanged?.Invoke(this, new RoutedEventArgs());
            }
        }

        public HorizontalControl()
        {
            InitializeComponent();
            
            // Subscribe to button click events
            ButtonGrow.Click += ButtonGrow_Click;
            ButtonShrink.Click += ButtonShrink_Click;
            
            // Subscribe to textbox text changed events
            BufferSizeTextBox.TextChanged += BufferSizeTextBox_TextChanged;
            WindowSizeTextBox.TextChanged += WindowSizeTextBox_TextChanged;
        }

        private void ButtonGrow_Click(object sender, RoutedEventArgs e)
        {
            // Double the window size
            WindowSize = Math.Min(WindowSize * 2, 100000); // Cap at 100,000 to prevent overflow
            GrowClicked?.Invoke(this, e);
        }

        private void ButtonShrink_Click(object sender, RoutedEventArgs e)
        {
            // Halve the window size (divide by 2)
            WindowSize = Math.Max(WindowSize / 2, 1); // Minimum value of 1
            ShrinkClicked?.Invoke(this, e);
        }

        private void BufferSizeTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (int.TryParse(BufferSizeTextBox.Text, out int bufferSize))
            {
                if (bufferSize != _bufferSize)
                {
                    _bufferSize = bufferSize;
                    BufferSizeChanged?.Invoke(this, new RoutedEventArgs());
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
                    WindowSizeChanged?.Invoke(this, new RoutedEventArgs());
                }
            }
        }
    }
}
