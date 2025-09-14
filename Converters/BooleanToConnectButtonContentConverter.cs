using System;
using System.Globalization;
using System.Windows.Data;

namespace PowerScope.Converters
{
    /// <summary>
    /// Converts boolean IsConnected value to button content text
    /// </summary>
    public class BooleanToConnectButtonContentConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isConnected)
            {
                return isConnected ? "Disconnect" : "Connect";
            }
            return "Connect";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}