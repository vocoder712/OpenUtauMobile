using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Avalonia;
using Avalonia.Media;
using OpenUtau.Core.Util;
using OpenUtauMobile.Services;

namespace OpenUtauMobile.Themes.OpenUtauMobile.Runtime;

public interface ISystemAccentColorProvider
{
    bool TryGetSystemSeed(out Color color, out string source);
}

public sealed class DefaultSystemAccentColorProvider : ISystemAccentColorProvider
{
    public bool TryGetSystemSeed(out Color color, out string source)
    {
        if (TryGetFromAvalonia(out color, out source))
        {
            return true;
        }

        Func<(bool success, Color color, string source)>? fallback = ServiceHub.TryGetPlatformAccentFallback;
        if (fallback != null)
        {
            (bool success, Color delegatedColor, string delegatedSource) = fallback.Invoke();
            if (success)
            {
                color = delegatedColor;
                source = delegatedSource;
                return true;
            }
        }

        color = default;
        source = string.Empty;
        return false;
    }

    private static bool TryGetFromAvalonia(out Color color, out string source)
    {
        object? platformSettings = Application.Current?.PlatformSettings;
        if (platformSettings is null)
        {
            color = default;
            source = string.Empty;
            return false;
        }

        MethodInfo? getColorValues = platformSettings.GetType().GetMethod("GetColorValues", Type.EmptyTypes);
        if (getColorValues is null)
        {
            color = default;
            source = string.Empty;
            return false;
        }

        object? colorValues = getColorValues.Invoke(platformSettings, null);
        if (colorValues is null)
        {
            color = default;
            source = string.Empty;
            return false;
        }

        if (!TryReadAccentColor(colorValues, out Color parsed))
        {
            color = default;
            source = string.Empty;
            return false;
        }

        color = Color.FromRgb(parsed.R, parsed.G, parsed.B);
        source = "Avalonia.PlatformSettings";
        return true;
    }

    private static bool TryReadAccentColor(object colorValues, out Color color)
    {
        foreach (string propertyName in AccentPropertyCandidates)
        {
            PropertyInfo? property = colorValues.GetType()
                .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            if (property is null)
            {
                continue;
            }

            object? value = property.GetValue(colorValues);
            if (value is null)
            {
                continue;
            }

            if (value is Color direct)
            {
                color = direct;
                return true;
            }

            Type valueType = value.GetType();
            if (Nullable.GetUnderlyingType(property.PropertyType) == typeof(Color) || valueType == typeof(Color))
            {
                color = (Color)value;
                return true;
            }
        }

        color = default;
        return false;
    }

    private static readonly IReadOnlyList<string> AccentPropertyCandidates =
    [
        "Accent",
        "AccentColor",
        "SystemAccentColor",
        "AccentColor1",
        "AccentPrimary"
    ];
}

public enum ThemeColorMode
{
    FollowSystem = 0,
    Custom = 1
}

public static class ThemeSeedResolver
{
    public static Color ResolveSeed(ISystemAccentColorProvider? provider, out string source, out string fallbackReason)
    {
        ThemeColorMode mode = Preferences.Default.ThemeColorMode == (int)ThemeColorMode.Custom
            ? ThemeColorMode.Custom
            : ThemeColorMode.FollowSystem;

        string seedHex = Preferences.Default.ThemeColorSeedHex;

        if (mode == ThemeColorMode.FollowSystem)
        {
            if (TryGetProviderColor(provider, out Color systemSeed, out source))
            {
                fallbackReason = string.Empty;
                return systemSeed;
            }

            if (TryParseHexSeed(seedHex, out Color customSeed))
            {
                source = "CustomSeed";
                fallbackReason = "SystemUnavailableUseCustom";
                return customSeed;
            }

            source = "Default";
            fallbackReason = "SystemUnavailableAndCustomInvalid";
            return Color.Parse("#FF0000");
        }

        if (TryParseHexSeed(seedHex, out Color customModeSeed))
        {
            source = "CustomSeed";
            fallbackReason = string.Empty;
            return customModeSeed;
        }

        if (TryGetProviderColor(provider, out Color fallbackSystemSeed, out source))
        {
            fallbackReason = "CustomInvalidUseSystem";
            return fallbackSystemSeed;
        }

        source = "Default";
        fallbackReason = "CustomInvalidAndSystemUnavailable";
        return Color.Parse("#FF0000");
    }

    public static bool TryParseHexSeed(string? input, out Color color)
    {
        color = default;
        if (!TryNormalizeHex(input, out string normalized))
        {
            return false;
        }

        color = Color.Parse(normalized);
        return true;
    }

    public static bool TryNormalizeHex(string? input, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        string trimmed = input.Trim();
        if (trimmed.StartsWith("#", StringComparison.Ordinal))
        {
            trimmed = trimmed[1..];
        }

        if (trimmed.Length != 6)
        {
            return false;
        }

        if (!int.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
        {
            return false;
        }

        normalized = "#" + trimmed.ToUpperInvariant();
        return true;
    }

    public static string ToHex(Color color)
    {
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private static bool TryGetProviderColor(ISystemAccentColorProvider? provider, out Color color, out string source)
    {
        if (provider != null && provider.TryGetSystemSeed(out Color fromProvider, out string providerSource))
        {
            color = Color.FromRgb(fromProvider.R, fromProvider.G, fromProvider.B);
            source = providerSource;
            return true;
        }

        color = default;
        source = string.Empty;
        return false;
    }
}

public sealed class ThemeSeedPreset
{
    public ThemeSeedPreset(string id, string displayName, string hex)
    {
        Id = id;
        DisplayName = displayName;
        Hex = hex;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string Hex { get; }
}

public static class ThemeSeedPresets
{
    public static IReadOnlyList<ThemeSeedPreset> All { get; } =
    [
        new("tianyi", "Tianyi", "#66CCFF"),
        new("rose-red", "Rose Red", "#D32F2F"),
        new("coral", "Coral", "#FF6F61"),
        new("amber", "Amber", "#FFB300"),
        new("lime", "Lime", "#8BC34A"),
        new("forest", "Forest", "#2E7D32"),
        new("jade", "Jade", "#009688"),
        new("sky", "Sky", "#03A9F4"),
        new("cobalt", "Cobalt", "#3F51B5"),
        new("violet", "Violet", "#7E57C2"),
        new("magenta", "Magenta", "#D81B60"),
        new("slate", "Slate", "#546E7A"),
        new("graphite", "Graphite", "#455A64")
    ];
}