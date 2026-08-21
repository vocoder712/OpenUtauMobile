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
    }

    private static MainView CreateActivityMainView()
    {
        MainView mainView = new MainView
        {
            DataContext = new MainViewModel()
        };
        // Android 的活动视图工厂晚于应用初始化执行，此处按同一偏好重新应用最终主题。
        ThemeManagerV2.ApplyConfiguredTheme(ServiceHub.SystemAccentColorProvider, out _, out _);
        ActivityMainView = mainView;
        return mainView;
    }

    private static ThemeVariant ParseThemePreference(string? value) => value switch
    {
        "Light" => ThemeVariant.Light,
        "Dark" => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };
}
