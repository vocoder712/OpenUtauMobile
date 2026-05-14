using System.Collections.Generic;
using Avalonia.Media;
using Avalonia.Styling;
using MaterialColorUtilities.Palettes;
using MaterialColorUtilities.Schemes;

namespace OpenUtauMobile.Themes.OpenUtauMobile.Runtime;

public static class ThemeGenerator
{
    public static ThemeScheme Generate(Color seed, ThemeVariant variant)
    {
        uint argb = ToArgb(seed);
        CorePalette corePalette = CorePalette.Of(argb);
        ISchemeMapper<CorePalette, Scheme<uint>> mapper =
            variant == ThemeVariant.Dark ? new DarkSchemeMapper() : new LightSchemeMapper();
        Scheme<uint> scheme = mapper.Map(corePalette);

        Dictionary<string, Color> semantic = new()
        {
            // --- Primary ---
            ["Sem.Color.Primary"] = ToColor(scheme.Primary),
            ["Sem.Color.OnPrimary"] = ToColor(scheme.OnPrimary),
            ["Sem.Color.PrimaryContainer"] = ToColor(scheme.PrimaryContainer),
            ["Sem.Color.OnPrimaryContainer"] = ToColor(scheme.OnPrimaryContainer),
            ["Sem.Color.PrimaryHover"] = ToColor(scheme.PrimaryContainer), // 习惯用法
            ["Sem.Color.PrimaryPressed"] = ToColor(scheme.InversePrimary), // 习惯用法

            // --- Secondary ---
            ["Sem.Color.Secondary"] = ToColor(scheme.Secondary),
            ["Sem.Color.OnSecondary"] = ToColor(scheme.OnSecondary),
            ["Sem.Color.SecondaryContainer"] = ToColor(scheme.SecondaryContainer),
            ["Sem.Color.OnSecondaryContainer"] = ToColor(scheme.OnSecondaryContainer),

            // --- Tertiary ---
            ["Sem.Color.Tertiary"] = ToColor(scheme.Tertiary),
            ["Sem.Color.OnTertiary"] = ToColor(scheme.OnTertiary),
            ["Sem.Color.TertiaryContainer"] = ToColor(scheme.TertiaryContainer),
            ["Sem.Color.OnTertiaryContainer"] = ToColor(scheme.OnTertiaryContainer),

            // --- Error ---
            ["Sem.Color.Error"] = ToColor(scheme.Error),
            ["Sem.Color.OnError"] = ToColor(scheme.OnError),
            ["Sem.Color.ErrorContainer"] = ToColor(scheme.ErrorContainer),
            ["Sem.Color.OnErrorContainer"] = ToColor(scheme.OnErrorContainer),

            // --- Surface & Background ---
            ["Sem.Color.Background"] = ToColor(scheme.Background),
            ["Sem.Color.OnBackground"] = ToColor(scheme.OnBackground),
            ["Sem.Color.Surface"] = ToColor(scheme.Surface),
            ["Sem.Color.OnSurface"] = ToColor(scheme.OnSurface),
            ["Sem.Color.SurfaceVariant"] = ToColor(scheme.SurfaceVariant),
            ["Sem.Color.OnSurfaceVariant"] = ToColor(scheme.OnSurfaceVariant),

            // Surface Containers (MD3 新版核心)
            ["Sem.Color.SurfaceDim"] = ToColor(scheme.SurfaceDim),
            ["Sem.Color.SurfaceBright"] = ToColor(scheme.SurfaceBright),
            ["Sem.Color.SurfaceContainerLowest"] = ToColor(scheme.SurfaceContainerLowest),
            ["Sem.Color.SurfaceContainerLow"] = ToColor(scheme.SurfaceContainerLow),
            ["Sem.Color.SurfaceContainer"] = ToColor(scheme.SurfaceContainer),
            ["Sem.Color.SurfaceContainerHigh"] = ToColor(scheme.SurfaceContainerHigh),
            ["Sem.Color.SurfaceContainerHighest"] = ToColor(scheme.SurfaceContainerHighest),

            // --- Inverse ---
            ["Sem.Color.InverseSurface"] = ToColor(scheme.InverseSurface),
            ["Sem.Color.InverseOnSurface"] = ToColor(scheme.InverseOnSurface),
            ["Sem.Color.InversePrimary"] = ToColor(scheme.InversePrimary),

            // --- Outline ---
            ["Sem.Color.Outline"] = ToColor(scheme.Outline),
            ["Sem.Color.OutlineVariant"] = ToColor(scheme.OutlineVariant),

            // --- 钢琴键 ---
            ["Sem.Color.WhiteKey"] = variant == ThemeVariant.Dark
                ? ToColor(scheme.Outline)
                : ToColor(scheme.Surface),
            ["Sem.Color.BlackKey"] = variant == ThemeVariant.Dark
                ? ToColor(scheme.Surface)
                : ToColor(scheme.Outline),
            ["Sem.Color.CenterKey"] = ToColor(scheme.PrimaryContainer),
            ["Sem.Color.WhiteKey.Background"] = variant == ThemeVariant.Dark
                ? ToColor(scheme.SurfaceContainer)
                : ToColor(scheme.Surface),
            ["Sem.Color.BlackKey.Background"] = variant == ThemeVariant.Dark
                ? ToColor(scheme.Surface)
                : ToColor(scheme.SurfaceContainer),

            // --- Custom / Derived ---
            ["Sem.Color.Shadow"] = ToColor(scheme.Shadow),
            ["Sem.Color.Scrim"] = WithAlpha(ToColor(scheme.Shadow), 0x99),
            ["Sem.Color.GlassOverlay"] = WithAlpha(ToColor(scheme.Surface), 0x99),

            // --- Functional (Warning/Success) ---
            ["Sem.Color.Warning"] = ToColor(corePalette.Tertiary[variant == ThemeVariant.Dark ? 60u : 40u]),
            ["Sem.Color.WarningContainer"] = ToColor(corePalette.Tertiary[variant == ThemeVariant.Dark ? 30u : 90u]),
            ["Sem.Color.Success"] = ToColor(corePalette.Secondary[variant == ThemeVariant.Dark ? 60u : 40u]),
            ["Sem.Color.SuccessContainer"] = ToColor(corePalette.Secondary[variant == ThemeVariant.Dark ? 30u : 90u]),
        };

        return new ThemeScheme(new ThemePalette(seed, variant, corePalette), semantic);
    }

    private static uint ToArgb(Color color)
    {
        return ((uint)color.A << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
    }

    private static Color ToColor(uint argb)
    {
        byte a = (byte)((argb >> 24) & 0xFF);
        byte r = (byte)((argb >> 16) & 0xFF);
        byte g = (byte)((argb >> 8) & 0xFF);
        byte b = (byte)(argb & 0xFF);
        return Color.FromArgb(a, r, g, b);
    }

    private static Color WithAlpha(Color color, byte alpha)
    {
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }
}