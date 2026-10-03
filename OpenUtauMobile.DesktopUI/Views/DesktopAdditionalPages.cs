using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.DesktopUI.Views
{
    internal sealed class DesktopUnavailablePage : UserControl
    {
        public DesktopUnavailablePage(string route, Action? close = null)
        {
            StackPanel content = new() { Spacing = 8, MaxWidth = 560 };
            TextBlock title = DesktopUi.Label("Desktop.SurfaceUnavailable");
            title.Classes.Add("DesktopContentTitle");
            content.Children.Add(title);
            TextBlock detail = new() { Text = route, TextWrapping = TextWrapping.Wrap };
            detail.Classes.Add("DesktopSecondary");
            content.Children.Add(detail);
            if (close != null) content.Children.Add(DesktopUi.Action("Common.Close", close));
            Content = new Grid { Children = { content }, Margin = new Thickness(16), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        }
    }

    internal sealed partial class DesktopAboutPage : UserControl
    {
        public DesktopAboutPage(AboutViewModel model)
        {
            Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
            DataContext = model;
        }
    }

    internal sealed partial class DesktopSingerDetailPage : UserControl
    {
        public DesktopSingerDetailPage(SingerDetailViewModel model)
        {
            Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
            DataContext = model;
        }
    }

    internal sealed class DesktopExportLogsPage : UserControl
    {
        private readonly ExportLogsViewModel _model;
        private readonly ListBox _list;
        private readonly TextBlock _loading;
        private readonly StackPanel _emptyState;
        private readonly TextBlock _error;

        public DesktopExportLogsPage(ExportLogsViewModel model)
        {
            _model = model;
            DataContext = model;
            Grid root = new() { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 8 };
            Grid toolbar = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"), ColumnSpacing = 8 };
            TextBlock hint = DesktopUi.Label("ExportLogs.Hint");
            hint.Classes.Add("DesktopSecondary");
            toolbar.Children.Add(hint);
            Button refresh = Command("ExportLogs.Refresh", nameof(model.RefreshCommand));
            Grid.SetColumn(refresh, 1);
            toolbar.Children.Add(refresh);
            Button all = Command("Common.SelectAll", nameof(model.SelectAllCommand));
            Grid.SetColumn(all, 2);
            toolbar.Children.Add(all);
            Button none = Command("ExportLogs.SelectNone", nameof(model.SelectNoneCommand));
            Grid.SetColumn(none, 3);
            toolbar.Children.Add(none);
            root.Children.Add(toolbar);

            _list = new ListBox();
            _list.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(model.Logs)));
            _list.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<LogFileItemViewModel>((item, _) =>
            {
                Grid row = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), ColumnSpacing = 12, MinHeight = 36 };
                CheckBox selected = new();
                Binding selection = new(nameof(item.IsSelected)) { Mode = BindingMode.TwoWay };
                selected.Bind(ToggleButton.IsCheckedProperty, selection);
                row.Children.Add(selected);
                TextBlock name = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                name.Bind(TextBlock.TextProperty, new Binding(nameof(item.Name)));
                Grid.SetColumn(name, 1);
                row.Children.Add(name);
                TextBlock modified = new() { VerticalAlignment = VerticalAlignment.Center };
                modified.Classes.Add("DesktopMetadata");
                modified.Bind(TextBlock.TextProperty, new Binding(nameof(item.ModifiedAt)));
                Grid.SetColumn(modified, 2);
                row.Children.Add(modified);
                TextBlock size = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 64, TextAlignment = TextAlignment.Right };
                size.Classes.Add("DesktopMetadata");
                size.Bind(TextBlock.TextProperty, new Binding(nameof(item.Size)));
                Grid.SetColumn(size, 3);
                row.Children.Add(size);
                return row;
            }, true);
            Grid.SetRow(_list, 1);
            root.Children.Add(_list);

            StackPanel states = new() { Spacing = 8, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            _loading = DesktopUi.Label("ExportLogs.Loading");
            states.Children.Add(_loading);
            _emptyState = new StackPanel { Spacing = 4 };
            TextBlock emptyTitle = DesktopUi.Label("ExportLogs.EmptyTitle");
            emptyTitle.Classes.Add("DesktopContentTitle");
            _emptyState.Children.Add(emptyTitle);
            TextBlock emptyDescription = DesktopUi.Label("ExportLogs.EmptyDescription");
            emptyDescription.Classes.Add("DesktopSecondary");
            _emptyState.Children.Add(emptyDescription);
            states.Children.Add(_emptyState);
            _error = new TextBlock();
            _error.Bind(TextBlock.TextProperty, new Binding(nameof(model.ErrorMessage)));
            states.Children.Add(_error);
            Grid.SetRow(states, 1);
            root.Children.Add(states);

            Grid footer = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 8 };
            TextBlock summary = new();
            summary.Bind(TextBlock.TextProperty, new Binding(nameof(model.SelectionSummary)));
            summary.Classes.Add("DesktopSecondary");
            footer.Children.Add(summary);
            Button crash = Command("ExportLogs.ExportCrashLogs", nameof(model.ExportCrashLogsCommand));
            crash.Bind(IsVisibleProperty, new Binding(nameof(model.IsCrashLogExportAvailable)));
            Grid.SetColumn(crash, 1);
            footer.Children.Add(crash);
            Button export = Command("ExportLogs.Export", nameof(model.ExportCommand));
            Grid.SetColumn(export, 2);
            footer.Children.Add(export);
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);
            Content = root;
            RefreshStates();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _model.PropertyChanged += OnModelPropertyChanged;
            _model.Logs.CollectionChanged += OnLogsChanged;
            RefreshStates();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            _model.PropertyChanged -= OnModelPropertyChanged;
            _model.Logs.CollectionChanged -= OnLogsChanged;
            base.OnDetachedFromVisualTree(e);
        }

        private void OnModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => RefreshStates();
        private void OnLogsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => RefreshStates();

        private void RefreshStates()
        {
            _loading.IsVisible = _model.IsLoading;
            _emptyState.IsVisible = !_model.IsLoading && !_model.HasError && !_model.HasLogs;
            _error.IsVisible = !_model.IsLoading && _model.HasError;
            _list.IsVisible = !_model.IsLoading && _model.HasLogs;
        }

        private Button Command(string key, string path)
        {
            Button button = new() { Content = DesktopUi.Label(key) };
            button.Classes.Add("DesktopAction");
            button.DataContext = DataContext;
            button.Bind(Button.CommandProperty, new Binding(path));
            return button;
        }
    }

    internal sealed class DesktopOptionsPage : UserControl
    {
        public DesktopOptionsPage(OptionsViewModel model)
        {
            DataContext = model;
            StackPanel actions = new() { Spacing = 8, MaxWidth = 560 };
            actions.Children.Add(Command(model, "Options.Settings", nameof(model.OpenSettingsCommand)));
            actions.Children.Add(Command(model, "Options.DependencyManager", nameof(model.OpenDependencyManagerCommand)));
            Button help = Command(model, "Options.Help", nameof(model.OpenHelpCommand));
            help.IsEnabled = false;
            ToolTip.SetTip(help, DesktopUi.Label("Options.Toast.HelpNotImpl"));
            actions.Children.Add(help);
            actions.Children.Add(Command(model, "Options.ExportLog", nameof(model.OpenExportLogsCommand)));
            actions.Children.Add(Command(model, "Options.About", nameof(model.OpenAboutCommand)));
            Content = DesktopUi.Scroll(actions);
        }

        private static Button Command(object context, string key, string path)
        {
            Button button = new() { Content = DesktopUi.Label(key), HorizontalContentAlignment = HorizontalAlignment.Left };
            button.Classes.Add("DesktopAction");
            button.DataContext = context;
            button.Bind(Button.CommandProperty, new Binding(path));
            return button;
        }
    }
}
