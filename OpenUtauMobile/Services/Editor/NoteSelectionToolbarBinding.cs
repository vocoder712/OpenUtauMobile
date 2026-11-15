using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using OpenUtauMobile.Controls;
using OpenUtauMobile.Themes.OpenUtauMobile.Controls;
using OpenUtauMobile.ViewModels;
using ReactiveUI;

namespace OpenUtauMobile.Services.Editor;

/// <summary>在移动与桌面编辑页中，将选区或新建占位框映射到页级浮动工具栏。</summary>
public sealed class NoteSelectionToolbarBinding : IDisposable
{
    private readonly FloatingToolbar _toolbar;
    private readonly Control _viewport;
    private readonly NotesCanvas _notes;
    private readonly Control _creationPreview;
    private readonly EditorViewModel _editor;
    private readonly Func<bool> _isPianoVisible;
    private readonly Action _activatePiano;
    private readonly CompositeDisposable _subscriptions = new();

    /// <summary>跟随编辑页的挂载生命周期建立绑定；视口应为工具栏所在的覆盖层。</summary>
    public NoteSelectionToolbarBinding(FloatingToolbar toolbar, Control viewport, NotesCanvas notes, Control creationPreview,
        EditorViewModel editor, Func<bool> isPianoVisible, Action activatePiano)
    {
        _toolbar = toolbar;
        _viewport = viewport;
        _notes = notes;
        _creationPreview = creationPreview;
        _editor = editor;
        _isPianoVisible = isPianoVisible;
        _activatePiano = activatePiano;
        viewport.LayoutUpdated += OnLayoutUpdated;
        viewport.AddHandler(InputElement.PointerPressedEvent, OnViewportPressed, RoutingStrategies.Tunnel, true);
        viewport.AddHandler(InputElement.KeyDownEvent, OnViewportKeyDown, RoutingStrategies.Tunnel, true);
        toolbar.AddHandler(InputElement.PointerPressedEvent, OnToolbarPressed, RoutingStrategies.Tunnel);
        Observable.FromEvent(
                handler => editor.PianoRollViewModel.RequestInvalidateVisual += handler,
                handler => editor.PianoRollViewModel.RequestInvalidateVisual -= handler)
            .Subscribe(_ => UpdateNoteToolbarAnchor())
            .DisposeWith(_subscriptions);
        editor.PianoRollViewModel.WhenAnyValue(model => model.NoteToolbarActions)
            .Subscribe(_ => UpdateNoteToolbarAnchor())
            .DisposeWith(_subscriptions);
    }

    /// <summary>页面布局、分割和窗口尺寸变化后重新换算坐标。</summary>
    private void OnLayoutUpdated(object? sender, EventArgs e) => UpdateNoteToolbarAnchor();

    /// <summary>选区工具栏的输入始终归属钢琴卷帘。</summary>
    private void OnToolbarPressed(object? sender, PointerPressedEventArgs e) => _activatePiano();

    /// <summary>画布内点击交给音符手势；点击页面其他区域时收起新建占位框。</summary>
    private void OnViewportPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_editor.PianoRollViewModel.IsNoteCreationPreviewVisible) return;
        if (e.Source is Visual source &&
            (source == _notes || _notes.IsVisualAncestorOf(source) ||
             source == _creationPreview || _creationPreview.IsVisualAncestorOf(source) ||
             source == _toolbar || _toolbar.IsVisualAncestorOf(source))) return;
        _editor.PianoRollViewModel.DismissNoteCreationPreview();
    }

    /// <summary>占位框及其工具栏允许通过 Escape 取消。</summary>
    private void OnViewportKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || !_editor.PianoRollViewModel.IsNoteCreationPreviewVisible) return;
        _editor.PianoRollViewModel.DismissNoteCreationPreview();
        e.Handled = true;
    }

    /// <summary>将当前选区的画布锚点转换到编辑页覆盖层，覆盖音符笔、多选和锚点模式。</summary>
    private void UpdateNoteToolbarAnchor()
    {
        if (!_isPianoVisible() || _editor.IsLoadingProject ||
            _editor.PianoRollViewModel.GetSelectionToolbarAnchorRect() is not { } anchor)
        {
            _toolbar.IsVisible = false;
            return;
        }
        Point? origin = _notes.TranslatePoint(anchor.TopLeft, _viewport);
        _toolbar.IsVisible = origin.HasValue;
        if (origin.HasValue)
        {
            _toolbar.AnchorRect = new Rect(origin.Value, anchor.Size);
        }
    }

    /// <summary>解除页级输入、布局与视图模型订阅，离开页面后不再更新工具栏。</summary>
    public void Dispose()
    {
        _viewport.LayoutUpdated -= OnLayoutUpdated;
        _viewport.RemoveHandler(InputElement.PointerPressedEvent, OnViewportPressed);
        _viewport.RemoveHandler(InputElement.KeyDownEvent, OnViewportKeyDown);
        _toolbar.RemoveHandler(InputElement.PointerPressedEvent, OnToolbarPressed);
        _subscriptions.Dispose();
        _editor.PianoRollViewModel.DismissNoteCreationPreview();
        _toolbar.IsVisible = false;
    }
}
