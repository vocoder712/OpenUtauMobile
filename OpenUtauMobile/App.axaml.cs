using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using OpenUtau.Core.Util;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services;
using OpenUtauMobile.Themes.OpenUtauMobile.Runtime;
using OpenUtauMobile.ViewModels;
using OpenUtauMobile.Views;

namespace OpenUtauMobile;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Load language preference before constructing any UI/view-models.
        LocalizationManager.LoadLanguage(Preferences.Default.Language);

        RequestedThemeVariant = ParseThemePreference(Preferences.Default.ThemeName);

        ServiceHub.SystemAccentColorProvider ??= new DefaultSystemAccentColorProvider();

        // Initialize runtime theme resources before creating UI.
        ThemeManagerV2.Initialize();
        var seed = ThemeSeedResolver.ResolveSeed(
            ServiceHub.SystemAccentColorProvider,
            out _,
            out _);
        ThemeManagerV2.ApplyGlobalTheme(seed, RequestedThemeVariant);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel()
            };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = new MainView
            {
                DataContext = new MainViewModel()
            };
        }

        base.OnFrameworkInitializationCompleted();

        // Follow system light/dark changes with runtime-generated semantic theme.
        ActualThemeVariantChanged += (_, _) => ThemeManagerV2.OnThemeVariantChanged();
    }

    private static ThemeVariant ParseThemePreference(string? value) => value switch
    {
        "Light" => ThemeVariant.Light,
        "Dark" => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };
}