using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace OpenUtauMobile.Themes.OpenUtauMobile.Runtime;

public static class ThemeResources
{
    public static bool IsDarkMode => Application.Current?.ActualThemeVariant == ThemeVariant.Dark;

    private static bool TryResolveResource(string key, ThemeVariant variant, out object? resource)
    {
        resource = null;

        if (Application.Current is null)
        {
            return false;
        }

        if (Application.Current.Resources.TryGetResource(key, variant, out resource))
        {
            return true;
        }

        if (Application.Current.Styles.TryGetResource(key, variant, out resource))
        {
            return true;
        }

        return false;
    }

    public static IBrush GetBrush(string key)
    {
        if (Application.Current is null)
        {
            throw new InvalidOperationException("Application.Current is null while resolving theme brush.");
        }

        if (TryResolveResource(key, Application.Current.ActualThemeVariant, out object? resource) &&
            resource is IBrush brush)
        {
            return brush;
        }

        throw new InvalidOperationException($"Theme brush resource not found: {key}");
    }

    public static Color GetColor(string key)
    {
        if (Application.Current is null)
        {
            throw new InvalidOperationException("Application.Current is null while resolving theme color.");
        }

        if (TryResolveResource(key, Application.Current.ActualThemeVariant, out object? resource))
        {
            if (resource is SolidColorBrush brush)
            {
                return brush.Color;
            }

            if (resource is Color color)
            {
                return color;
            }
        }

        throw new InvalidOperationException($"Theme color resource not found: {key}");
    }

    public static IPen GetPen(string key, double thickness = 1.0)
    {
        if (Application.Current is null)
        {
            throw new InvalidOperationException("Application.Current is null while resolving theme pen.");
        }

        if (TryResolveResource(key, Application.Current.ActualThemeVariant, out object? resource))
        {
            if (resource is IPen pen)
            {
                return pen;
            }

            if (resource is IBrush brush)
            {
                return new Pen(brush, thickness);
            }
        }

        throw new InvalidOperationException($"Theme pen resource not found: {key}");
    }

    public static string GetString(string key)
    {
        if (Application.Current is null)
        {
            throw new InvalidOperationException("Application.Current is null while resolving theme string.");
        }

        if (TryResolveResource(key, ThemeVariant.Default, out object? resource) && resource is string str)
        {
            return str;
        }

        throw new InvalidOperationException($"Theme string resource not found: {key}");
    }
}