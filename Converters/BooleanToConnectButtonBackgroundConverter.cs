using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SerialPlotDN_WPF.Converters
{
    /// <summary>
    /// Converts boolean IsConnected value to button background brush
    /// </summary>
    public class BooleanToConnectButtonBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isConnected)
            {
                return isConnected ? new SolidColorBrush(Colors.OrangeRed) : new SolidColorBrush(Colors.LightGreen);
            }
            return new SolidColorBrush(Colors.LightGreen);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}