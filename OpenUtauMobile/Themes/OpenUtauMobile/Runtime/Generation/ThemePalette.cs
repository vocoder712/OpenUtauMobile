using Avalonia.Media;
using Avalonia.Styling;
using MaterialColorUtilities.Palettes;

namespace OpenUtauMobile.Themes.OpenUtauMobile.Runtime;

public sealed class ThemePalette
{
    public ThemePalette(Color seed, ThemeVariant variant, CorePalette corePalette)
    {
        Seed = seed;
        Variant = variant;
        CorePalette = corePalette;
    }

    public Color Seed { get; }
    public ThemeVariant Variant { get; }
    public CorePalette CorePalette { get; }
}