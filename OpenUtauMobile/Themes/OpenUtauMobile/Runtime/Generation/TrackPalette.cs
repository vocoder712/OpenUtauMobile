using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace OpenUtauMobile.Themes.OpenUtauMobile.Runtime;

public static class TrackPalette
{
    public sealed record TrackColorInfo(string Name, SolidColorBrush DarkColor, SolidColorBrush LightColor)
    {
        public SolidColorBrush AccentColor => Application.Current?.ActualThemeVariant == ThemeVariant.Dark
            ? DarkColor
            : LightColor;
    }

    public static IReadOnlyList<TrackColorInfo> TrackColors { get; } =
    [
        new("Pink", new SolidColorBrush(Color.Parse("#EC407A")), new SolidColorBrush(Color.Parse("#F48FB1"))),
        new("Red", new SolidColorBrush(Color.Parse("#E53935")), new SolidColorBrush(Color.Parse("#E57373"))),
        new("Orange", new SolidColorBrush(Color.Parse("#FF7043")), new SolidColorBrush(Color.Parse("#FFAB91"))),
        new("Yellow", new SolidColorBrush(Color.Parse("#F9A825")), new SolidColorBrush(Color.Parse("#FDD835"))),
        new("Light Green", new SolidColorBrush(Color.Parse("#C0CA33")), new SolidColorBrush(Color.Parse("#DCE775"))),
        new("Green", new SolidColorBrush(Color.Parse("#43A047")), new SolidColorBrush(Color.Parse("#A5D6A7"))),
        new("Light Blue", new SolidColorBrush(Color.Parse("#29B6F6")), new SolidColorBrush(Color.Parse("#81D4FA"))),
        new("Blue", new SolidColorBrush(Color.Parse("#1E88E5")), new SolidColorBrush(Color.Parse("#90CAF9"))),
        new("Purple", new SolidColorBrush(Color.Parse("#AB47BC")), new SolidColorBrush(Color.Parse("#CE93D8"))),
        new("Pink2", new SolidColorBrush(Color.Parse("#C2185B")), new SolidColorBrush(Color.Parse("#F06292"))),
        new("Red2", new SolidColorBrush(Color.Parse("#B71C1C")), new SolidColorBrush(Color.Parse("#EF5350"))),
        new("Orange2", new SolidColorBrush(Color.Parse("#E64A19")), new SolidColorBrush(Color.Parse("#FF7043"))),
        new("Yellow2", new SolidColorBrush(Color.Parse("#FF7F00")), new SolidColorBrush(Color.Parse("#FFB300"))),
        new("Light Green2", new SolidColorBrush(Color.Parse("#9E9D24")), new SolidColorBrush(Color.Parse("#CDDC39"))),
        new("Green2", new SolidColorBrush(Color.Parse("#1B5E20")), new SolidColorBrush(Color.Parse("#43A047"))),
        new("Light Blue2", new SolidColorBrush(Color.Parse("#0D47A1")), new SolidColorBrush(Color.Parse("#2196F3"))),
        new("Blue2", new SolidColorBrush(Color.Parse("#283593")), new SolidColorBrush(Color.Parse("#5C6BC0"))),
        new("Purple2", new SolidColorBrush(Color.Parse("#4A148C")), new SolidColorBrush(Color.Parse("#AB47BC"))),
    ];

    private static readonly Dictionary<string, TrackColorInfo> TrackColorMap = BuildTrackColorMap();

    private static Dictionary<string, TrackColorInfo> BuildTrackColorMap()
    {
        Dictionary<string, TrackColorInfo> map = new();
        foreach (TrackColorInfo color in TrackColors)
        {
            map[color.Name] = color;
        }

        return map;
    }

    public static TrackColorInfo GetTrackColor(string? name)
    {
        if (string.IsNullOrEmpty(name) || !TrackColorMap.TryGetValue(name, out TrackColorInfo? color))
        {
            return TrackColors[0];
        }

        return color;
    }
}