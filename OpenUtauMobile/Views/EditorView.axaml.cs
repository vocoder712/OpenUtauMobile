using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using OpenUtauMobile.ViewModels;
using ReactiveUI;

namespace OpenUtauMobile.Views;

public partial class EditorView : UserControl
{
    // ── 分割拖拽状态 ──
    private bool _splitPointerDown;
    private bool _splitDragging;
    private Point _splitPressAbsPoint;
    private double _splitPressPrimaryExtent;
    private double _splitPressHandleAxisRatio;
    private double _portraitTrackExtent = ViewConstants.EditorSplitDefaultTrackExtentPortrait;
    private double _landscapeTrackExtent;
    private double _portraitHandleAxisRatio = 0.5;
    private double _landscapeHandleAxisRatio = 0.5;
    private bool _isLandscape;
    private bool _responsiveLayoutInitialized;

    // ── 轨道头列宽动画 ──
    private IDisposable? _viewModelSubscription;
    private DispatcherTimer? _widthAnimTimer;
    private double _widthAnimTarget;
    private double _widthAnimStart;
    private DateTime _widthAnimStartTime;

    public EditorView()
    {
        InitializeComponent();

        SplitDragHandle.PointerPressed += OnSplitHandlePointerPressed;
        SplitDragHandle.PointerMoved += OnSplitHandlePointerMoved;
        SplitDragHandle.PointerReleased += OnSplitHandlePointerReleased;
        SplitDragHandle.PointerCaptureLost += OnSplitHandlePointerCaptureLost;
        AttachedToVisualTree += (_, _) => InitializeResponsiveLayout();

        // 初始化轨道头列宽动画
        InitializeTrackHeaderAnimation();

        // 订阅 DataContext 变化，绑定到 ViewModel
        this.WhenAnyValue(x => x.DataContext)
            .OfType<EditorViewModel>()
            .Subscribe(BindViewModel);
    }

    private void BindViewModel(EditorViewModel vm)
    {
        _viewModelSubscription?.Dispose();
        CompositeDisposable disp = new();
        _viewModelSubscription = disp;

        // 立即设置初始列宽，不动画
        SetColWidth(vm.IsTrackHeaderExpanded
            ? ViewConstants.TrackHeaderWidthExpanded
            : ViewConstants.TrackHeaderWidthCollapsed);

        vm.WhenAnyValue(x => x.IsTrackHeaderExpanded)
            .Skip(1)
            .Subscribe(expanded => SetColWidth(expanded
                ? ViewConstants.TrackHeaderWidthExpanded
                : ViewConstants.TrackHeaderWidthCollapsed))
            .DisposeWith(disp);

        // 订阅放大镜打开事件
        Observable.FromEvent<Action<Point>, Point>(
                h => vm.PianoRollViewModel.RequestMagnifierOpen += h,
                h => vm.PianoRollViewModel.RequestMagnifierOpen -= h)
            .Subscribe(OnMagnifierOpen)
            .DisposeWith(disp);
        // 订阅放大镜更新事件
        Observable.FromEvent<Action<Point>, Point>(
                h => vm.PianoRollViewModel.RequestMagnifierUpdate += h,
                h => vm.PianoRollViewModel.RequestMagnifierUpdate -= h)
            .Subscribe(OnMagnifierUpdate)
            .DisposeWith(disp);
        // 订阅放大镜关闭事件
        Observable.FromEvent(
                h => vm.PianoRollViewModel.RequestMagnifierClose += h,
                h => vm.PianoRollViewModel.RequestMagnifierClose -= h)
            .Subscribe(_ => OnMagnifierClose())
            .DisposeWith(disp);
    }

    private void OnMagnifierOpen(Point point)
    {
        if (!PitchMagnifier.IsVisible)
        {
            // 使用 PART_PianoRollGrid 作为 source，以包含完整的钢琴卷帘区域：
            // 包括标尺行（Row 0, Col 1）、背景网格、琴键等。
            // 手势点虽然来自 NotesCanvas，但通过 MapPointFromNotesCanvasToPianoRoll 正确映射。
            PitchMagnifier.Source = PART_PianoRollGrid;
            PitchMagnifier.IsVisible = true;
        }

        OnMagnifierUpdate(point);
    }

    private void OnMagnifierUpdate(Point point)
    {
        if (!PitchMagnifier.IsVisible) return;

        Point targetPoint = MapPointFromNotesCanvasToPianoRoll(point);
        PitchMagnifier.UpdateView(targetPoint);
    }

    /// <summary>
    /// 将来自NotesCanvas的坐标映射到钢琴卷帘
    /// </summary>
    /// <param name="pointInNotesCanvas">相对NotesCanvas的坐标</param>
    /// <returns>相对钢琴卷帘的坐标</returns>
    private static Point MapPointFromNotesCanvasToPianoRoll(Point pointInNotesCanvas)
    {
        return new Point(
            pointInNotesCanvas.X + ViewConstants.PianoKeysWidth,
            pointInNotesCanvas.Y + ViewConstants.PianoRollTickRulerHeight);
    }

    private void OnMagnifierClose()
    {
        PitchMagnifier.IsVisible = false;
        PitchMagnifier.Source = null; // 停止渲染引用
    }

    private void InitializeResponsiveLayout()
    {
        if (_responsiveLayoutInitialized)
        {
            return;
        }

        _responsiveLayoutInitialized = true;

        if (_landscapeTrackExtent <= 0)
        {
            double width = Math.Max(Bounds.Width, EditorGrid.Bounds.Width);
            _landscapeTrackExtent = width * ViewConstants.EditorSplitDefaultTrackRatioLandscape;
        }

        _isLandscape = Bounds.Width > Bounds.Height;
        ApplyOrientationLayout();
    }

    private void ApplyOrientationLayout()
    {
        if (EditorGrid.RowDefinitions.Count < 2 || EditorGrid.ColumnDefinitions.Count < 2)
        {
            return;
        }

        if (_isLandscape)
        {
            ApplyLandscapeLayout();
        }
        else
        {
            ApplyPortraitLayout();
        }

        UpdateSplitHandleOrientation();
        UpdateSplitHandleCursor();
        UpdateSplitOverlayVisual();
    }

    private void UpdateSplitHandleOrientation()
    {
        SplitDragHandle.RenderTransform = _isLandscape
            ? new RotateTransform(90)
            : null;
    }

    private void ApplyPortraitLayout()
    {
        EditorGrid.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);
        EditorGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        EditorGrid.ColumnDefinitions[1].Width = new GridLength(0, GridUnitType.Pixel);

        Grid.SetRow(TrackAreaGrid, 0);
        Grid.SetColumn(TrackAreaGrid, 0);
        Grid.SetRowSpan(TrackAreaGrid, 1);
        Grid.SetColumnSpan(TrackAreaGrid, 2);

        Grid.SetRow(PianoRollAreaGrid, 1);
        Grid.SetColumn(PianoRollAreaGrid, 0);
        Grid.SetRowSpan(PianoRollAreaGrid, 1);
        Grid.SetColumnSpan(PianoRollAreaGrid, 2);

        if (_portraitTrackExtent <= 0)
        {
            _portraitTrackExtent = ViewConstants.EditorSplitDefaultTrackExtentPortrait;
        }

        SetCurrentTrackExtent(_portraitTrackExtent);
    }

    private void ApplyLandscapeLayout()
    {
        EditorGrid.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
        EditorGrid.RowDefinitions[1].Height = new GridLength(0, GridUnitType.Pixel);
        EditorGrid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);

        Grid.SetRow(TrackAreaGrid, 0);
        Grid.SetColumn(TrackAreaGrid, 0);
        Grid.SetRowSpan(TrackAreaGrid, 2);
        Grid.SetColumnSpan(TrackAreaGrid, 1);

        Grid.SetRow(PianoRollAreaGrid, 0);
        Grid.SetColumn(PianoRollAreaGrid, 1);
        Grid.SetRowSpan(PianoRollAreaGrid, 2);
        Grid.SetColumnSpan(PianoRollAreaGrid, 1);

        if (_landscapeTrackExtent <= 0)
        {
            _landscapeTrackExtent = GetPrimaryTotalExtent() * ViewConstants.EditorSplitDefaultTrackRatioLandscape;
        }

        SetCurrentTrackExtent(_landscapeTrackExtent);
    }

    private double ClampTrackExtent(double extent)
    {
        double total = GetPrimaryTotalExtent();
        if (total <= 0)
        {
            return extent;
        }

        const double minTrack = 0;
        const double minPiano = 0;

        double maxTrack = total - minPiano;
        if (maxTrack < minTrack)
        {
            return Math.Clamp(total * 0.5, 0, total);
        }

        return Math.Clamp(extent, minTrack, maxTrack);
    }

    private double GetPrimaryTotalExtent()
    {
        return _isLandscape ? EditorGrid.Bounds.Width : EditorGrid.Bounds.Height;
    }

    private double GetCurrentTrackExtent()
    {
        return _isLandscape
            ? EditorGrid.ColumnDefinitions[0].Width.Value
            : EditorGrid.RowDefinitions[0].Height.Value;
    }

    private void SetCurrentTrackExtent(double extent)
    {
        double clamped = ClampTrackExtent(extent);
        if (_isLandscape)
        {
            _landscapeTrackExtent = clamped;
            EditorGrid.ColumnDefinitions[0].Width = new GridLength(clamped, GridUnitType.Pixel);
        }
        else
        {
            _portraitTrackExtent = clamped;
            EditorGrid.RowDefinitions[0].Height = new GridLength(clamped, GridUnitType.Pixel);
        }
    }

    private static double ClampHandleAxisRatio(double ratio)
    {
        return Math.Clamp(ratio, 0, 1);
    }

    private void UpdateSplitOverlayVisual()
    {
        double totalWidth = EditorGrid.Bounds.Width;
        double totalHeight = EditorGrid.Bounds.Height;
        if (totalWidth <= 0 || totalHeight <= 0)
        {
            return;
        }

        double lineThickness = ViewConstants.EditorSplitLineThickness;
        double handleWidth = ViewConstants.EditorSplitHandleWidth;
        double handleHeight = ViewConstants.EditorSplitHandleHeight;
        double edgeInset = ViewConstants.EditorSplitHandleEdgeInset;

        if (_isLandscape)
        {
            double splitX = GetCurrentTrackExtent();
            SplitGuideLine.Width = lineThickness;
            SplitGuideLine.Height = totalHeight;
            Canvas.SetLeft(SplitGuideLine, splitX);
            Canvas.SetTop(SplitGuideLine, 0);

            _landscapeHandleAxisRatio = ClampHandleAxisRatio(_landscapeHandleAxisRatio);
            // 旋转 90 度后，手柄视觉高度等于原宽度，需要补偿布局高度与视觉高度差。
            double rotatedVisualHeight = handleWidth;
            double axisRange = Math.Max(0, totalHeight - rotatedVisualHeight - edgeInset * 2);
            double visualTop = edgeInset + axisRange * _landscapeHandleAxisRatio;
            double top = visualTop + (rotatedVisualHeight - handleHeight) / 2.0;
            Canvas.SetLeft(SplitDragHandle, splitX + handleHeight / 2.0);
            Canvas.SetTop(SplitDragHandle, top);
        }
        else
        {
            double splitY = GetCurrentTrackExtent();
            SplitGuideLine.Width = totalWidth;
            SplitGuideLine.Height = lineThickness;
            Canvas.SetLeft(SplitGuideLine, 0);
            Canvas.SetTop(SplitGuideLine, splitY - lineThickness / 2.0);

            _portraitHandleAxisRatio = ClampHandleAxisRatio(_portraitHandleAxisRatio);
            double axisRange = Math.Max(0, totalWidth - handleWidth - edgeInset * 2);
            double left = edgeInset + axisRange * _portraitHandleAxisRatio;
            Canvas.SetLeft(SplitDragHandle, left);
            Canvas.SetTop(SplitDragHandle, splitY - handleHeight / 2.0);
        }
    }

    private void UpdateSplitHandleCursor()
    {
        SplitDragHandle.Cursor = _isLandscape
            ? ViewConstants.cursorSizeWE
            : ViewConstants.cursorSizeNS;
    }

    private void ResetSplitDragState()
    {
        _splitPointerDown = false;
        _splitDragging = false;
    }

    private void InitializeTrackHeaderAnimation()
    {
        // ColumnDefinition不支持Transitions，使用Timer + Easing实现流畅动画
    }

    private void SetColWidth(double targetWidth)
    {
        var currentWidth = TrackAreaGrid.ColumnDefinitions[0].Width.Value;

        if (Math.Abs(currentWidth - targetWidth) < 0.1)
        {
            TrackAreaGrid.ColumnDefinitions[0].Width = new GridLength(targetWidth, GridUnitType.Pixel);
            return;
        }

        // 启动动画
        _widthAnimStart = currentWidth;
        _widthAnimTarget = targetWidth;
        _widthAnimStartTime = DateTime.Now;

        _widthAnimTimer?.Stop();
        _widthAnimTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) }; // ~60fps
        _widthAnimTimer.Tick += OnWidthAnimTick;
        _widthAnimTimer.Start();
    }

    private void OnWidthAnimTick(object? sender, EventArgs e)
    {
        const double durationMs = 200;
        var elapsed = (DateTime.Now - _widthAnimStartTime).TotalMilliseconds;
        var progress = Math.Min(elapsed / durationMs, 1.0);

        // CubicEaseInOut函数
        var eased = progress < 0.5
            ? 4 * progress * progress * progress
            : 1 - Math.Pow(-2 * progress + 2, 3) / 2;

        var currentWidth = _widthAnimStart + (_widthAnimTarget - _widthAnimStart) * eased;
        TrackAreaGrid.ColumnDefinitions[0].Width = new GridLength(currentWidth, GridUnitType.Pixel);

        if (progress >= 1.0)
        {
            _widthAnimTimer?.Stop();
            _widthAnimTimer = null;
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);

        if (e.NewSize.Width <= 0 || e.NewSize.Height <= 0)
        {
            return;
        }

        bool newIsLandscape = e.NewSize.Width > e.NewSize.Height;
        if (!_responsiveLayoutInitialized)
        {
            _isLandscape = newIsLandscape;
            InitializeResponsiveLayout();
            return;
        }

        if (newIsLandscape != _isLandscape)
        {
            _isLandscape = newIsLandscape;
            ApplyOrientationLayout();
            return;
        }

        SetCurrentTrackExtent(GetCurrentTrackExtent());
        UpdateSplitOverlayVisual();
    }

    #region 分割手势

    /// <summary>
    /// 指针按下
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void OnSplitHandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(SplitDragHandle).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _splitPointerDown = true;
        _splitDragging = false;
        _splitPressAbsPoint = e.GetPosition(this);
        _splitPressPrimaryExtent = GetCurrentTrackExtent();
        _splitPressHandleAxisRatio = _isLandscape
            ? _landscapeHandleAxisRatio
            : _portraitHandleAxisRatio;

        e.Pointer.Capture(SplitDragHandle);
        e.Handled = true;
    }

    /// <summary>
    /// 指针移动
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void OnSplitHandlePointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_splitPointerDown)
        {
            return;
        }

        // 防御：若左键已在弹出层/外部释放，此处清理孤立状态
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed)
        {
            e.Pointer.Capture(null);
            ResetSplitDragState();
            return;
        }

        Point currentPoint = e.GetPosition(this);
        double primaryDelta = _isLandscape
            ? currentPoint.X - _splitPressAbsPoint.X
            : currentPoint.Y - _splitPressAbsPoint.Y;
        double axisDelta = _isLandscape
            ? currentPoint.Y - _splitPressAbsPoint.Y
            : currentPoint.X - _splitPressAbsPoint.X;

        if (!_splitDragging)
        {
            if (Math.Max(Math.Abs(primaryDelta), Math.Abs(axisDelta)) <= ViewConstants.SplitDragThreshold)
            {
                return;
            }

            _splitDragging = true;
        }

        SetCurrentTrackExtent(_splitPressPrimaryExtent + primaryDelta);

        double axisTotal = _isLandscape ? EditorGrid.Bounds.Height : EditorGrid.Bounds.Width;
        double handleSize = _isLandscape
            ? ViewConstants.EditorSplitHandleHeight
            : ViewConstants.EditorSplitHandleWidth;
        double axisRange = Math.Max(1, axisTotal - handleSize - ViewConstants.EditorSplitHandleEdgeInset * 2);
        double axisRatio = ClampHandleAxisRatio(_splitPressHandleAxisRatio + axisDelta / axisRange);
        if (_isLandscape)
        {
            _landscapeHandleAxisRatio = axisRatio;
        }
        else
        {
            _portraitHandleAxisRatio = axisRatio;
        }

        UpdateSplitOverlayVisual();
        e.Handled = true;
    }

    /// <summary>
    /// 指针释放
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void OnSplitHandlePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_splitPointerDown)
        {
            return;
        }

        ResetSplitDragState();
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnSplitHandlePointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        ResetSplitDragState();
    }

    #endregion
}