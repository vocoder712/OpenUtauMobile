using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenUtauMobile.ViewModels.Converters
{
    public class VolumeLayoutBoundConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is double volume)
            {
                if (volume >= -24d && volume <= 12d)
                {
                    return new Rect((volume + 24d) / 36d, 0.5d, 5, 15);
                }
                return new Rect(1d / 3d, 0.5d, 5, 15);
            }
            return new Rect(1d / 3d, 0.5d, 5, 15);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is Rect rect)
            {
                return rect.X * 36d - 24d;
            }
            return 0d;
        }
    }
}
