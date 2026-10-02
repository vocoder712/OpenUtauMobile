using System;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.DesktopUI.Views
{
    internal sealed class DesktopDependencyPage : UserControl
    {
        public DesktopDependencyPage(DependencyManagerViewModel dependencies)
        {
            DataContext = dependencies;
            Grid root = new() { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 8 };
            WrapPanel header = new() { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 8 };
            TextBox search = new() { Name = "DesktopDependencySearch", Width = 260 };
            search.Bind(TextBox.PlaceholderTextProperty, search.GetResourceObservable("Desktop.Search"));
            search.Bind(TextBox.TextProperty, new Binding("SearchText") { Mode = BindingMode.TwoWay });
            header.Children.Add(search);
            ComboBox sort = new() { MinWidth = 160 };
            sort.Items.Add(new ComboBoxItem { Content = DesktopUi.Label("DependencyManager.SortByName") });
            sort.Items.Add(new ComboBoxItem { Content = DesktopUi.Label("DependencyManager.SortByDeveloper") });
            sort.Bind(SelectingItemsControl.SelectedIndexProperty, new Binding("SortMode") { Mode = BindingMode.TwoWay });
            ToolTip.SetTip(sort, DesktopUi.Label("DependencyManager.Sort")); header.Children.Add(sort);
            Button refreshAvailable = DesktopUi.Command("Desktop.Refresh", "RefreshRegistryCommand");
            Button refreshInstalled = DesktopUi.Command("Desktop.Refresh", "RefreshInstalledCommand");
            refreshInstalled.IsVisible = false;
            header.Children.Add(refreshAvailable);
            header.Children.Add(refreshInstalled);
            header.Children.Add(DesktopUi.Command("DependencyManager.InstallFromFile", "InstallFromFileCommand"));
            root.Children.Add(header);
            Grid columns = new() { ColumnDefinitions = new ColumnDefinitions("320,6,*") };
            TabControl tabs = new();
            tabs.Bind(SelectingItemsControl.SelectedIndexProperty, new Binding("SelectedTabIndex") { Mode = BindingMode.TwoWay });
            ListBox available = new() { Name = "DesktopAvailablePackages" };
            available.ItemTemplate = new FuncDataTemplate<DependencyItemViewModel>((item, _) =>
            {
                StackPanel row = new() { Spacing = 2, MinHeight = 48, VerticalAlignment = VerticalAlignment.Center };
                TextBlock name = new() { Text = item?.Name ?? string.Empty, FontWeight = Avalonia.Media.FontWeight.SemiBold, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
                TextBlock developers = new() { Text = item?.Developers ?? string.Empty, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
                developers.Classes.Add("DesktopSecondary");
                row.Children.Add(name);
                row.Children.Add(developers);
                return row;
            });
            ListBox installed = new() { Name = "DesktopInstalledPackages" };
            installed.ItemTemplate = new FuncDataTemplate<InstalledDependencyViewModel>((item, _) =>
            {
                StackPanel row = new() { Spacing = 2, MinHeight = 48, VerticalAlignment = VerticalAlignment.Center };
                TextBlock id = new() { Text = item?.Id ?? string.Empty, FontWeight = Avalonia.Media.FontWeight.SemiBold, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
                TextBlock version = new() { Text = item?.Version ?? string.Empty };
                version.Classes.Add("DesktopSecondary");
                row.Children.Add(id);
                row.Children.Add(version);
                return row;
            });
            TextBlock availableLoading = DesktopUi.Label("DependencyManager.LoadingAvailable");
            TextBlock availableEmpty = DesktopUi.Label("DependencyManager.EmptyAvailable");
            TextBlock availableNoResults = DesktopUi.Label("DependencyManager.NoResults");
            TextBlock availableError = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            Grid availablePanel = StatePanel(available, availableLoading, availableEmpty, availableNoResults, availableError);
            TextBlock installedLoading = DesktopUi.Label("DependencyManager.LoadingInstalled");
            TextBlock installedEmpty = DesktopUi.Label("DependencyManager.EmptyInstalled");
            TextBlock installedNoResults = DesktopUi.Label("DependencyManager.NoResults");
            TextBlock installedError = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            Grid installedPanel = StatePanel(installed, installedLoading, installedEmpty, installedNoResults, installedError);
            tabs.ItemsSource = new[] { new TabItem { Header = DesktopUi.Label("DependencyManager.Available"), Content = availablePanel }, new TabItem { Header = DesktopUi.Label("DependencyManager.InstalledTab"), Content = installedPanel } };
            columns.ColumnDefinitions[0].MinWidth = 180;
            columns.ColumnDefinitions[2].MinWidth = 200;
            ContentControl details = new() { ClipToBounds = true, Margin = new Thickness(16, 0, 0, 0) };
            bool refreshing = false;
            void Refresh()
            {
                string? availableId = (available.SelectedItem as DependencyItemViewModel)?.Id;
                string? installedId = (installed.SelectedItem as InstalledDependencyViewModel)?.Id;
                DependencyItemViewModel[] packages = dependencies.FilteredAvailablePackages.ToArray();
                InstalledDependencyViewModel[] installedPackages = dependencies.FilteredInstalledPackages.ToArray();
                refreshing = true;
                try
                {
                    available.ItemsSource = packages; available.SelectedItem = availableId == null ? null : packages.FirstOrDefault(p => p.Id == availableId);
                    installed.ItemsSource = installedPackages; installed.SelectedItem = installedId == null ? null : installedPackages.FirstOrDefault(p => p.Id == installedId);
                }
                finally { refreshing = false; }
                Select(tabs.SelectedIndex == 1 ? installed.SelectedItem : available.SelectedItem);
            }
            void Select(object? item)
            {
                if (item == null) { details.Content = null; return; }
                StackPanel panel = new() { Spacing = 12, DataContext = item };
                foreach (string field in item is DependencyItemViewModel ? new[] { "Name", "Developers", "Category", "LatestVersion" } : new[] { "Id", "Description", "Version" })
                {
                    if (field is "LatestVersion" or "Version") panel.Children.Add(DesktopUi.Label(field == "LatestVersion" ? "DependencyManager.LatestVersion" : "DependencyManager.InstalledVersion"));
                    TextBlock text = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap }; text.Bind(TextBlock.TextProperty, new Binding(field)); panel.Children.Add(text);
                }
                Button action = DesktopUi.Command(item is DependencyItemViewModel ? "Desktop.Install" : "Desktop.Uninstall", item is DependencyItemViewModel ? "InstallCommand" : "UninstallCommand");
                if (item is DependencyItemViewModel)
                {
                    TextBlock state = new(); state.Bind(TextBlock.TextProperty, new Binding("ButtonText")); action.Content = state;
                    StackPanel version = new() { Spacing = 4 };
                    version.Bind(IsVisibleProperty, new Binding("IsInstalled"));
                    version.Children.Add(DesktopUi.Label("DependencyManager.InstalledVersion"));
                    TextBlock installedVersion = new(); installedVersion.Bind(TextBlock.TextProperty, new Binding("InstalledVersion")); version.Children.Add(installedVersion);
                    panel.Children.Add(version);
                }
                panel.Children.Add(action);
                if (item is DependencyItemViewModel)
                {
                    ProgressBar progress = new() { Minimum = 0, Maximum = 100 };
                    progress.Bind(IsVisibleProperty, new Binding("IsInstalling"));
                    progress.Bind(ProgressBar.ValueProperty, new Binding("InstallProgress")); panel.Children.Add(progress);
                }
                details.Content = item == null ? null : DesktopUi.Scroll(panel, false);
            }
            void UpdateStates()
            {
                bool hasQuery = !string.IsNullOrWhiteSpace(dependencies.SearchText);
                bool availableBusy = dependencies.IsLoadingRegistry;
                bool installedBusy = dependencies.IsLoadingInstalled;
                available.IsVisible = !availableBusy && string.IsNullOrEmpty(dependencies.RegistryError) && dependencies.FilteredAvailablePackages.Count > 0;
                availableLoading.IsVisible = availableBusy;
                availableError.Text = dependencies.RegistryError;
                availableError.IsVisible = !availableBusy && !string.IsNullOrEmpty(dependencies.RegistryError);
                availableEmpty.IsVisible = !availableBusy && string.IsNullOrEmpty(dependencies.RegistryError) && !hasQuery && dependencies.AvailablePackages.Count == 0;
                availableNoResults.IsVisible = !availableBusy && string.IsNullOrEmpty(dependencies.RegistryError) && hasQuery && dependencies.FilteredAvailablePackages.Count == 0;
                installed.IsVisible = !installedBusy && string.IsNullOrEmpty(dependencies.InstalledError) && dependencies.FilteredInstalledPackages.Count > 0;
                installedLoading.IsVisible = installedBusy;
                installedError.Text = dependencies.InstalledError;
                installedError.IsVisible = !installedBusy && !string.IsNullOrEmpty(dependencies.InstalledError);
                installedEmpty.IsVisible = !installedBusy && string.IsNullOrEmpty(dependencies.InstalledError) && !hasQuery && dependencies.InstalledPackages.Count == 0;
                installedNoResults.IsVisible = !installedBusy && string.IsNullOrEmpty(dependencies.InstalledError) && hasQuery && dependencies.FilteredInstalledPackages.Count == 0;
            }
            tabs.SelectionChanged += (_, e) =>
            {
                if (e.Source != tabs) return;
                bool installedTab = tabs.SelectedIndex == 1;
                refreshAvailable.IsVisible = !installedTab;
                refreshInstalled.IsVisible = installedTab;
                sort.IsVisible = !installedTab;
                Select(installedTab ? installed.SelectedItem : available.SelectedItem);
            };
            available.SelectionChanged += (_, _) => { if (!refreshing && tabs.SelectedIndex != 1) Select(available.SelectedItem); };
            installed.SelectionChanged += (_, _) => { if (!refreshing && tabs.SelectedIndex == 1) Select(installed.SelectedItem); };
            PropertyChangedEventHandler changed = (_, e) =>
            {
                if (e.PropertyName is nameof(dependencies.FilteredAvailablePackages) or nameof(dependencies.FilteredInstalledPackages) or
                    nameof(dependencies.IsLoadingRegistry) or nameof(dependencies.IsLoadingInstalled) or nameof(dependencies.RegistryError) or
                    nameof(dependencies.InstalledError) or nameof(dependencies.SearchText))
                {
                    if (e.PropertyName is nameof(dependencies.FilteredAvailablePackages) or nameof(dependencies.FilteredInstalledPackages)) Refresh();
                    UpdateStates();
                }
            };
            AttachedToVisualTree += (_, _) => { dependencies.PropertyChanged += changed; Refresh(); };
            DetachedFromVisualTree += (_, _) => dependencies.PropertyChanged -= changed;
            columns.Children.Add(DesktopUi.Panel(tabs));
            DesktopPanelSplitter splitter = new() { ResizeDirection = GridResizeDirection.Columns, VerticalAlignment = VerticalAlignment.Stretch };
            Grid.SetColumn(splitter, 1); columns.Children.Add(splitter);
            Grid.SetColumn(details, 2); columns.Children.Add(details);
            Grid.SetRow(columns, 1); root.Children.Add(columns);
            Content = root;
            UpdateStates();
        }

        private static Grid StatePanel(ListBox list, params TextBlock[] states)
        {
            Grid content = new();
            content.Children.Add(list);
            StackPanel stateContent = new() { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 420 };
            foreach (TextBlock state in states)
            {
                state.Classes.Add("DesktopSecondary");
                state.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
                stateContent.Children.Add(state);
            }
            stateContent.IsHitTestVisible = false;
            content.Children.Add(stateContent);
            return content;
        }
    }
}
