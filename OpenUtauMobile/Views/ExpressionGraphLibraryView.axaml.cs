using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using OpenUtauMobile.Controls;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Views;

public partial class ExpressionGraphLibraryView : UserControl
{
    private ExpressionGraphLibraryViewModel? _vm;

    public ExpressionGraphLibraryView()
    {
        InitializeComponent();
        CreateMenu.ActionInvoked += CreateAction;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && DataContext is ExpressionGraphLibraryViewModel vm)
            { vm.OnBackRequested(); e.Handled = true; }
        };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _vm = DataContext as ExpressionGraphLibraryViewModel;
        if (_vm != null) _vm.RequestRevealGraph += Reveal;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_vm != null) _vm.RequestRevealGraph -= Reveal;
        _vm = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void Reveal(string id)
    {
        ExpressionGraphLibraryItem? item = _vm?.Graphs.FirstOrDefault(graph => graph.Id == id);
        if (item == null) return;
        // 图库没有选择状态；等新增卡片完成布局后，仅滚动到它的位置。
        Dispatcher.UIThread.Post(() => GraphList.ContainerFromItem(item)?.BringIntoView(), DispatcherPriority.Loaded);
    }

    private void CreateAction(FabMenuAction action)
    {
        if (DataContext is ExpressionGraphLibraryViewModel vm && !vm.IsBusy)
            vm.CreateCommand.Execute(action.Id).Subscribe();
    }

    private void PageMenuClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || DataContext is not ExpressionGraphLibraryViewModel vm) return;
        ContextMenu menu = new()
        {
            Items =
            {
                new MenuItem { Header = L.S("ExpressionGraph.Import"), Command = vm.ImportCommand, IsEnabled = !vm.IsBusy && vm.IsCurrentProject },
                new MenuItem { Header = L.S("ExpressionGraph.UndoProject"), Command = vm.UndoCommand, IsEnabled = vm.CanUndo && !vm.IsBusy },
                new MenuItem { Header = L.S("ExpressionGraph.RedoProject"), Command = vm.RedoCommand, IsEnabled = vm.CanRedo && !vm.IsBusy },
            },
        };
        menu.Open(button);
    }

    private void GraphMenuClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ExpressionGraphLibraryItem item } button
            || DataContext is not ExpressionGraphLibraryViewModel vm) return;
        ContextMenu menu = new()
        {
            Items =
            {
                new MenuItem { Header = L.S("ExpressionGraph.Settings"), Command = vm.SettingsCommand, CommandParameter = item, IsEnabled = !vm.IsBusy && vm.IsCurrentProject },
                new MenuItem { Header = L.S("ExpressionGraph.Export"), Command = vm.ExportCommand, CommandParameter = item, IsEnabled = !vm.IsBusy && vm.IsCurrentProject },
                new MenuItem { Header = L.S("ExpressionGraph.Copy"), Command = vm.DuplicateCommand, CommandParameter = item },
                new MenuItem { Header = L.S("Common.Delete"), Command = vm.DeleteCommand, CommandParameter = item },
            },
        };
        menu.Open(button);
    }
}
