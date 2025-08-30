using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using SerialPlotDN_WPF.Model;

namespace SerialPlotDN_WPF.View.UserControls
{
    public class ColorToBrushConverter : IValueConverter
    {
        public static readonly ColorToBrushConverter Instance = new ColorToBrushConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Color color)
                return new SolidColorBrush(color);
            return new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is SolidColorBrush brush)
                return brush.Color;
            return Colors.Gray;
        }
    }

    public partial class ChannelControl : UserControl
    {
        private static readonly Color DisabledColor = Colors.Gray;
        private static readonly Brush SelectedBrush = new SolidColorBrush(Colors.LimeGreen);
        private static readonly Brush DefaultBrush = new SolidColorBrush(DisabledColor);

        /// <summary>
        /// Gets the ChannelSettings from DataContext
        /// </summary>
        public ChannelSettings Settings 
        { 
            get 
            { 
                return DataContext as ChannelSettings; 
            } 
        }

        // Events
        public event RoutedEventHandler ChannelEnabledChanged;
        public event RoutedEventHandler GainChanged;

        public ChannelControl()
        {
            InitializeComponent();
            Loaded += ChannelControl_Loaded;
        }

        private void ChannelControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (Settings != null)
            {
                Settings.PropertyChanged += Settings_PropertyChanged;
            }
        }

        private void Settings_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            Action updateAction = delegate()
            {
                if (e.PropertyName == nameof(ChannelSettings.IsEnabled))
                {
                    if (ChannelEnabledChanged != null)
                        ChannelEnabledChanged.Invoke(this, new RoutedEventArgs());
                }
                else if (e.PropertyName == nameof(ChannelSettings.Gain))
                {
                    if (GainChanged != null)
                        GainChanged.Invoke(this, new RoutedEventArgs());
                }
            };
            Dispatcher.BeginInvoke(updateAction);
        }

        private void TopColorBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Toggle the enabled state
            if (Settings != null)
                Settings.IsEnabled = !Settings.IsEnabled;

            // Prevent the event from bubbling up to RootGrid
            e.Handled = true;
        }

        private void ButtonGainUp_Click(object sender, RoutedEventArgs e)
        {
            if (Settings != null)
                Settings.Gain = Math.Min(Settings.Gain * 2, 16); // Double gain, max 16
        }

        private void ButtonGainDown_Click(object sender, RoutedEventArgs e)
        {
            if (Settings != null)
                Settings.Gain = Math.Max(Settings.Gain / 2, 0.125); // Halve gain, minimum 0.125
        }

        private void ButtonFilters_Click(object sender, RoutedEventArgs e)
        {
            if (Settings != null)
            {
                var filterWindow = new View.UserForms.FilterConfigWindow();
                filterWindow.DataContext = Settings;
                filterWindow.Owner = Window.GetWindow(this);
                filterWindow.ShowDialog();
            }
        }
    }
}
