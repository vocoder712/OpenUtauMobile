using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using System;
using System.Collections.Generic;
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
        private sealed record PageRoute(
            string TitleKey,
            DesktopWindowProfile Window,
            Func<NavigateViewModelBase, MainViewModel, DesktopLayoutStore, Control?> Build);

        private static PageRoute Route<T>(string titleKey, DesktopWindowProfile window,
            Func<T, MainViewModel, DesktopLayoutStore, Control?> build) where T : NavigateViewModelBase =>
            new(titleKey, window, (viewModel, main, layout) => build((T)viewModel, main, layout));

        private static readonly IReadOnlyDictionary<Type, PageRoute> Routes = new Dictionary<Type, PageRoute>
        {
            [typeof(HomeViewModel)] = Route<HomeViewModel>("Home.Title", DesktopWindowProfile.Standard,
                (home, main, _) => new Border { Child = new DesktopHomePage(main, home) }),
            [typeof(SingerManagementViewModel)] = Route<SingerManagementViewModel>("SingerManagement.Title", DesktopWindowProfile.Standard,
                (singers, main, _) => Utility("SingerManagement.Title", new DesktopSingerPage(main, singers))),
            [typeof(DependencyManagerViewModel)] = Route<DependencyManagerViewModel>("DependencyManager.Title", DesktopWindowProfile.Standard,
                (dependencies, _, _) => Utility("DependencyManager.Title", new DesktopDependencyPage(dependencies))),
            [typeof(DesktopToolsViewModel)] = Route<DesktopToolsViewModel>("Desktop.Tools", DesktopWindowProfile.Standard,
                (tools, _, _) => Utility("Desktop.Tools", new DesktopToolsPage(tools), new Thickness(20, 20, 20, 16))),
            [typeof(SettingsViewModel)] = Route<SettingsViewModel>("Settings.Title", DesktopWindowProfile.Standard,
                (settings, _, _) => Settings(settings)),
            [typeof(AboutViewModel)] = Route<AboutViewModel>("About.Title", DesktopWindowProfile.About,
                (about, _, _) => Utility("About.Title", new DesktopAboutPage(about))),
            [typeof(ExportLogsViewModel)] = Route<ExportLogsViewModel>("ExportLogs.Title", DesktopWindowProfile.Logs,
                (logs, _, _) => Utility("ExportLogs.Title", new DesktopExportLogsPage(logs))),
            [typeof(ClassicSingerSetupViewModel)] = Route<ClassicSingerSetupViewModel>("SingerSetup.Title", DesktopWindowProfile.SingerSetup,
                (setup, _, _) => Utility("SingerSetup.Title", new DesktopSingerSetupPage(setup))),
            [typeof(SingerDetailViewModel)] = Route<SingerDetailViewModel>("SingerDetail.Title", DesktopWindowProfile.Standard,
                (singer, _, _) => Utility("SingerDetail.Title", new DesktopSingerDetailPage(singer))),
            [typeof(OptionsViewModel)] = Route<OptionsViewModel>("Options.Title", DesktopWindowProfile.Standard,
                (options, _, _) => Utility("Options.Title", new DesktopOptionsPage(options))),
            [typeof(SplashScreenViewModel)] = Route<SplashScreenViewModel>("Desktop.Window", DesktopWindowProfile.Standard,
                (splash, _, _) => new SplashScreenView { DataContext = splash }),
        };

        public static DesktopWindowProfile WindowFor(NavigateViewModelBase viewModel) =>
            Routes.TryGetValue(viewModel.GetType(), out PageRoute? route) ? route.Window : DesktopWindowProfile.Standard;

        public static string TitleKeyFor(NavigateViewModelBase viewModel) =>
            Routes.TryGetValue(viewModel.GetType(), out PageRoute? route) ? route.TitleKey : "Desktop.Window";

        public static Control? BuildPage(object? data, MainViewModel main, DesktopLayoutStore layout)
        {
            if (data is NavigateViewModelBase viewModel && Routes.TryGetValue(viewModel.GetType(), out PageRoute? route))
                return route.Build(viewModel, main, layout);
            if (data is ViewModelBase model) return Unavailable(model);
            return null;
        }

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

        private static Control Utility(string titleKey, Control page, Thickness? inset = null) =>
            new DesktopUtilityShell(titleKey, page, contentInset: inset);
    }
}
