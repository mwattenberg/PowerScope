using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using PowerScope.Model;

namespace PowerScope.Converters
{
    /// <summary>
    /// Converter that returns a gradient brush for virtual channels or a solid brush for physical channels
    /// Used to visually distinguish virtual channels in the ChannelControl UI
    /// </summary>
    public class ChannelTypeColorBrushConverter : IValueConverter
    {
     /// <summary>
     /// Converts a Color to a Brush (solid or gradient based on channel type)
        /// </summary>
     /// <param name="value">The color (from ChannelSettings.Color)</param>
        /// <param name="targetType">Expected to be Brush</param>
    /// <param name="parameter">Should contain a reference to the Channel object to detect if virtual</param>
        /// <param name="culture">Culture info for conversion</param>
        /// <returns>A SolidColorBrush for physical channels or a LinearGradientBrush for virtual channels</returns>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
  if (!(value is Color color))
     return Brushes.Gray;

 // Check if the parameter is a Channel and if it's virtual
 bool isVirtualChannel = false;
            if (parameter is Channel channel)
  {
        isVirtualChannel = channel.OwnerStream is VirtualDataStream;
  }

            // For virtual channels, create a gradient brush
 if (isVirtualChannel)
            {
     LinearGradientBrush gradientBrush = new LinearGradientBrush
         {
  StartPoint = new System.Windows.Point(0, 0),
        EndPoint = new System.Windows.Point(1, 0) // Horizontal gradient
                };

             // Create lighter and darker versions of the color
                Color lighterColor = LightenColor(color, 0.4);  // 40% lighter
     Color darkerColor = DarkenColor(color, 0.3);   // 30% darker

          // Add gradient stops
    gradientBrush.GradientStops.Add(new GradientStop(lighterColor, 0.0));
                gradientBrush.GradientStops.Add(new GradientStop(color, 0.5));
       gradientBrush.GradientStops.Add(new GradientStop(darkerColor, 1.0));

           gradientBrush.Freeze();
      return gradientBrush;
            }

  // For physical channels, return solid color brush
            SolidColorBrush solidBrush = new SolidColorBrush(color);
     solidBrush.Freeze();
            return solidBrush;
        }

      public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        /// <summary>
     /// Lightens a color by a given percentage
        /// </summary>
   private Color LightenColor(Color color, double percentage)
        {
            byte r = (byte)Math.Min(255, color.R + (255 - color.R) * percentage);
      byte g = (byte)Math.Min(255, color.G + (255 - color.G) * percentage);
    byte b = (byte)Math.Min(255, color.B + (255 - color.B) * percentage);
        return Color.FromArgb(color.A, r, g, b);
        }

        /// <summary>
        /// Darkens a color by a given percentage
        /// </summary>
        private Color DarkenColor(Color color, double percentage)
      {
      byte r = (byte)(color.R * (1 - percentage));
         byte g = (byte)(color.G * (1 - percentage));
   byte b = (byte)(color.B * (1 - percentage));
            return Color.FromArgb(color.A, r, g, b);
 }
    }
}
