using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using OpenUtau.Core.Util;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services;
using OpenUtauMobile.Themes.OpenUtauMobile.Runtime;
using OpenUtauMobile.ViewModels;
using OpenUtauMobile.Views;

namespace OpenUtauMobile;

public partial class App : Application
{
    internal static MainView? ActivityMainView { get; private set; }

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
        ThemeManagerV2.ApplyConfiguredTheme(ServiceHub.SystemAccentColorProvider, out _, out _);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel()
            };
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime activityPlatform)
        {
            activityPlatform.MainViewFactory = CreateActivityMainView;
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

        IPlatformSettings? platformSettings = PlatformSettings;
        if (platformSettings is not null)
        {
            platformSettings.ColorValuesChanged += (_, _) => RefreshSystemThemeColor();
        }
    }

    private static MainView CreateActivityMainView()
    {
        MainView mainView = new MainView
        {
            DataContext = new MainViewModel()
        };
        ActivityMainView = mainView;
        return mainView;
    }

    internal static void RefreshSystemThemeColor()
    {
        if (Preferences.Default.ThemeColorMode != (int)ThemeColorMode.FollowSystem)
        {
            return;
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RefreshSystemThemeColor);
            return;
        }

        ThemeManagerV2.ApplyConfiguredTheme(ServiceHub.SystemAccentColorProvider, out _, out _);
    }

    private static ThemeVariant ParseThemePreference(string? value) => value switch
    {
        "Light" => ThemeVariant.Light,
        "Dark" => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };
}
