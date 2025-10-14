using System.Globalization;

namespace OpenUtauMobile.ViewModels.Converters
{
    /// <summary>
    /// 将null值转换为StrokeThickness的转换器
    /// </summary>
    public class NullToStrokeThicknessConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            // 如果Singer为null，返回1（显示虚线边框）
            // 如果Singer不为null，返回0（不显示边框）
            return value == null ? 1.0 : 0.0;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}