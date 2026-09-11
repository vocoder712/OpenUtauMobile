using System.Collections.Generic;
using Avalonia.Media;

namespace OpenUtauMobile.Themes.OpenUtauMobile.Runtime;

public sealed class ThemeScheme
{
    public ThemeScheme(ThemePalette palette, IReadOnlyDictionary<string, Color> semanticColors)
    {
        Palette = palette;
        SemanticColors = semanticColors;
    }

    public ThemePalette Palette { get; }
    public IReadOnlyDictionary<string, Color> SemanticColors { get; }
}