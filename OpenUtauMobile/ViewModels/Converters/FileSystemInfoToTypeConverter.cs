using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenUtauMobile.ViewModels.Converters
{
    /// <summary>
    /// 文件系统信息类型转换器
    /// </summary>
    public class FileSystemInfoToTypeConverter : IValueConverter
    {
        public static readonly FileSystemInfoToTypeConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value switch
            {
                DirectoryInfo => "Directory",
                FileInfo => "File",
                _ => "Unknown"
            };
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
