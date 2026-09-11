using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace OpenUtauMobile.Themes.OpenUtauMobile.Runtime;

public sealed class ThemeResourceBridge
{
    private readonly ResourceDictionary _runtimeDictionary = new();
    private readonly Dictionary<string, SolidColorBrush> _runtimeBrushes = new();
    private Application? _attachedApplication;
    private StyledElement? _attachedHost;

    public void EnsureAttached(Application app)
    {
        if (ReferenceEquals(_attachedApplication, app))
        {
            return;
        }

        _attachedApplication?.Resources.MergedDictionaries.Remove(_runtimeDictionary);

        if (_attachedHost is not null)
        {
            _attachedHost.Resources.MergedDictionaries.Remove(_runtimeDictionary);
            _attachedHost = null;
        }

        app.Resources.MergedDictionaries.Add(_runtimeDictionary);
        _attachedApplication = app;
    }

    public void EnsureAttached(StyledElement host)
    {
        if (ReferenceEquals(_attachedHost, host))
        {
            return;
        }

        if (_attachedApplication is not null)
        {
            _attachedApplication.Resources.MergedDictionaries.Remove(_runtimeDictionary);
            _attachedApplication = null;
        }

        _attachedHost?.Resources.MergedDictionaries.Remove(_runtimeDictionary);

        host.Resources.MergedDictionaries.Add(_runtimeDictionary);
        _attachedHost = host;
    }

    public void ApplySemanticBrushes(IReadOnlyDictionary<string, Color> semanticColors, ThemeVariant variant)
    {
        if (!_runtimeDictionary.ThemeDictionaries.ContainsKey(variant))
        {
            _runtimeDictionary.ThemeDictionaries[variant] = new ResourceDictionary();
        }
        ResourceDictionary palette = (ResourceDictionary)_runtimeDictionary.ThemeDictionaries[variant];
        foreach (KeyValuePair<string, Color> pair in semanticColors)
        {
            if (palette.TryGetValue(pair.Key, out object? value) && value is SolidColorBrush brush)
            {
                brush.Color = pair.Value;
            }
            else
            {
                palette[pair.Key] = new SolidColorBrush(pair.Value);
            }
            palette[pair.Key.Replace("Sem.Color.", "Sem.Value.")] = pair.Value;
        }

        if (semanticColors.TryGetValue("Sem.Color.Primary", out Color primary))
        {
            foreach (string suffix in new[] { "", "Dark1", "Dark2", "Dark3", "Light1", "Light2", "Light3" })
            {
                palette["SystemAccentColor" + suffix] = primary;
            }
        }
    }

    public void ClearSemanticBrushes(IReadOnlyList<string> semanticKeys)
    {
        foreach (string key in semanticKeys)
        {
            _runtimeDictionary.Remove(key);
            _runtimeDictionary.Remove(key.Replace("Sem.Color.", "Sem.Value."));
            foreach (ThemeVariant variant in new[] { ThemeVariant.Default, ThemeVariant.Light, ThemeVariant.Dark })
            {
                if (_runtimeDictionary.ThemeDictionaries.ContainsKey(variant))
                {
                    ResourceDictionary palette = (ResourceDictionary)_runtimeDictionary.ThemeDictionaries[variant];
                    palette.Remove(key);
                    palette.Remove(key.Replace("Sem.Color.", "Sem.Value."));
                }
            }
        }
    }

    public void TryMutateExistingBrush(Application app, string key, Color color)
    {
        if (app.TryGetResource(key, app.ActualThemeVariant, out object? existing) && existing is SolidColorBrush brush)
        {
            brush.Color = color;
            return;
        }

        UpsertRuntimeBrush(key, color);
    }

    private void UpsertRuntimeBrush(string key, Color color)
    {
        if (_runtimeBrushes.TryGetValue(key, out SolidColorBrush? brush))
        {
            brush.Color = color;
            return;
        }

        SolidColorBrush newBrush = new(color);
        _runtimeBrushes[key] = newBrush;
        _runtimeDictionary.Add(key, newBrush);
    }
}
