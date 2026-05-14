using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Themes.OpenUtauMobile.Runtime;

namespace OpenUtauMobile.Tools;

/// <summary>
/// USingerType => SolidColorBrush  (颜色由 ThemeResources 统一管理，支持明暗主题)
/// </summary>
public class SingerTypeToColorConverter : IValueConverter
{
    public static readonly SingerTypeToColorConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is USingerType singerType)
        {
            return singerType switch
            {
                USingerType.Classic => ThemeResources.GetBrush("Sem.Color.Singer.Classic"),
                USingerType.Enunu => ThemeResources.GetBrush("Sem.Color.Singer.Enunu"),
                USingerType.DiffSinger => ThemeResources.GetBrush("Sem.Color.Singer.DiffSinger"),
                USingerType.Vogen => ThemeResources.GetBrush("Sem.Color.Singer.Vogen"),
                USingerType.Voicevox => ThemeResources.GetBrush("Sem.Color.Singer.Voicevox"),
                _ => ThemeResources.GetBrush("Sem.Color.Singer.Unknown"),
            };
        }

        return ThemeResources.GetBrush("Sem.Color.Singer.Unknown");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => AvaloniaProperty.UnsetValue;
}

/// <summary>
/// int Count => bool: true when count == 0 (used for empty-state visibility)
/// </summary>
public class CountZeroToBoolConverter : IValueConverter
{
    public static readonly CountZeroToBoolConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is 0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => AvaloniaProperty.UnsetValue;
}

/// <summary>
/// int Count => bool: true when count > 0 (used for list visibility)
/// </summary>
public class CountNonZeroToBoolConverter : IValueConverter
{
    public static readonly CountNonZeroToBoolConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is > 0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => AvaloniaProperty.UnsetValue;
}

/// <summary>
/// Full file path => file name only (e.g. "song.ustx")
/// </summary>
public class PathToFileNameConverter : IValueConverter
{
    public static readonly PathToFileNameConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string s ? Path.GetFileName(s) : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => AvaloniaProperty.UnsetValue;
}

/// <summary>
/// Full file path => directory portion (e.g. "/sdcard/Projects")
/// </summary>
public class PathToDirectoryConverter : IValueConverter
{
    public static readonly PathToDirectoryConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string s ? (Path.GetDirectoryName(s) ?? string.Empty) : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => AvaloniaProperty.UnsetValue;
}

/// <summary>
/// USingerType => string (label)
/// </summary>
public class SingerTypeToLabelConverter : IValueConverter
{
    public static readonly SingerTypeToLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is USingerType singerType)
        {
            return singerType switch
            {
                USingerType.Classic => "UTAU",
                USingerType.Enunu => "ENUNU",
                USingerType.DiffSinger => "DiffSinger",
                USingerType.Vogen => "Vogen",
                USingerType.Voicevox => "Voicevox",
                _ => "Unknown",
            };
        }

        return "Unknown";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => AvaloniaProperty.UnsetValue;
}

/// <summary>
/// byte[] => Bitmap (avatar image)
/// </summary>
public class AvatarDataToBitmapConverter : IValueConverter
{
    public static readonly AvatarDataToBitmapConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is byte[] { Length: > 0 } data)
        {
            try
            {
                using MemoryStream ms = new MemoryStream(data);
                return new Bitmap(ms);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => AvaloniaProperty.UnsetValue;
}

/// <summary>
/// Returns true when the value is non-null — used to toggle avatar image visibility.
/// </summary>
public class NullToBoolConverter : IValueConverter
{
    public static readonly NullToBoolConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value != null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => AvaloniaProperty.UnsetValue;
}

/// <summary>
/// Returns the first uppercased character of a string — used as avatar fallback initial.
/// </summary>
public class StringToInitialConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string { Length: > 0 } s)
            return s[0].ToString().ToUpper(culture);
        return "?";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => AvaloniaProperty.UnsetValue;
}

/// <summary>
/// Enum value == parameter => bool (used for SettingsCategory selection visibility).
/// </summary>
public class EnumEqualConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value != null && parameter != null && value.ToString() == parameter.ToString();

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => AvaloniaProperty.UnsetValue;
}

/// <summary>
/// Enum value != parameter => bool.
/// </summary>
public class EnumNotEqualConverter : IValueConverter
{
    public static readonly EnumNotEqualConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value == null || parameter == null || value.ToString() != parameter.ToString();

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => AvaloniaProperty.UnsetValue;
}

/// <summary>
/// FileSystemInfo => bool: true when item is FileInfo (not DirectoryInfo).
/// Pass ConverterParameter="invert" to get the opposite (i.e. true for directories).
/// </summary>
public class IsFileConverter : IValueConverter
{
    public static readonly IsFileConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isFile = value is FileInfo;
        if (parameter is "invert") return !isFile;
        return isFile;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => AvaloniaProperty.UnsetValue;
}

/// <summary>
/// FileSystemInfo => human-readable file size string (e.g. "1.2 MB").
/// Returns empty string for directories.
/// </summary>
public class FileSizeConverter : IValueConverter
{
    public static readonly FileSizeConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not FileInfo fi) return string.Empty;
        long bytes = fi.Length;
        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
            _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB",
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => AvaloniaProperty.UnsetValue;
}

/// <summary>
/// 枚举值与布尔值的转换器，若绑定值等于参数值则返回 true，反之返回 false。
/// </summary>
public class EnumToBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null || parameter == null) return false;
        return value.ToString()!.Equals(parameter.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool and true && parameter != null)
        {
            try
            {
                return Enum.Parse(targetType, parameter.ToString()!);
            }
            catch
            {
                return AvaloniaProperty.UnsetValue;
            }
        }

        return AvaloniaProperty.UnsetValue;
    }
}

/// <summary>
/// int SnapDiv => display label ("自动" / "关" / "1/N").
/// Calls ViewConstants.SnapDivToLabel.
/// </summary>
public class SnapDivToLabelConverter : IValueConverter
{
    public static readonly SnapDivToLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int div ? ViewConstants.SnapDivToLabel(div) : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => AvaloniaProperty.UnsetValue;
}

/// <summary>
/// bool => inverted bool (true becomes false, false becomes true).
/// </summary>
public class BoolInvertConverter : IValueConverter
{
    public static readonly BoolInvertConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is false;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is false;
}

public class EncodingNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value as Encoding)?.EncodingName ?? string.Empty;

    // Display-only converter: ignore reverse updates to avoid binding exceptions on controls with TwoWay text.
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        AvaloniaProperty.UnsetValue;
}

/// <summary>
/// ProgressValue (0-100) and Panel Width => Width for progress bar.
/// </summary>
public class ProgressToWidthConverter : IMultiValueConverter
{
    public static readonly ProgressToWidthConverter Instance = new();

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2)
        {
            return 0.0;
        }

        if (!ConverterHelpers.TryGetDouble(values[0], out double progress) ||
            !ConverterHelpers.TryGetDouble(values[1], out double width))
        {
            return 0.0;
        }

        progress = Math.Clamp(progress, 0.0, 100.0);
        width = Math.Max(0.0, width);
        return progress / 100.0 * width;
    }

    // ConvertBack is not needed for one-way usage; throw to clearly indicate it's unsupported.
    public object ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converter that passes through the resource value as-is.
/// Used to bind DynamicResource to Css properties that don't support direct binding.
/// </summary>
public class PassThroughConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class DoubleToGridLengthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (ConverterHelpers.TryGetDouble(value, out double number))
        {
            return new GridLength(number);
        }

        return new GridLength(0);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is GridLength gl)
        {
            return gl.Value;
        }

        return 0.0;
    }
}

internal static class ConverterHelpers
{
    public static bool TryGetDouble(object? value, out double number)
    {
        switch (value)
        {
            case double d:
                number = d;
                return true;
            case float f:
                number = f;
                return true;
            case int i:
                number = i;
                return true;
            case long l:
                number = l;
                return true;
            case decimal m:
                number = (double)m;
                return true;
            default:
                number = 0;
                return false;
        }
    }
}