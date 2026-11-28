using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Layout;
using IconPacks.Avalonia.PhosphorIcons;
using Avalonia.Markup.Xaml.Styling;
using System;
using System.Collections.Generic;
using OpenUtauMobile.DesktopUI.Services;
using OpenUtauMobile.Controls;
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
            [typeof(UtauSingerSetupViewModel)] = Route<UtauSingerSetupViewModel>("SingerSetup.Title", DesktopWindowProfile.SingerSetup,
                (setup, _, _) => Utility("SingerSetup.Title", new DesktopSingerSetupPage(setup))),
            [typeof(DiffSingerSetupViewModel)] = Route<DiffSingerSetupViewModel>("SingerSetup.Title", DesktopWindowProfile.SingerSetup,
                (setup, _, _) => Utility("SingerSetup.Title", new DesktopSingerSetupPage(setup))),
            [typeof(EnunuSingerSetupViewModel)] = Route<EnunuSingerSetupViewModel>("SingerSetup.Title", DesktopWindowProfile.SingerSetup,
                (setup, _, _) => Utility("SingerSetup.Title", new DesktopSingerSetupPage(setup))),
            [typeof(NeutrinoSingerSetupViewModel)] = Route<NeutrinoSingerSetupViewModel>("SingerSetup.Title", DesktopWindowProfile.SingerSetup,
                (setup, _, _) => Utility("SingerSetup.Title", new DesktopSingerSetupPage(setup))),
            [typeof(SingleFileSingerSetupViewModel)] = Route<SingleFileSingerSetupViewModel>("SingerSetup.Title", DesktopWindowProfile.SingerSetup,
                (setup, _, _) => Utility("SingerSetup.Title", new DesktopSingerSetupPage(setup))),
            [typeof(PluginSingerSetupViewModel)] = Route<PluginSingerSetupViewModel>("SingerSetup.Title", DesktopWindowProfile.SingerSetup,
                (setup, _, _) => Utility("SingerSetup.Title", new DesktopSingerSetupPage(setup))),
            [typeof(SingerDetailViewModel)] = Route<SingerDetailViewModel>("SingerDetail.Title", DesktopWindowProfile.Standard,
                (singer, _, _) => Utility("SingerDetail.Title", new DesktopSingerDetailPage(singer))),
            [typeof(OptionsViewModel)] = Route<OptionsViewModel>("Options.Title", DesktopWindowProfile.Standard,
                (options, _, _) => Utility("Options.Title", new DesktopOptionsPage(options))),
            [typeof(PluginManagerViewModel)] = Route<PluginManagerViewModel>("PluginManager.Title", DesktopWindowProfile.Standard,
                (plugins, _, _) => HostedPage("PluginManager.Title", new PluginManagerView { DataContext = plugins }, plugins,
                    DesktopUi.Command("Common.Refresh", nameof(PluginManagerViewModel.RefreshCommand)),
                    PrimaryCommand("PluginManager.Install", nameof(PluginManagerViewModel.InstallCommand)))),
            [typeof(PluginDetailsViewModel)] = Route<PluginDetailsViewModel>("PluginDetails.Title", DesktopWindowProfile.Standard,
                (plugin, _, _) => HostedPage("PluginDetails.Title", new PluginDetailsView { DataContext = plugin }, plugin,
                    DesktopUi.Command("Common.Refresh", nameof(PluginDetailsViewModel.RefreshCommand)))),
            [typeof(PluginWebSettingsViewModel)] = Route<PluginWebSettingsViewModel>("PluginWebSettings.Title", DesktopWindowProfile.Standard,
                (settings, _, _) => HostedPage("PluginWebSettings.Title", new PluginWebSettingsView { DataContext = settings }, settings,
                    DesktopUi.Command("PluginWebSettings.OpenBrowser", nameof(PluginWebSettingsViewModel.OpenBrowserCommand)))),
            [typeof(ExpressionGraphLibraryViewModel)] =Route<ExpressionGraphLibraryViewModel>("ExpressionGraph.LibraryTitle", DesktopWindowProfile.Standard,
                (graphs, _, _) => ExpressionGraphLibrary(graphs)),
            [typeof(ExpressionGraphEditorViewModel)] = Route<ExpressionGraphEditorViewModel>("GraphEdit.Title", DesktopWindowProfile.Standard,
                (graph, _, _) => ExpressionGraphEditor(graph)),
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

        /// <summary>在桌面工具窗口中复用表情图库；页面菜单与悬浮新建菜单改为窗口工具栏按钮。</summary>
        private static Control ExpressionGraphLibrary(ExpressionGraphLibraryViewModel graphs)
        {
            ExpressionGraphLibraryView view = new() { DataContext = graphs };
            view.SetDesktopChrome();
            Button create = PrimaryButton("ExpressionGraph.Create");
            create.Click += (_, _) =>
            {
                ContextMenu menu = new();
                foreach (FabMenuAction action in graphs.CreationActions)
                    menu.Items.Add(new MenuItem
                    {
                        Header = action.Label,
                        Icon = new PackIconPhosphorIcons { Kind = action.Icon, Width = 16, Height = 16 },
                        Command = graphs.CreateCommand,
                        CommandParameter = action.Id,
                    });
                menu.Open(create);
            };
            Button import = DesktopUi.Command("ExpressionGraph.Import", nameof(ExpressionGraphLibraryViewModel.ImportCommand));
            foreach (Button button in new[] { create, import })
                button.Bind(InputElement.IsEnabledProperty, new MultiBinding
                {
                    Converter = BoolConverters.And,
                    Bindings = { new Binding(nameof(ExpressionGraphLibraryViewModel.IsCurrentProject)), new Binding(nameof(ExpressionGraphLibraryViewModel.IsBusy)) { Converter = BoolConverters.Not } },
                });
            return HostedPage("ExpressionGraph.LibraryTitle", view, graphs,
                IconCommand(PackIconPhosphorIconsKind.ArrowCounterClockwise, "ExpressionGraph.UndoProject", nameof(ExpressionGraphLibraryViewModel.UndoCommand)),
                IconCommand(PackIconPhosphorIconsKind.ArrowClockwise, "ExpressionGraph.RedoProject", nameof(ExpressionGraphLibraryViewModel.RedoCommand)),
                import, create);
        }

        /// <summary>在桌面工具窗口中复用图编辑器及其画布、参数和撤销逻辑；顶栏和画布工具条按桌面紧凑样式显示。</summary>
        private static Control ExpressionGraphEditor(ExpressionGraphEditorViewModel graph)
        {
            ExpressionGraphEditorView view = new() { DataContext = graph };
            view.SetDesktopChrome();
            view.Classes.Add("DesktopGraphEditor");
            return Utility("GraphEdit.Title", view, new Thickness(0));
        }

        /// <summary>图标按钮：与编辑器传输栏相同的 28px 紧凑样式。</summary>
        private static Button IconCommand(PackIconPhosphorIconsKind icon, string tipKey, string path)
        {
            Button button = new() { Content = new PackIconPhosphorIcons { Kind = icon } };
            button.Classes.AddRange(["DesktopAction", "DesktopTransportControl", "DesktopTransportIcon"]);
            button.Bind(Button.CommandProperty, new Binding(path));
            ToolTip.SetTip(button, DesktopUi.Label(tipKey));
            return button;
        }

        /// <summary>
        /// 在桌面工具窗口中复用插件页面：隐藏移动端顶栏和悬浮按钮（标题由窗口提供，返回由 Esc/关闭窗口完成），
        /// 其操作改为与歌手管理一致的右对齐桌面按钮。
        /// </summary>
        private static Control HostedPage(string titleKey, Control view, object model, params Button[] actions)
        {
            view.Classes.Add("DesktopHostedPage");
            StackPanel toolbar = new() { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(20, 16, 20, 0) };
            foreach (Button action in actions)
            {
                action.DataContext = model;
                toolbar.Children.Add(action);
            }
            Grid page = new() { RowDefinitions = new RowDefinitions("Auto,*") };
            page.Children.Add(toolbar);
            Grid.SetRow(view, 1);
            page.Children.Add(view);
            return Utility(titleKey, page, new Thickness(0));
        }

        /// <summary>主要操作按钮：带加号图标的强调样式，与歌手管理的安装按钮一致。</summary>
        private static Button PrimaryCommand(string key, string path)
        {
            Button button = PrimaryButton(key);
            button.Bind(Button.CommandProperty, new Binding(path));
            return button;
        }

        /// <summary>不绑定命令的主要操作按钮，用于需要弹出菜单的入口。</summary>
        private static Button PrimaryButton(string key)
        {
            Button button = new();
            button.Classes.Add("DesktopAction");
            StackPanel label = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
            label.Children.Add(new PackIconPhosphorIcons { Kind = PackIconPhosphorIconsKind.Plus, Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center });
            label.Children.Add(DesktopUi.Label(key));
            button.Content = label;
            button.Classes.Add("tonal");
            return button;
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
