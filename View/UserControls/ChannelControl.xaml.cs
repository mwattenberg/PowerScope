using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SerialPlotDN_WPF.Model;

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

        /// <summary>
        /// Gets the ChannelSettings from DataContext
        /// </summary>
        public ChannelSettings Settings => DataContext as ChannelSettings;

        // Legacy properties for backward compatibility
        public Color Color
        {
            get => Settings?.Color ?? Colors.Gray;
            set
            {
                if (Settings != null)
                    Settings.Color = value;
            }
        }

        public bool IsEnabled
        {
            get => Settings?.IsEnabled ?? true;
            set
            {
                if (Settings != null)
                    Settings.IsEnabled = value;
            }
        }

        public string Label
        {
            get => Settings?.Label ?? "Channel";
            set
            {
                if (Settings != null)
                    Settings.Label = value;
            }
        }

        public CouplingMode Coupling
        {
            get => Settings?.Coupling ?? CouplingMode.DC;
            set
            {
                if (Settings != null)
                    Settings.Coupling = value;
            }
        }

        public FilterMode Filter
        {
            get => Settings?.Filter ?? FilterMode.None;
            set
            {
                if (Settings != null)
                    Settings.Filter = value;
            }
        }

        public double Gain
        {
            get => Settings?.Gain ?? 1.0;
            set
            {
                if (Settings != null)
                    Settings.Gain = value;
            }
        }

        public double Offset
        {
            get => Settings?.Offset ?? 0.0;
            set
            {
                if (Settings != null)
                    Settings.Offset = value;
            }
        }

        // Events
        public event RoutedEventHandler? ChannelEnabledChanged;
        public event RoutedEventHandler? GainChanged;

        public ChannelControl()
        {
            InitializeComponent();
            
            // Subscribe to DataContext changes
            DataContextChanged += ChannelControl_DataContextChanged;
        }

        private void ChannelControl_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // Unsubscribe from old settings
            if (e.OldValue is ChannelSettings oldSettings)
            {
                oldSettings.PropertyChanged -= Settings_PropertyChanged;
            }

            // Subscribe to new settings
            if (e.NewValue is ChannelSettings newSettings)
            {
                newSettings.PropertyChanged += Settings_PropertyChanged;
                UpdateUIFromSettings();
            }
        }

        private void Settings_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            Dispatcher.BeginInvoke(() => 
            {
                switch (e.PropertyName)
                {
                    case nameof(ChannelSettings.Color):
                    case nameof(ChannelSettings.IsEnabled):
                        UpdateColorBarBackground();
                        if (e.PropertyName == nameof(ChannelSettings.IsEnabled))
                            ChannelEnabledChanged?.Invoke(this, new RoutedEventArgs());
                        break;
                    case nameof(ChannelSettings.Coupling):
                        UpdateCouplingButtons();
                        break;
                    case nameof(ChannelSettings.Filter):
                        UpdateFilterButtons();
                        break;
                    case nameof(ChannelSettings.Gain):
                        GainChanged?.Invoke(this, new RoutedEventArgs());
                        break;
                    case nameof(ChannelSettings.Label):
                        UpdateLabel();
                        break;
                }
            });
        }

        private void UpdateUIFromSettings()
        {
            if (Settings != null)
            {
                UpdateColorBarBackground();
                UpdateCouplingButtons();
                UpdateFilterButtons();
                UpdateLabel();
            }
        }

        private void UpdateColorBarBackground()
        {
            if (TopColorBar != null && Settings != null)
            {
                TopColorBar.Background = new SolidColorBrush(Settings.IsEnabled ? Settings.Color : DisabledColor);
            }
        }

        private void UpdateLabel()
        {
            if (ChannelLabelTextBox != null && Settings != null)
            {
                ChannelLabelTextBox.Text = Settings.Label;
            }
        }

        private void UpdateCouplingButtons()
        {
            if (Settings == null) return;

            // Reset all button backgrounds to default
            DC.Background = DefaultBrush;
            AC.Background = DefaultBrush;
            
            // Set selected button background
            switch (Settings.Coupling)
            {
                case CouplingMode.DC:
                    DC.Background = SelectedBrush;
                    break;
                case CouplingMode.AC:
                    AC.Background = SelectedBrush;
                    break;
            }
        }

        private void UpdateFilterButtons()
        {
            if (Settings == null) return;

            // Reset all button backgrounds to default
            LPF.Background = DefaultBrush;
            HPF.Background = DefaultBrush;
            ABS.Background = DefaultBrush;
            Squared.Background = DefaultBrush;
            
            // Set selected button background
            switch (Settings.Filter)
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
                case FilterMode.None:
                default:
                    break;
            }
        }

        private void TopColorBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Toggle the enabled state
            if (Settings != null)
            {
                Settings.IsEnabled = !Settings.IsEnabled;
            }

            // Prevent the event from bubbling up to RootGrid
            e.Handled = true;
        }

        private void ButtonGainUp_Click(object sender, RoutedEventArgs e)
        {
            if (Settings != null)
            {
                Settings.Gain = Math.Min(Settings.Gain * 2, 16); // Double gain, max 16
            }
        }

        private void ButtonGainDown_Click(object sender, RoutedEventArgs e)
        {
            if (Settings != null)
            {
                Settings.Gain = Math.Max(Settings.Gain / 2, 0.125); // Halve gain, minimum 0.125
            }
        }

        private void CouplingMode_Click(object sender, RoutedEventArgs e)
        {
            if (Settings == null) return;

            if (sender == DC)
                Settings.Coupling = CouplingMode.DC;
            else if (sender == AC)
                Settings.Coupling = CouplingMode.AC;
        }

        private void FilterMode_Click(object sender, RoutedEventArgs e)
        {
            if (Settings == null) return;

            if (sender == LPF)
                Settings.Filter = FilterMode.LPF;
            else if (sender == HPF)
                Settings.Filter = FilterMode.HPF;
            else if (sender == ABS)
                Settings.Filter = FilterMode.ABS;
            else if (sender == Squared)
                Settings.Filter = FilterMode.Squared;
        }
    }
}
