using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenUtauMobile.ViewModels.Converters
{
    public class TrackColorConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string colorString)
            {
                Color? color = ViewConstants.TrackMauiColors.GetValueOrDefault(colorString);
                return color ?? Color.Parse("#00FFFFFF");
            }
            return Color.Parse("#00FFFFFF");
        }
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is Color color)
            {
                return color.ToString();
            }
            return null;
        }
    }
}
