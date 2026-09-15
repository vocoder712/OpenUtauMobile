using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace OpenUtauMobile.Themes.OpenUtauMobile.Runtime;

/// <summary>为独立主题、设计器和主题变体作用域提供完整默认调色资源。</summary>
public sealed class SemanticThemeResources : ResourceDictionary
{
    public SemanticThemeResources()
    {
        foreach (ThemeVariant variant in new[] { ThemeVariant.Default, ThemeVariant.Light, ThemeVariant.Dark })
        {
            ResourceDictionary palette = new();
            ThemeScheme scheme = ThemeGenerator.Generate(ThemeManagerV2.CurrentSeed, variant);
            foreach (KeyValuePair<string, Color> pair in scheme.SemanticColors)
            {
                palette[pair.Key] = new SolidColorBrush(pair.Value);
                palette[pair.Key.Replace("Sem.Color.", "Sem.Value.")] = pair.Value;
            }

            // 少见控件保留上游强调色键，但颜色始终来自同一语义调色板。
            foreach (string suffix in new[] { "", "Dark1", "Dark2", "Dark3", "Light1", "Light2", "Light3" })
            {
                palette["SystemAccentColor" + suffix] = scheme.SemanticColors["Sem.Color.Primary"];
            }

            ThemeDictionaries[variant] = palette;
        }
    }
}
