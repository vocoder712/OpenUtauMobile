using System;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using OpenUtauMobile.Themes.OpenUtauMobile.Tokens.Foundation;

namespace OpenUtauMobile.Controls;

/// <summary>编辑模式的普通覆盖控件；固定矩形内完成呈现，不创建或移动 Popup。</summary>
public partial class EditorModeLayer : UserControl
{
    private OverlayLayer? _host;
    private TopLevel? _topLevel;
    private Button? _trigger;
    private Control? _previousFocus;
    private readonly ScaleTransform _scale = new(1, 1);
    private Rect _triggerRect;
    private Rect _finalRect;
    private double _expansion;
    private int _motionVersion;

    /// <summary>当前模式选项的固定内容容器。</summary>
    internal WrapPanel Items => ModeItems;
    /// <summary>沿用入口本地化标题。</summary>
    internal TextBlock TitleLabel => ModeTitle;
    /// <summary>展开或收回过程中独立层均保持打开。</summary>
    internal bool IsOpen { get; private set; }
    /// <summary>当前是否正在收回。</summary>
    internal bool IsClosing { get; private set; }

    internal event Action? ToggleRequested;
    internal event Action? Closed;

    /// <summary>初始化固定视口、表面和背景输入处理。</summary>
    public EditorModeLayer()
    {
        InitializeComponent();
        ModeSurface.RenderTransform = _scale;
        AddHandler(PointerPressedEvent, OnLayerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerWheelChangedEvent, (_, e) => e.Handled = true, RoutingStrategies.Bubble);
        AddHandler(PointerTouchPadGestureMagnifyEvent, (_, e) => e.Handled = true, RoutingStrategies.Bubble);
    }

    /// <summary>随入口挂到同一窗口的普通覆盖层，整个挂载期间不再更换父容器。</summary>
    internal void Mount(Control owner, Button button)
    {
        Unmount();
        _trigger = button;
        _topLevel = TopLevel.GetTopLevel(owner);
        _host = OverlayLayer.GetOverlayLayer(owner);
        _host?.Children.Add(this);
        if (_topLevel == null) return;
        _topLevel.AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        _topLevel.SizeChanged += OnWindowSizeChanged;
    }

    /// <summary>入口离开窗口时才卸载独立层，并终止所有旧帧。</summary>
    internal void Unmount()
    {
        Hide(immediate: true);
        if (_topLevel != null)
        {
            _topLevel.RemoveHandler(KeyDownEvent, OnWindowKeyDown);
            _topLevel.SizeChanged -= OnWindowSizeChanged;
        }
        _host?.Children.Remove(this);
        _host = null;
        _topLevel = null;
        _trigger = null;
    }

    /// <summary>先测量内容并固定最终矩形；反向展开直接复用该矩形。</summary>
    internal void Show(string title, PlacementMode placement)
    {
        if (_host == null || _topLevel == null || _trigger == null) return;
        if (IsOpen)
        {
            if (IsClosing) StartMotion(opening: true);
            return;
        }
        Thickness safe = _topLevel.InsetsManager?.SafeAreaPadding ?? default;
        Rect available = new Rect(_host.Bounds.Size).Deflate(safe).Deflate(8);
        if (available.Width <= 0 || available.Height <= 0) return;
        Point? anchor = _trigger.TranslatePoint(default, _host);
        if (anchor == null) return;
        _triggerRect = new Rect(anchor.Value, _trigger.Bounds.Size);
        ModeTitle.Text = title;
        UpdateExpansion(0);
        Width = _host.Bounds.Width;
        Height = _host.Bounds.Height;
        IsVisible = true;
        ModeSurface.ApplyStyling();
        double chromeWidth = ModeSurface.Padding.Left + ModeSurface.Padding.Right + 2;
        double chromeHeight = ModeSurface.Padding.Top + ModeSurface.Padding.Bottom + 2;
        ModeScroll.MaxWidth = Math.Max(1, available.Width - chromeWidth);
        ModeScroll.MaxHeight = Math.Max(1, available.Height - chromeHeight);
        ModeScroll.Offset = default;
        // 直接测量业务内容，首次打开也不依赖 ScrollViewer 模板的延迟初始化。
        ModeContent.Measure(new Size(ModeScroll.MaxWidth, double.PositiveInfinity));
        Size size = new(
            Math.Min(available.Width, ModeContent.DesiredSize.Width + chromeWidth),
            Math.Min(available.Height, ModeContent.DesiredSize.Height + chromeHeight));
        double x = placement == PlacementMode.Top ? _triggerRect.Left : _triggerRect.Left - size.Width - 4;
        double y = placement == PlacementMode.Top ? _triggerRect.Top - size.Height - 4 : _triggerRect.Center.Y - size.Height / 2;
        if (placement == PlacementMode.Top && y < available.Top) y = _triggerRect.Bottom + 4;
        if (placement != PlacementMode.Top && x < available.Left && _triggerRect.Right + 4 + size.Width <= available.Right)
            x = _triggerRect.Right + 4;
        _finalRect = new Rect(
            Math.Clamp(x, available.Left, Math.Max(available.Left, available.Right - size.Width)),
            Math.Clamp(y, available.Top, Math.Max(available.Top, available.Bottom - size.Height)),
            size.Width, size.Height);
        ModeViewport.Width = _finalRect.Width;
        ModeViewport.Height = _finalRect.Height;
        Canvas.SetLeft(ModeViewport, _finalRect.X);
        Canvas.SetTop(ModeViewport, _finalRect.Y);
        _previousFocus = _topLevel.FocusManager?.GetFocusedElement() as Control;
        IsOpen = true;
        StartMotion(opening: true);
        ModeItems.Children.OfType<Button>().FirstOrDefault(button => button.Classes.Contains("Selected"))?.Focus();
    }

    /// <summary>普通关闭先播放退出动画，页面变化则立即隐藏，不卸载父容器。</summary>
    internal void Hide(bool immediate = false)
    {
        if (!IsOpen) return;
        if (immediate) FinishClose();
        else if (!IsClosing) StartMotion(opening: false);
    }

    /// <summary>外部点击只负责关闭；入口区域可在收回途中直接反向展开。</summary>
    private void OnLayerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsOpen || _finalRect.Contains(e.GetPosition(this))) return;
        e.Handled = true;
        if (_triggerRect.Contains(e.GetPosition(this))) ToggleRequested?.Invoke();
        else Hide();
    }

    /// <summary>局部接管 Esc 和 Tab，避免输入落到下方编辑画布。</summary>
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (!IsOpen) return;
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Hide();
        }
        else if (e.Key == Key.Tab)
        {
            IInputElement?[] buttons = [.. ModeItems.Children.OfType<Button>()];
            if (buttons.Length == 0) return;
            int current = Array.IndexOf(buttons, _topLevel?.FocusManager?.GetFocusedElement());
            int step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1;
            buttons[(current + step + buttons.Length) % buttons.Length]?.Focus(NavigationMethod.Tab);
            e.Handled = true;
        }
    }

    private void OnWindowSizeChanged(object? sender, SizeChangedEventArgs e) => Hide(immediate: true);

    /// <summary>只更新固定矩形内的表面，连续开关从当前进度反向且令旧帧失效。</summary>
    private void StartMotion(bool opening)
    {
        if (_topLevel == null) return;
        int version = ++_motionVersion;
        IsClosing = !opening;
        ModeSurface.IsHitTestVisible = opening;
        double from = _expansion;
        double target = opening ? 1 : 0;
        Stopwatch clock = Stopwatch.StartNew();
        TimeSpan duration = opening ? MotionTokens.DurationMedium : MotionTokens.DurationExit;
        Easing easing = opening ? new CubicEaseOut() : new CubicEaseIn();
        _topLevel.RequestAnimationFrame(Frame);
        return;

        void Frame(TimeSpan timestamp)
        {
            if (version != _motionVersion || !IsOpen) return;
            double progress = Math.Min(1, clock.Elapsed.TotalMilliseconds / duration.TotalMilliseconds);
            UpdateExpansion(from + (target - from) * easing.Ease(progress));
            if (progress >= 1)
            {
                if (!opening) FinishClose();
                return;
            }
            _topLevel?.RequestAnimationFrame(Frame);
        }
    }

    /// <summary>缩放始终小于等于一，居中原点不引用鼠标或任何窗口坐标。</summary>
    private void UpdateExpansion(double progress)
    {
        _expansion = progress;
        _scale.ScaleX = _scale.ScaleY = 0.92 + 0.08 * progress;
        ModeSurface.Opacity = progress;
    }

    /// <summary>清理呈现状态并恢复原焦点，下一次打开不会继承旧坐标或帧。</summary>
    private void FinishClose()
    {
        _motionVersion++;
        IsOpen = false;
        IsClosing = false;
        Control? focused = _topLevel?.FocusManager?.GetFocusedElement() as Control;
        IsVisible = false;
        UpdateExpansion(0);
        if (focused?.GetSelfAndVisualAncestors().Contains(this) == true)
        {
            Control? restore = _previousFocus?.IsEffectivelyVisible == true ? _previousFocus : _trigger;
            if (restore?.IsEffectivelyVisible == true) restore.Focus();
        }
        _previousFocus = null;
        Closed?.Invoke();
    }
}
