using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using System;
using OpenUtauMobile.DesktopUI.Services;
using OpenUtauMobile.ViewModels;
using OpenUtauMobile.Views;
using Serilog;

namespace OpenUtauMobile.DesktopUI.Views
{
    internal readonly record struct DesktopWindowProfile(double Width, double Height, double MinWidth, double MinHeight)
    {
        public static DesktopWindowProfile Standard => new(960, 720, 560, 400);
        public static DesktopWindowProfile About => new(560, 600, 400, 320);
        public static DesktopWindowProfile Logs => new(800, 600, 560, 400);
        public static DesktopWindowProfile SingerSetup => new(800, 640, 560, 400);
        public static DesktopWindowProfile Mixer => new(900, 560, 600, 420);
    }

    internal static class DesktopPresentationCatalog
    {
        public static DesktopWindowProfile WindowFor(NavigateViewModelBase viewModel) => viewModel switch
        {
            AboutViewModel => DesktopWindowProfile.About,
            ExportLogsViewModel => DesktopWindowProfile.Logs,
            ClassicSingerSetupViewModel => DesktopWindowProfile.SingerSetup,
            _ => DesktopWindowProfile.Standard
        };

        public static string TitleKeyFor(NavigateViewModelBase viewModel) => viewModel switch
        {
            SettingsViewModel => "Settings.Title",
            DesktopToolsViewModel => "Desktop.Tools",
            SingerManagementViewModel => "SingerManagement.Title",
            DependencyManagerViewModel => "DependencyManager.Title",
            AboutViewModel => "About.Title",
            ExportLogsViewModel => "ExportLogs.Title",
            HomeViewModel => "Home.Title",
            ClassicSingerSetupViewModel => "SingerSetup.Title",
            SingerDetailViewModel => "SingerDetail.Title",
            OptionsViewModel => "Options.Title",
            _ => "Desktop.Window"
        };

        public static Control? BuildPage(object? data, MainViewModel main, DesktopLayoutStore layout) => data switch
        {
            HomeViewModel home => new Border { Child = new DesktopHomePage(main, home) },
            SingerManagementViewModel singers => Utility("SingerManagement.Title", new DesktopSingerPage(main, singers)),
            DependencyManagerViewModel dependencies => Utility("DependencyManager.Title", new DesktopDependencyPage(dependencies)),
            DesktopToolsViewModel tools => Utility("Desktop.Tools", new DesktopToolsPage(tools), new Thickness(20, 20, 20, 16)),
            SettingsViewModel settings => Settings(settings),
            AboutViewModel about => Utility("About.Title", new DesktopAboutPage(about)),
            ExportLogsViewModel logs => Utility("ExportLogs.Title", new DesktopExportLogsPage(logs)),
            ClassicSingerSetupViewModel setup => Utility("SingerSetup.Title", new DesktopSingerSetupPage(setup)),
            SingerDetailViewModel singer => Utility("SingerDetail.Title", new DesktopSingerDetailPage(singer)),
            OptionsViewModel options => Utility("Options.Title", new DesktopOptionsPage(options)),
            SplashScreenViewModel splash => new OpenUtauMobile.Views.SplashScreenView { DataContext = splash },
            ViewModelBase model => Unavailable(model),
            _ => null
        };

        private static Control Settings(SettingsViewModel settings)
        {
            settings.IsNavExpanded = true;
            SettingsView view = new() { DataContext = settings, PersistentNavigation = true };
            DesktopDensity.Apply(view);
            view.Classes.Add("DesktopSettings");
            view.Styles.Add(new StyleInclude(new Uri("avares://OpenUtauMobile.DesktopUI/"))
            {
                Source = new Uri("avares://OpenUtauMobile.DesktopUI/Views/DesktopStyles.axaml")
            });
            return Utility("Settings.Title", view);
        }

        private static Control Unavailable(ViewModelBase model)
        {
            Log.Warning("No desktop presentation registered for {Route}", model.GetType().FullName);
            return Utility("Desktop.Window", new DesktopUnavailablePage(model.GetType().Name));
        }

        private static Control Utility(string titleKey, Control page, Thickness? inset = null) => new DesktopUtilityShell(titleKey, page, contentInset: inset);
    }
}
