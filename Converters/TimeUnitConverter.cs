using System;
using System.Globalization;
using System.Windows.Data;

namespace PowerScope.Converters
{
    /// <summary>
    /// Converter that formats time values with "s" suffix for seconds
    /// Uses the same smart formatting logic as ExponentialNotationConverter
    /// </summary>
    public class TimeUnitConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double doubleValue)
            {
                string formattedValue;
                
                if (Math.Abs(doubleValue) == 0.0)
                    formattedValue = "0";
                else if (Math.Abs(doubleValue) > 99999)
                    formattedValue = doubleValue.ToString("E2", CultureInfo.InvariantCulture);
                else if (Math.Abs(doubleValue) > 9999)
                    formattedValue = doubleValue.ToString("F0", CultureInfo.InvariantCulture);
                else if (Math.Abs(doubleValue) > 999)
                    formattedValue = doubleValue.ToString("F1", CultureInfo.InvariantCulture);
                else if (Math.Abs(doubleValue) > 99)
                    formattedValue = doubleValue.ToString("F2", CultureInfo.InvariantCulture);
                else if (Math.Abs(doubleValue) > 9)
                    formattedValue = doubleValue.ToString("F3", CultureInfo.InvariantCulture);
                else if (Math.Abs(doubleValue) > 0.001)
                    formattedValue = doubleValue.ToString("F4", CultureInfo.InvariantCulture);
                else
                    formattedValue = doubleValue.ToString("E2", CultureInfo.InvariantCulture);
                
                return formattedValue + " s";
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string stringValue)
            {
                // Remove " s" suffix if present and try to parse
                string cleanValue = stringValue.Replace(" s", "").Trim();
                if (double.TryParse(cleanValue, NumberStyles.Float | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out double result))
                {
                    return result;
                }
            }
            return value;
        }
    }
}