using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;


namespace SerialPlotDN_WPF.View.UserControls
{
    public partial class ChannelControl : UserControl
    {
        // ChannelMode enum for channel processing modes
        public enum FilterMode
        {
            LPF,
            HPF,
            ABS,
            Squared,
            None
        }

        public enum CouplingMode
        {
            DC,
            AC
        }

        private static readonly Color DisabledColor = Colors.Gray;
        private static readonly Brush SelectedBrush = new SolidColorBrush(Colors.LimeGreen);
        private static readonly Brush DefaultBrush = new SolidColorBrush(DisabledColor);
       
        
        public double _gain = 1.0;
        public double _offset = 0.0;
        private CouplingMode _coupling;
        private FilterMode _filter;

        private Color _color;
        public Color Color
        {
            get => _color;
            set
            {
                _color = value;
                UpdateColorBarBackground();
            }
        }

        private bool _isEnabled = true;
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

        public string Label
        {
            get => ChannelLabelTextBox.Text;
            set
            {
                if (ChannelLabelTextBox.Text != value)
                {
                    ChannelLabelTextBox.Text = value;
                    if (ChannelLabelTextBox != null)
                        ChannelLabelTextBox.Text = ChannelLabelTextBox.Text;
                }
            }
        }

        // Events
        public event RoutedEventHandler? ChannelEnabledChanged;
        public event RoutedEventHandler? GainChanged;

        public ChannelControl()
        {
            InitializeComponent();
            UpdateGainOffsetDisplay();
            this.Coupling = CouplingMode.DC; // Default mode
            this.Filter = FilterMode.None; // Default filter mode
            this.Color = Colors.Gray; // Default color
            this.Label = "Channel"; // Default label
        }

        public CouplingMode Coupling
        {
            get => _coupling;
            set
            {
                _coupling = value;
                // Reset all button backgrounds to default
                DC.Background = DefaultBrush;
                AC.Background = DefaultBrush;
                // Set selected button background
                switch (_coupling)
                {
                    case CouplingMode.DC:
                        DC.Background = SelectedBrush;
                        break;
                    case CouplingMode.AC:
                        AC.Background = SelectedBrush;
                        break;
                }
            }
        }

        public FilterMode Filter
        {
            get => _filter;
            set
            {
                _filter = value;
                // Reset all button backgrounds to default
                LPF.Background = DefaultBrush;
                HPF.Background = DefaultBrush;
                ABS.Background = DefaultBrush;
                Squared.Background = DefaultBrush;
                // Set selected button background
                switch (_filter)
                {
                    case FilterMode.LPF:
                        LPF.Background = SelectedBrush;
                        break;
                    case FilterMode.HPF:
                        HPF.Background = SelectedBrush;
                        break;
                    case FilterMode.ABS:
                        ABS.Background = SelectedBrush;
                        break;
                    case FilterMode.Squared:
                        Squared.Background = SelectedBrush;
                        break;
                    case FilterMode.None: //nothing for this mode
                    default:
                        break;
                }
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



        private void UpdateColorBarBackground()
        {
            TopColorBar.Background = new SolidColorBrush(_isEnabled ? _color : DisabledColor);
        }

        private void UpdateGainOffsetDisplay()
        {
            if(_offset > 0)
                GainOffsetText.Text = $"= y * {_gain:F2} + {_offset:F2}";
            else
                GainOffsetText.Text = $"= y *{_gain:F2} - {_offset:F2}";
            
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
            Gain = Math.Min(Gain * 2,16); // Increment gain by 0.1
        }

        private void ButtonGainDown_Click(object sender, RoutedEventArgs e)
        {
            Gain = Math.Max(Gain/ 2, 0.125); // Decrement gain by 0.1, minimum 0.1
        }

        private void CouplingMode_Click(object sender, RoutedEventArgs e)
        {
            if (sender == DC)
                this.Coupling = CouplingMode.DC;
            else if (sender == AC)
                this.Coupling = CouplingMode.AC;
        }

        private void FilterMode_Click(object sender, RoutedEventArgs e)
        {
            if (sender == LPF)
                this.Filter = FilterMode.LPF;
            else if (sender == HPF)
                this.Filter = FilterMode.HPF;
            else if (sender == ABS)
                this.Filter = FilterMode.ABS;
            else if (sender == Squared)
                this.Filter = FilterMode.Squared;
        }
    }
}
