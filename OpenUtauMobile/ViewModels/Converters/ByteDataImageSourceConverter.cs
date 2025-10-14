using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenUtauMobile.ViewModels.Converters
{
    public class ByteDataImageSourceConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is byte[] byteArray)
            {
                return ImageSource.FromStream(() => new MemoryStream(byteArray));
            }
            return null;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is ImageSource imageSource)
            {
                if (imageSource is StreamImageSource streamImageSource)
                {
                    using (var stream = streamImageSource.Stream(CancellationToken.None).Result)
                    {
                        using (var memoryStream = new MemoryStream())
                        {
                            stream.CopyTo(memoryStream);
                            return memoryStream.ToArray();
                        }
                    }
                }
            } 
            return null;
        }
    }
}
