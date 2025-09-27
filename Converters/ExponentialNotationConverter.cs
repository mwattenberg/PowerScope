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
                if(Math.Abs(doubleValue) == 0.0)
                    return "0";
                else if (Math.Abs(doubleValue) > 99999)
                    return doubleValue.ToString("E2", CultureInfo.InvariantCulture); // Fixed-point notation
                else if (Math.Abs(doubleValue) > 9999)
                    return doubleValue.ToString("F0", CultureInfo.InvariantCulture); // Fixed-point notation
                else if(Math.Abs(doubleValue) > 999)
                    return doubleValue.ToString("F1", CultureInfo.InvariantCulture); // Fixed-point notation
                else if(Math.Abs(doubleValue) > 99)
                    return doubleValue.ToString("F2", CultureInfo.InvariantCulture); // Fixed-point notation
                else if (Math.Abs(doubleValue) > 9)
                    return doubleValue.ToString("F3", CultureInfo.InvariantCulture); // Fixed-point notation
                else if (Math.Abs(doubleValue) > 0.001)
                    return doubleValue.ToString("F4", CultureInfo.InvariantCulture); // Fixed-point notation
                else return doubleValue.ToString("E2", CultureInfo.InvariantCulture); // Fixed-point notation
                //}
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string stringValue && double.TryParse(stringValue, NumberStyles.Float | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out double result))
            {
                // Convert exponential notation back to double
                return result;
            }
            return value;
        }
    }
}