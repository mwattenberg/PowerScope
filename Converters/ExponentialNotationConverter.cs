using System;
using System.Globalization;
using System.Windows.Data;

namespace PowerScope.Converters
{
    public class ExponentialNotationConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double doubleValue)
            {
                // Show exponential notation only for values larger than 9999 or smaller than 0.001
                if (Math.Abs(doubleValue) > 9999 || (Math.Abs(doubleValue) < 0.001 && Math.Abs(doubleValue) > 0.0))
                {
                    return doubleValue.ToString("E3", CultureInfo.InvariantCulture);
                }
                else
                {
                    return doubleValue.ToString("F3", CultureInfo.InvariantCulture); // Fixed-point notation
                }
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string stringValue && double.TryParse(stringValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double result))
            {
                // Convert exponential notation back to double
                return result;
            }
            return value;
        }
    }
}