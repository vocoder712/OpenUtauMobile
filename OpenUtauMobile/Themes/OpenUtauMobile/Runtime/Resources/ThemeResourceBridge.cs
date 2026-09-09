using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

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

    public void ApplySemanticBrushes(IReadOnlyDictionary<string, Color> semanticColors)
    {
        foreach (KeyValuePair<string, Color> pair in semanticColors)
        {
            UpsertRuntimeBrush(pair.Key, pair.Value);
        }
    }

    public void ClearSemanticBrushes(IReadOnlyList<string> semanticKeys)
    {
        foreach (string key in semanticKeys)
        {
            _runtimeDictionary.Remove(key);
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