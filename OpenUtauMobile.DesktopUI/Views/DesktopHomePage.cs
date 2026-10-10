using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using IconPacks.Avalonia.PhosphorIcons;
using OpenUtau.Core;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services.Dialogs;
using OpenUtauMobile.Services.Tracks;
using OpenUtauMobile.Storage;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.DesktopUI.Views
{
    internal sealed class DesktopHomePage : UserControl
    {
        private bool _opening;

        public DesktopHomePage(MainViewModel main, HomeViewModel home)
        {
            DataContext = home;
            DesktopDensity.Apply(this);
            Classes.Add("DesktopHome");
            Grid page = new() { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 24, MaxWidth = 1360 };
            Grid heading = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 20 };
            TextBlock brand = DesktopUi.Label("Home.Brand.Name");
            brand.FontSize = 22;
            brand.FontWeight = FontWeight.SemiBold;
            heading.Children.Add(brand);
            StackPanel actions = new() { Orientation = Orientation.Horizontal, Spacing = 12 };
            Button create = HomeAction("Home.Action.New", PackIconPhosphorIconsKind.Plus, async () =>
            {
                string path = Path.Combine(PathManager.Inst.TemplatesPath, "default.ustx");
                await main.OpenDesktopProjectAsync(File.Exists(path) ? new(path, ProjectOpenKind.Template) : new());
            });
            create.Classes.Add("filled");
            actions.Children.Add(create);
            Button open = HomeAction("Home.Action.Open", PackIconPhosphorIconsKind.FolderOpen, async () =>
            {
                string path = await FilePicker.PickSingleFileAsync(L.S("Home.Action.Open"), TrackImportService.FilePatterns);
                if (!string.IsNullOrEmpty(path)) await main.OpenDesktopProjectAsync(new(path,
                    Path.GetExtension(path).Equals(".ustx", StringComparison.OrdinalIgnoreCase) ? ProjectOpenKind.Normal : ProjectOpenKind.ExternalCopy));
            });
            open.Classes.Add("tonal");
            actions.Children.Add(open);
            Grid.SetColumn(actions, 1);
            heading.Children.Add(actions);
            StackPanel header = new() { Spacing = 20, Children = { heading } };
            page.Children.Add(header);

            StackPanel recoveryContent = new() { Spacing = 12 };
            recoveryContent.Children.Add(DesktopUi.Label("Home.Recovery.Message"));
            StackPanel recoveryActions = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
            recoveryActions.Children.Add(DesktopUi.Action("Home.Recovery.OpenAction", async () =>
            {
                if (!string.IsNullOrEmpty(home.RecoveryPath)) await main.OpenDesktopProjectAsync(new(home.RecoveryPath, ProjectOpenKind.ExternalCopy));
            }));
            recoveryActions.Children.Add(DesktopUi.Command("Home.Recovery.Dismiss", nameof(home.DismissRecoveryCommand)));
            recoveryContent.Children.Add(recoveryActions);
            Border recovery = DesktopUi.Panel(recoveryContent, true);
            recovery.Bind(IsVisibleProperty, new MultiBinding
            {
                Bindings = { new Binding(nameof(home.HasRecovery)) { Source = home }, new Binding(nameof(home.RecoveryBannerDismissed)) { Source = home, Converter = BoolConverters.Not } },
                Converter = BoolConverters.And
            });
            header.Children.Add(recovery);

            Grid columns = new() { ColumnDefinitions = new ColumnDefinitions("2*,*"), ColumnSpacing = 20 };
            Grid recent = new() { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 12 };
            TextBlock recentTitle = DesktopUi.Label("Home.Recent.Title");
            recentTitle.Classes.Add("DesktopContentTitle");
            recent.Children.Add(recentTitle);
            ListBox list = new() { Name = "DesktopRecentProjects" };
            list.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(home.RecentProjects)) { Source = home });
            list.ItemTemplate = new FuncDataTemplate<string>((path, _) => RecentItem(path, home));
            async Task OpenRecentAsync()
            {
                if (_opening || list.SelectedItem is not string path) return;
                _opening = true;
                try { await main.OpenDesktopProjectAsync(new(path)); }
                catch (Exception e) { ErrorDialogService.Show(new ErrorDialogViewModel(new ErrorMessageNotification(e))); }
                finally { _opening = false; }
            }
            ActivateList(list, OpenRecentAsync);
            Border recentPanel = DesktopUi.Panel(list);
            Grid.SetRow(recentPanel, 1);
            recent.Children.Add(recentPanel);
            TextBlock empty = DesktopUi.Label("Home.Recent.Empty");
            empty.Classes.Add("DesktopSecondary");
            empty.Margin = new Thickness(24);
            empty.VerticalAlignment = VerticalAlignment.Top;
            empty.Bind(IsVisibleProperty, new Binding("RecentProjects.Count") { Source = home, Converter = new FuncValueConverter<int, bool>(count => count == 0) });
            Grid.SetRow(empty, 1);
            recent.Children.Add(empty);
            columns.Children.Add(recent);

            Grid templates = new() { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 12 };
            TextBlock templateTitle = DesktopUi.Label("Desktop.Templates");
            templateTitle.Classes.Add("DesktopContentTitle");
            templates.Children.Add(templateTitle);
            ListBox templateList = new() { Name = "DesktopTemplates" };
            templateList.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(home.Templates)) { Source = home });
            templateList.ItemTemplate = new FuncDataTemplate<string>((path, _) => new TextBlock
            {
                Text = Path.GetFileNameWithoutExtension(path), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center
            });
            async Task OpenTemplateAsync()
            {
                if (_opening || templateList.SelectedItem is not string selected) return;
                _opening = true;
                try { await main.OpenDesktopProjectAsync(new(selected, ProjectOpenKind.Template)); }
                catch (Exception e) { ErrorDialogService.Show(new ErrorDialogViewModel(new ErrorMessageNotification(e))); }
                finally { _opening = false; }
            }
            ActivateList(templateList, OpenTemplateAsync);
            Border templatePanel = DesktopUi.Panel(templateList);
            Grid.SetRow(templatePanel, 1);
            templates.Children.Add(templatePanel);
            TextBlock noTemplates = DesktopUi.Label("Desktop.NoTemplates");
            noTemplates.Classes.Add("DesktopSecondary");
            noTemplates.Margin = new Thickness(24);
            noTemplates.VerticalAlignment = VerticalAlignment.Top;
            noTemplates.Bind(IsVisibleProperty, new Binding("Templates.Count") { Source = home, Converter = new FuncValueConverter<int, bool>(count => count == 0) });
            Grid.SetRow(noTemplates, 1);
            templates.Children.Add(noTemplates);
            Button openTemplate = DesktopUi.Action("Home.Action.OpenTemplate", OpenTemplateAsync);
            openTemplate.Bind(IsEnabledProperty, new Binding(nameof(templateList.SelectedItem)) { Source = templateList, Converter = ObjectConverters.IsNotNull });
            openTemplate.Bind(IsVisibleProperty, new Binding("Templates.Count") { Source = home, Converter = new FuncValueConverter<int, bool>(count => count > 0) });
            Grid.SetRow(openTemplate, 2);
            templates.Children.Add(openTemplate);
            Grid.SetColumn(templates, 1);
            columns.Children.Add(templates);
            Grid.SetRow(columns, 1);
            page.Children.Add(columns);
            Content = new Border { Padding = new Thickness(24), Child = page };
        }

        private static Button HomeAction(string key, PackIconPhosphorIconsKind icon, Func<Task> action)
        {
            Button button = DesktopUi.Action(key, action);
            button.Bind(AutomationProperties.NameProperty, button.GetResourceObservable(key));
            button.CornerRadius = new CornerRadius(999);
            button.Padding = new Thickness(20, 10);
            button.Content = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 10,
                Children = { new PackIconPhosphorIcons { Kind = icon, Width = 20, Height = 20 }, DesktopUi.Label(key) }
            };
            return button;
        }

        private static Control RecentItem(string? path, HomeViewModel home)
        {
            Grid row = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12 };
            row.Children.Add(new PackIconPhosphorIcons { Kind = PackIconPhosphorIconsKind.FileText, Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center });
            TextBlock location = new() { Text = path, TextTrimming = TextTrimming.CharacterEllipsis };
            location.Classes.Add("DesktopSecondary");
            StackPanel description = new()
            {
                Spacing = 4,
                Children = { new TextBlock { Text = Path.GetFileName(path), FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis }, location }
            };
            Grid.SetColumn(description, 1);
            row.Children.Add(description);
            Button remove = new()
            {
                Classes = { "DesktopToolbarButton" }, Background = Brushes.Transparent,
                Content = new PackIconPhosphorIcons { Kind = PackIconPhosphorIconsKind.Trash, Width = 16, Height = 16 },
                Command = home.RemoveRecentCommand, CommandParameter = path, VerticalAlignment = VerticalAlignment.Center
            };
            ToolTip.SetTip(remove, DesktopUi.Label("Home.Recent.Remove"));
            remove.Bind(AutomationProperties.NameProperty, remove.GetResourceObservable("Home.Recent.Remove"));
            Grid.SetColumn(remove, 2);
            row.Children.Add(remove);
            ToolTip.SetTip(row, path);
            return row;
        }

        private static void ActivateList(ListBox list, Func<Task> activate)
        {
            list.DoubleTapped += async (_, e) =>
            {
                if (e.Source is Visual source && source.GetSelfAndVisualAncestors().OfType<ListBoxItem>().Any()
                    && !source.GetSelfAndVisualAncestors().OfType<Button>().Any()) await activate();
            };
            list.AddHandler(InputElement.KeyDownEvent, async (_, e) =>
            {
                if (e.Key == Key.Enter) { e.Handled = true; await activate(); }
            }, RoutingStrategies.Tunnel, handledEventsToo: true);
        }
    }
}
