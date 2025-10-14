using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenUtauMobile.ViewModels.Converters
{
    public class SKColorMauiColorConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is SKColor skColor)
            {
                return Color.FromRgba(skColor.Red, skColor.Green, skColor.Blue, skColor.Alpha);
            }
            return Colors.Magenta;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is Color mauiColor)
            {
                return new SKColor((byte)mauiColor.Red, (byte)mauiColor.Green, (byte)mauiColor.Blue, (byte)mauiColor.Alpha);
            }
            return SKColors.Magenta;
        }
    }
}
