using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using PowerScope.Model;

namespace PowerScope.Converters
{
    public class ChannelColorBarBrushConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            Color displayColor = Colors.Gray;
            if (values[0] is Color colorValue)
                displayColor = colorValue;

            bool isVirtual = false;
            if (values[1] is bool boolValue)
                isVirtual = boolValue;

            Channel parentA = values[2] as Channel;
            Channel parentB = values[3] as Channel;

            if (!isVirtual)
                return new SolidColorBrush(displayColor);

            Color startColor = GetParentColor(parentA);

            if (parentB != null)
            {
                Color parentBColor = GetParentColor(parentB);

                LinearGradientBrush gradientBrush = new LinearGradientBrush();
                gradientBrush.StartPoint = new Point(0, 0.5);
                gradientBrush.EndPoint = new Point(1, 0.5);
                gradientBrush.GradientStops.Add(new GradientStop(startColor, 0.0));
                gradientBrush.GradientStops.Add(new GradientStop(parentBColor, 0.25));
                gradientBrush.GradientStops.Add(new GradientStop(displayColor, 0.5));
                return gradientBrush;
            }
            else
            {
                LinearGradientBrush gradientBrush = new LinearGradientBrush();
                gradientBrush.StartPoint = new Point(0, 0.5);
                gradientBrush.EndPoint = new Point(1, 0.5);
                gradientBrush.GradientStops.Add(new GradientStop(startColor, 0.0));
                gradientBrush.GradientStops.Add(new GradientStop(displayColor, 0.5));
                return gradientBrush;
            }
        }

        private static Color GetParentColor(Channel channel)
        {
            if (channel == null)
                return Colors.Black;
            if (channel.OwnerStream is ConstantDataStream)
                return Colors.DimGray;
            return channel.Settings.DisplayColor;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
