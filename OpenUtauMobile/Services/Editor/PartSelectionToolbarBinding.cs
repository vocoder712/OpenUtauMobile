using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using OpenUtauMobile.Controls;
using OpenUtauMobile.Themes.OpenUtauMobile.Controls;
using OpenUtauMobile.ViewModels;
using ReactiveUI;

namespace OpenUtauMobile.Services.Editor;

/// <summary>在移动与桌面编辑页中，将最后选中的分片映射到页级浮动工具栏。</summary>
public sealed class PartSelectionToolbarBinding : IDisposable
{
    private readonly FloatingToolbar _toolbar;
    private readonly Control _viewport;
    private readonly PartsCanvas _parts;
    private readonly EditorViewModel _editor;
    private readonly Func<bool> _isArrangementVisible;
    private readonly Action _activateArrangement;
    private readonly CompositeDisposable _subscriptions = new();

    /// <summary>绑定分片选区和视口变换；视口为工具栏所在的页级覆盖层。</summary>
    public PartSelectionToolbarBinding(FloatingToolbar toolbar, Control viewport, PartsCanvas parts,
        EditorViewModel editor, Func<bool> isArrangementVisible, Action activateArrangement)
    {
        _toolbar = toolbar;
        _viewport = viewport;
        _parts = parts;
        _editor = editor;
        _isArrangementVisible = isArrangementVisible;
        _activateArrangement = activateArrangement;
        viewport.LayoutUpdated += OnLayoutUpdated;
        toolbar.AddHandler(InputElement.PointerPressedEvent, OnToolbarPressed, RoutingStrategies.Tunnel);
        Observable.FromEvent(
                handler => editor.RequestInvalidateVisual += handler,
                handler => editor.RequestInvalidateVisual -= handler)
            .Subscribe(_ => UpdateAnchor())
            .DisposeWith(_subscriptions);
        editor.WhenAnyValue(model => model.PartToolbarActions, model => model.IsPartToolbarExpanded)
            .Subscribe(_ => UpdateAnchor())
            .DisposeWith(_subscriptions);
    }

    /// <summary>页面分割、视口滚动和窗口尺寸变化后重新换算锚点。</summary>
    private void OnLayoutUpdated(object? sender, EventArgs e) => UpdateAnchor();

    /// <summary>操作工具栏时将键盘快捷键的目标切回走带编曲。</summary>
    private void OnToolbarPressed(object? sender, PointerPressedEventArgs e) => _activateArrangement();

    /// <summary>将最后选中的分片从画布坐标转换到页级覆盖层坐标。</summary>
    private void UpdateAnchor()
    {
        if (!_isArrangementVisible() || _editor.IsLoadingProject || _editor.GetPartToolbarAnchorRect() is not { } anchor)
        {
            _toolbar.IsVisible = false;
            return;
        }
        Point? origin = _parts.TranslatePoint(anchor.TopLeft, _viewport);
        _toolbar.IsVisible = origin.HasValue;
        if (origin.HasValue) _toolbar.AnchorRect = new Rect(origin.Value, anchor.Size);
    }

    /// <summary>离开页面后解除选区、布局与输入订阅。</summary>
    public void Dispose()
    {
        _viewport.LayoutUpdated -= OnLayoutUpdated;
        _toolbar.RemoveHandler(InputElement.PointerPressedEvent, OnToolbarPressed);
        _subscriptions.Dispose();
        _toolbar.IsVisible = false;
    }
}
