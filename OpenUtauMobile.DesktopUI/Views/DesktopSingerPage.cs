using System;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Threading;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.ViewModels;
using OpenUtauMobile.Views;

namespace OpenUtauMobile.DesktopUI.Views
{
    internal sealed class DesktopSingerPage : UserControl
    {
        public DesktopSingerPage(MainViewModel main, SingerManagementViewModel singers)
        {
            DataContext = singers;
            Grid root = new() { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 12 };
            Grid toolbar = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
            TextBox search = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
            search.Bind(TextBox.PlaceholderTextProperty, search.GetResourceObservable("Desktop.Search"));
            toolbar.Children.Add(search);
            Button install = DesktopUi.Command("SingerManagement.Install", "AddSingerCommand");
            StackPanel installLabel = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
            installLabel.Children.Add(new IconPacks.Avalonia.PhosphorIcons.PackIconPhosphorIcons { Kind = IconPacks.Avalonia.PhosphorIcons.PackIconPhosphorIconsKind.Plus, Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center });
            installLabel.Children.Add(DesktopUi.Label("SingerManagement.Install"));
            install.Content = installLabel;
            install.Classes.Add("tonal");
            TextBlock uninstallStatus = new() { IsVisible = false, VerticalAlignment = VerticalAlignment.Center, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            DesktopUi.Paint(uninstallStatus, TextBlock.ForegroundProperty, "Sem.Color.Success");
            StackPanel actions = new() { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center, Children = { uninstallStatus, install } };
            Grid.SetColumn(actions, 1);
            toolbar.Children.Add(actions);
            root.Children.Add(toolbar);
            Grid columns = new() { ColumnDefinitions = new ColumnDefinitions("260,*"), ColumnSpacing = 20 };
            columns.SizeChanged += (_, e) => columns.ColumnDefinitions[0].Width = new GridLength(e.NewSize.Width < 700 ? 180 : 260);
            ListBox list = new();
            list.ItemTemplate = new FuncDataTemplate<USinger>((singer, _) => singer == null ? null : new DesktopSingerItem(singer, 48));
            columns.ColumnDefinitions[0].MinWidth = 180;
            ContentControl details = new() { ClipToBounds = true };
            SingerDetailViewModel? detailModel = null;
            USinger? selectedSinger = null;
            bool refreshing = false;
            void ShowDetail()
            {
                USinger? selected = list.SelectedItem as USinger;
                if (selected == selectedSinger) return;
                selectedSinger = selected;
                if (detailModel != null) detailModel.SingerUninstalled -= OnSingerUninstalled;
                detailModel?.Dispose();
                detailModel = selected == null ? null : new SingerDetailViewModel(main, selected);
                if (detailModel != null) detailModel.SingerUninstalled += OnSingerUninstalled;
                details.Content = detailModel == null ? null : new DesktopSingerDetailPage(detailModel);
            }
            void OnSingerUninstalled(USinger singer)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    uninstallStatus.Text = string.Format(OpenUtauMobile.Helpers.L.S("SingerDetail.UninstallSucceeded"), singer.LocalizedName);
                    uninstallStatus.IsVisible = true;
                    singers.Singers.Remove(singer);
                });
            }
            void Refresh()
            {
                USinger? selected = list.SelectedItem as USinger;
                USinger[] filtered = singers.Singers.Where(s => (s.LocalizedName + " " + s.Id + " " + s.Author).Contains(search.Text ?? "", StringComparison.CurrentCultureIgnoreCase)).ToArray();
                refreshing = true;
                try { list.ItemsSource = filtered; list.SelectedItem = selected != null && filtered.Contains(selected) ? selected : null; }
                finally { refreshing = false; }
                ShowDetail();
            }
            search.TextChanged += (_, _) => Refresh();
            NotifyCollectionChangedEventHandler changed = (_, _) => Refresh();
            AttachedToVisualTree += (_, _) => { singers.Singers.CollectionChanged += changed; Refresh(); };
            DetachedFromVisualTree += (_, _) => { singers.Singers.CollectionChanged -= changed; if (detailModel != null) detailModel.SingerUninstalled -= OnSingerUninstalled; detailModel?.Dispose(); detailModel = null; selectedSinger = null; details.Content = null; };
            list.SelectionChanged += (_, _) => { if (!refreshing) ShowDetail(); };
            columns.Children.Add(DesktopUi.Panel(list, false));
            Grid.SetColumn(details, 1); columns.Children.Add(details);
            Grid.SetRow(columns, 1); root.Children.Add(columns);
            Content = root;
        }
    }
}
