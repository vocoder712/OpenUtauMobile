using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using ReactiveUI;

namespace OpenUtauMobile.Themes.OpenUtauMobile.Runtime;

public static class ThemeManagerV2
{
    private static readonly ThemeResourceBridge ResourceBridge = new();

    private static readonly IReadOnlyDictionary<string, string> LegacyBrushAliases = new Dictionary<string, string>
    {
        ["bg-0"] = "Sem.Color.Surface",
        ["bg-1"] = "Sem.Color.SurfaceContainerLow",
        ["bg-2"] = "Sem.Color.SurfaceContainer",
        ["bg-3"] = "Sem.Color.SurfaceContainerHigh",
        ["bg-4"] = "Sem.Color.SurfaceContainerHighest",

        ["text-0"] = "Sem.Color.OnSurface",
        ["text-1"] = "Sem.Color.OnSurfaceVariant",
        ["text-2"] = "Sem.Color.Outline",
        ["text-3"] = "Sem.Color.Outline",
        ["text-4"] = "Sem.Color.OutlineVariant",

        ["border-0"] = "Sem.Color.Outline",
        ["border-1"] = "Sem.Color.OutlineVariant",
        ["border-2"] = "Sem.Color.SurfaceContainerHigh",

        ["fill-0"] = "Sem.Color.SurfaceContainer",
        ["fill-1"] = "Sem.Color.SurfaceContainerLow",
        ["fill-2"] = "Sem.Color.Surface",

        ["color-primary"] = "Sem.Color.Primary",
        ["color-primary-hover"] = "Sem.Color.PrimaryHover",
        ["color-primary-active"] = "Sem.Color.PrimaryPressed",
        ["color-primary-light"] = "Sem.Color.PrimaryContainer",
        ["color-primary-lighter"] = "Sem.Color.SurfaceContainerLow",

        ["color-danger"] = "Sem.Color.Error",
        ["color-danger-hover"] = "Sem.Color.ErrorContainer",
        ["color-danger-active"] = "Sem.Color.ErrorContainer",
        ["color-danger-light"] = "Sem.Color.ErrorContainer",
        ["color-danger-lighter"] = "Sem.Color.ErrorContainer",

        ["color-warning"] = "Sem.Color.Warning",
        ["color-warning-hover"] = "Sem.Color.Warning",
        ["color-warning-active"] = "Sem.Color.Warning",
        ["color-warning-light"] = "Sem.Color.WarningContainer",
        ["color-warning-lighter"] = "Sem.Color.WarningContainer",

        ["color-success"] = "Sem.Color.Success",
        ["color-success-hover"] = "Sem.Color.Success",
        ["color-success-active"] = "Sem.Color.Success",
        ["color-success-light"] = "Sem.Color.SuccessContainer",
        ["color-success-lighter"] = "Sem.Color.SuccessContainer",

        ["overlay-medium"] = "Sem.Color.Scrim",
        ["overlay-glass"] = "Sem.Color.GlassOverlay",
        ["shadow-color"] = "Sem.Color.Shadow",

        ["toast-background"] = "Sem.Color.Scrim",
        ["toast-foreground"] = "Sem.Color.OnSurface",
        ["toast-bg"] = "Sem.Color.Scrim",
        ["toast-text"] = "Sem.Color.OnSurface",
    };

    public static Color CurrentSeed { get; private set; } = Color.Parse("#FF0000");

    public static void Initialize(Color? seed = null)
    {
        if (Application.Current is null)
        {
            return;
        }

        if (seed.HasValue)
        {
            CurrentSeed = seed.Value;
        }

        ResourceBridge.EnsureAttached(Application.Current);
        ApplyGlobalTheme(CurrentSeed, Application.Current.ActualThemeVariant);
    }

    public static void ApplyGlobalTheme(Color seed, ThemeVariant variant)
    {
        if (Application.Current is null)
        {
            return;
        }

        CurrentSeed = seed;

        ThemeScheme scheme = ThemeGenerator.Generate(seed, variant);
        ResourceBridge.ApplySemanticBrushes(scheme.SemanticColors);

        foreach (KeyValuePair<string, string> alias in LegacyBrushAliases)
        {
            if (scheme.SemanticColors.TryGetValue(alias.Value, out Color color))
            {
                ResourceBridge.TryMutateExistingBrush(Application.Current, alias.Key, color);
            }
        }

        MessageBus.Current.SendMessage(new ThemeChangedEvent());
    }


    public static void OnThemeVariantChanged()
    {
        if (Application.Current is null)
        {
            return;
        }

        ApplyGlobalTheme(CurrentSeed, Application.Current.ActualThemeVariant);
    }
}