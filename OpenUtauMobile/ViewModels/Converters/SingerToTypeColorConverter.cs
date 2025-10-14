using OpenUtau.Core.Ustx;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenUtauMobile.ViewModels.Converters
{
    public class SingerToTypeColorConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is USinger singer)
            {
                switch (singer.SingerType)
                {
                    case USingerType.Vogen:
                        return Color.FromArgb("#FFB74D");
                    case USingerType.Enunu:
                        return Color.FromArgb("#4DB6AC");
                    case USingerType.Voicevox:
                        return Color.FromArgb("#9575CD");
                    case USingerType.Classic:
                        return Color.FromArgb("#90A4AE");
                    case USingerType.DiffSinger:
                        return Color.FromArgb("#F06292");
                    default:
                        return Color.FromArgb("#E0E0E0");
                }
            }
            return Color.FromArgb("#E0E0E0");
        }
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
