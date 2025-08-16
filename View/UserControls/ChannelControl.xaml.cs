using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SerialPlotDN_WPF.View.UserControls
{
    public partial class ChannelControl : UserControl
    {
        // ChannelMode enum for channel processing modes
        public enum ChannelMode
        {
            DC,
            AC,
            ABS,
            Squared
        }



        private Color _assignedColor = Colors.Gray;
        private static readonly Color DisabledColor = Colors.Gray;
        private static readonly Brush SelectedBrush = new SolidColorBrush(Colors.LimeGreen);
        private static readonly Brush DefaultBrush = new SolidColorBrush(DisabledColor);
        private bool _isEnabled = true;
        
        private double _gain = 1.0;
        private double _offset = 0.0;
        private ChannelMode _mode;

        // Events
        public event RoutedEventHandler? ChannelEnabledChanged;
        public event RoutedEventHandler? GainChanged;

        public ChannelControl()
        {
            InitializeComponent();
            UpdateGainOffsetDisplay();
            _mode = ChannelMode.DC; // Default mode
        }

        // Set the top color bar and channel label
        public void SetChannelColorAndLabel(Color color, string label)
        {
            _assignedColor = color;
            ChannelLabel.Text = label;
            UpdateColorBarBackground();
        }

        // Set gain and offset display
        public void SetGainOffset(double gain, double offset)
        {
            _gain = gain;
            _offset = offset;
            UpdateGainOffsetDisplay();
        }

        // Get/set math function (keeping for backward compatibility)
        public string MathFunction { get; set; } = "Raw";

        // Get/set channel enabled state
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                _isEnabled = value;
                UpdateColorBarBackground();
                ChannelEnabledChanged?.Invoke(this, new RoutedEventArgs());
            }
        }

        // Gain property
        public double Gain
        {
            get => _gain;
            set
            {
                _gain = value;
                UpdateGainOffsetDisplay();
                GainChanged?.Invoke(this, new RoutedEventArgs());
            }
        }

        // Offset property
        public double Offset
        {
            get => _offset;
            set
            {
                _offset = value;
                UpdateGainOffsetDisplay();
            }
        }

        // Mode property
        public ChannelMode Mode
        {
            get => _mode;
            set
            {
                _mode = value;
                // Reset all button backgrounds to default
                DC.Background = DefaultBrush;
                AC.Background = DefaultBrush;
                ABS.Background = DefaultBrush;
                Squared.Background = DefaultBrush;
                // Set selected button background
                switch (_mode)
                {
                    case ChannelMode.DC:
                        DC.Background = SelectedBrush;
                        break;
                    case ChannelMode.AC:
                        AC.Background = SelectedBrush;
                        break;
                    case ChannelMode.ABS:
                        ABS.Background = SelectedBrush;
                        break;
                    case ChannelMode.Squared:
                        Squared.Background = SelectedBrush;
                        break;
                }
            }
        }

        private void UpdateColorBarBackground()
        {
            TopColorBar.Background = new SolidColorBrush(_isEnabled ? _assignedColor : DisabledColor);
        }

        private void UpdateGainOffsetDisplay()
        {
            GainOffsetText.Text = $"Gain: {_gain:F2}, Offset: {_offset:F2}";
        }

        private void TopColorBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Toggle the enabled state
            IsEnabled = !IsEnabled;

            // Prevent the event from bubbling up to RootGrid
            e.Handled = true;
        }

        private void ButtonGainUp_Click(object sender, RoutedEventArgs e)
        {
            Gain += 0.1; // Increment gain by 0.1
        }

        private void ButtonGainDown_Click(object sender, RoutedEventArgs e)
        {
            Gain = Math.Max(0.1, Gain - 0.1); // Decrement gain by 0.1, minimum 0.1
        }



        private void ChannelMode_Click(object sender, RoutedEventArgs e)
        {
            if (sender == DC)
                Mode = ChannelMode.DC;
            else if (sender == AC)
                Mode = ChannelMode.AC;
            else if (sender == ABS)
                Mode = ChannelMode.ABS;
            else if (sender == Squared)
                Mode = ChannelMode.Squared;
        }
    }
}
