using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using IconPacks.Avalonia.PhosphorIcons;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Themes.OpenUtauMobile.Runtime;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Controls;

/// <summary>
/// 高级音素画布，完全对齐 OpenUtau 桌面端：包络梯形、先行发音（左下角）与重叠（左上角）两处交互控制点。
/// 顶部留有完整安全边距避开上方分割手柄，双击直接唤起音素别名编辑弹窗。
/// </summary>
public class PhonemeAdvancedCanvas : Control, ICmdSubscriber
{
    public static readonly StyledProperty<UVoicePart?> PartProperty =
        AvaloniaProperty.Register<PhonemeAdvancedCanvas, UVoicePart?>(nameof(Part));

    public static readonly StyledProperty<double> TickWidthProperty =
        AvaloniaProperty.Register<PhonemeAdvancedCanvas, double>(nameof(TickWidth), 0.1);

    public static readonly StyledProperty<double> TickOffsetProperty =
        AvaloniaProperty.Register<PhonemeAdvancedCanvas, double>(nameof(TickOffset));

    public UVoicePart? Part
    {
        get => GetValue(PartProperty);
        set => SetValue(PartProperty, value);
    }

    public double TickWidth
    {
        get => GetValue(TickWidthProperty);
        set => SetValue(TickWidthProperty, value);
    }

    public double TickOffset
    {
        get => GetValue(TickOffsetProperty);
        set => SetValue(TickOffsetProperty, value);
    }

    private PianoRollViewModel? _viewModel;
    private PianoRollViewModel? ViewModel => _viewModel ?? (DataContext as PianoRollViewModel);

    private enum AdvancedHandleType
    {
        None,
        TimingLine,
        Preutter,
        Overlap
    }

    private AdvancedHandleType _activeHandleType = AdvancedHandleType.None;
    private UPhoneme? _activePhoneme;
    private UPhoneme? _animatingTimingPhoneme;
    private bool _isResetTargetActive;
    private double _dragStartPointerX;
    private float _initialDelta;

    // 手柄展开动效状态
    private DispatcherTimer? _animTimer;
    private double _animProgress;
    private double _animStartProgress;
    private double _animTargetProgress;
    private DateTime _animStartTime;
    private const double AnimDurationMs = 130.0;

    // 重置目标动效状态
    private DispatcherTimer? _resetTargetAnimTimer;
    private double _resetTargetAnimProgress;
    private double _resetTargetAnimStartProgress;
    private double _resetTargetAnimTargetProgress;
    private DateTime _resetTargetAnimStartTime;

    // 双击检测
    private DateTime _lastClickTime = DateTime.MinValue;
    private Point _lastClickPoint;
    private const double DoubleClickMaxTimeMs = 350;
    private const double DoubleClickMaxDistance = 24.0;
    private const double HandleHitRadius = 24.0;

    private const double TopMargin = 16.0;      // 顶部留白避开分割条悬浮手柄
    private const double LabelHeight = 16.0;    // 标签高度
    private const double BottomMargin = 8.0;    // 底部留白

    private readonly Geometry _handleGeometry = new EllipseGeometry(new Rect(-3.5, -3.5, 7.0, 7.0));
    private readonly Geometry _resetIconGeometry;
    private readonly SolidColorBrush _resetTargetBackgroundBrush = new(Colors.Transparent);
    private readonly SolidColorBrush _resetTargetIconBrush = new(Colors.Transparent);
    private Color _resetTargetIdleBackgroundColor;
    private Color _resetTargetActiveBackgroundColor;
    private Color _resetTargetIdleIconColor;
    private Color _resetTargetActiveIconColor;

    public PhonemeAdvancedCanvas()
    {
        ClipToBounds = true;
        PackIconPhosphorIcons resetIcon = new()
        {
            Kind = PackIconPhosphorIconsKind.ArrowCounterClockwise
        };
        _resetIconGeometry = resetIcon.Data
            ?? throw new InvalidOperationException("Phosphor Trash icon geometry was not initialized.");
    }

    private void StartDragAnimation(double target)
    {
        _animStartProgress = _animProgress;
        _animTargetProgress = target;
        _animStartTime = DateTime.UtcNow;

        if (_animTimer == null)
        {
            _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _animTimer.Tick += OnAnimTimerTick;
        }
        _animTimer.Start();
    }

    private void OnAnimTimerTick(object? sender, EventArgs e)
    {
        double elapsed = (DateTime.UtcNow - _animStartTime).TotalMilliseconds;
        double t = Math.Clamp(elapsed / AnimDurationMs, 0.0, 1.0);
        // CubicEaseOut
        double eased = 1.0 - Math.Pow(1.0 - t, 3);
        _animProgress = _animStartProgress + (_animTargetProgress - _animStartProgress) * eased;

        InvalidateVisual();

        if (t >= 1.0)
        {
            _animProgress = _animTargetProgress;
            _animTimer?.Stop();
            if (_animProgress <= 0.0 && _activeHandleType != AdvancedHandleType.TimingLine)
            {
                _animatingTimingPhoneme = null;
            }
        }
    }

    private void StartResetTargetAnimation(double target)
    {
        _resetTargetAnimStartProgress = _resetTargetAnimProgress;
        _resetTargetAnimTargetProgress = target;
        _resetTargetAnimStartTime = DateTime.UtcNow;

        if (_resetTargetAnimTimer == null)
        {
            _resetTargetAnimTimer = new DispatcherTimer
            {
                Interval = ThemeBaseMotionTokens.FrameInterval
            };
            _resetTargetAnimTimer.Tick += OnResetTargetAnimTimerTick;
        }
        _resetTargetAnimTimer.Start();
    }

    private void OnResetTargetAnimTimerTick(object? sender, EventArgs e)
    {
        double elapsed = (DateTime.UtcNow - _resetTargetAnimStartTime).TotalMilliseconds;
        double duration = ThemeBaseMotionTokens.DurationShort2.TotalMilliseconds;
        double t = Math.Clamp(elapsed / duration, 0.0, 1.0);
        double eased = 1.0 - Math.Pow(1.0 - t, 3);
        _resetTargetAnimProgress = _resetTargetAnimStartProgress
            + (_resetTargetAnimTargetProgress - _resetTargetAnimStartProgress) * eased;
        InvalidateVisual();

        if (t >= 1.0)
        {
            _resetTargetAnimProgress = _resetTargetAnimTargetProgress;
            _resetTargetAnimTimer?.Stop();
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel != null)
        {
            _viewModel.RequestInvalidateVisual -= InvalidateVisual;
        }

        _viewModel = DataContext as PianoRollViewModel;

        if (_viewModel != null)
        {
            _viewModel.RequestInvalidateVisual += InvalidateVisual;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        DocManager.Inst.AddSubscriber(this);
        if (DataContext is PianoRollViewModel vm)
        {
            if (_viewModel != null)
            {
                _viewModel.RequestInvalidateVisual -= InvalidateVisual;
            }
            _viewModel = vm;
            _viewModel.RequestInvalidateVisual += InvalidateVisual;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        DocManager.Inst.RemoveSubscriber(this);
        _animTimer?.Stop();
        _resetTargetAnimTimer?.Stop();
        if (_viewModel != null)
        {
            _viewModel.RequestInvalidateVisual -= InvalidateVisual;
            _viewModel = null;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PartProperty ||
            change.Property == TickWidthProperty ||
            change.Property == TickOffsetProperty)
        {
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Part == null || Bounds.Width <= 0 || Bounds.Height <= 0 || DocManager.Inst.Project == null)
        {
            return;
        }

        IBrush bgBrush = ThemeResources.GetBrush("Sem.Color.SurfaceContainerLow");
        using (context.PushOpacity(0.5))
        {
            context.DrawRectangle(bgBrush, null, new Rect(0, 0, Bounds.Width, Bounds.Height));
        }
        double partPos = Part.position;
        double viewLeftTick = TickOffset - 480;
        double viewRightTick = TickOffset + Bounds.Width / TickWidth + 480;

        IBrush defaultBrush = ThemeResources.GetBrush("Sem.Color.SecondaryContainer");
        IBrush selectedBrush = ThemeResources.GetBrush("Sem.Color.PrimaryContainer");
        IPen defaultPen = ThemeResources.GetPen("Sem.Color.Secondary", 1.5);
        IPen selectedPen = ThemeResources.GetPen("Sem.Color.Primary", 1.5);
        IPen timingPen = ThemeResources.GetPen("Sem.Color.Primary", 1.5);
        IPen timingThickPen = ThemeResources.GetPen("Sem.Color.Primary", 3.0);
        IBrush textBgBrush = ThemeResources.GetBrush("Sem.Color.SurfaceContainerHighest");
        IPen textBorderPen = ThemeResources.GetPen("Sem.Color.OutlineVariant");
        IBrush textBrush = ThemeResources.GetBrush("Sem.Color.OnSurface");

        double totalHeight = Bounds.Height;
        double labelY = TopMargin + 2.0;
        double envelopeTopY = TopMargin + LabelHeight + 4.0;
        double envelopeHeight = Math.Max(20.0, totalHeight - envelopeTopY - BottomMargin);

        foreach (UPhoneme phoneme in Part.phonemes)
        {
            if (phoneme.Parent == null || phoneme.Parent.OverlapError)
            {
                continue;
            }

            double phonemeAbsStart = partPos + phoneme.position;
            double phonemeAbsEnd = partPos + phoneme.End;

            if (phonemeAbsEnd < viewLeftTick || phonemeAbsStart > viewRightTick)
            {
                continue;
            }

            bool isSelected = ViewModel?.SelectedNotes.Contains(phoneme.Parent) ?? false;
            IPen pen = isSelected ? selectedPen : defaultPen;
            IBrush fill = isSelected ? selectedBrush : defaultBrush;

            double posX = (phonemeAbsStart - TickOffset) * TickWidth;

            // 1. 绘制包络梯形（5点）
            if (!phoneme.Error && phoneme.envelope.data.Count >= 5)
            {
                double posMs = phoneme.PositionMs;
                TimeAxis timeAxis = DocManager.Inst.Project.timeAxis;

                double x0 = (timeAxis.MsPosToTickPos(posMs + phoneme.envelope.data[0].X) - TickOffset) * TickWidth;
                double y0 = envelopeTopY + (1.0 - phoneme.envelope.data[0].Y / 100.0) * envelopeHeight;

                double x1 = (timeAxis.MsPosToTickPos(posMs + phoneme.envelope.data[1].X) - TickOffset) * TickWidth;
                double y1 = envelopeTopY + (1.0 - phoneme.envelope.data[1].Y / 100.0) * envelopeHeight;

                double x2 = (timeAxis.MsPosToTickPos(posMs + phoneme.envelope.data[2].X) - TickOffset) * TickWidth;
                double y2 = envelopeTopY + (1.0 - phoneme.envelope.data[2].Y / 100.0) * envelopeHeight;

                double x3 = (timeAxis.MsPosToTickPos(posMs + phoneme.envelope.data[3].X) - TickOffset) * TickWidth;
                double y3 = envelopeTopY + (1.0 - phoneme.envelope.data[3].Y / 100.0) * envelopeHeight;

                double x4 = (timeAxis.MsPosToTickPos(posMs + phoneme.envelope.data[4].X) - TickOffset) * TickWidth;
                double y4 = envelopeTopY + (1.0 - phoneme.envelope.data[4].Y / 100.0) * envelopeHeight;

                Point[] pts =
                [
                    new(x0, y0),
                    new(x1, y1),
                    new(x2, y2),
                    new(x3, y3),
                    new(x4, y4)
                ];

                PolylineGeometry polyline = new PolylineGeometry(pts, true);
                using (context.PushOpacity(0.40))
                {
                    context.DrawGeometry(fill, pen, polyline);
                }

                // 2. 绘制控制手柄（对齐 OpenUtau 桌面端：左下角先行发音点 + 左上角重叠点）
                // 控制点 0：先行发音（Preutter - 左下角）
                IBrush p0Brush = phoneme.preutterDelta.HasValue ? (pen.Brush ?? selectedBrush) : textBgBrush;
                using (context.PushTransform(Matrix.CreateTranslation(x0, y0)))
                {
                    context.DrawGeometry(p0Brush, pen, _handleGeometry);
                }

                // 控制点 1：重叠（Overlap - 左上角起振过渡点）
                IBrush p1Brush = phoneme.overlapDelta.HasValue ? (pen.Brush ?? selectedBrush) : textBgBrush;
                using (context.PushTransform(Matrix.CreateTranslation(x1, y1)))
                {
                    context.DrawGeometry(p1Brush, pen, _handleGeometry);
                }
            }

            // 3. 绘制垂直位置基准线
            bool isModifiedTiming = phoneme.rawPosition != phoneme.position;
            bool isAnimTarget = _animatingTimingPhoneme == phoneme && _animProgress > 0.001;
            double progress = isAnimTarget ? _animProgress : 0.0;

            double expansion = 6.0 * progress; // 拖拽时平滑上下延伸 6dp
            double thicknessBonus = 1.2 * progress;
            double lineThickness = (isModifiedTiming ? 3.0 : 1.5) + thicknessBonus;
            IPen linePen = (isModifiedTiming || progress > 0.001)
                ? ThemeResources.GetPen("Sem.Color.Primary", lineThickness)
                : (isModifiedTiming ? timingThickPen : timingPen);

            double lineTop = TopMargin + 2.0 - expansion;
            double lineBottom = envelopeTopY + envelopeHeight + 2.0 + expansion;
            context.DrawLine(linePen, new Point(posX, lineTop), new Point(posX, lineBottom));

            // 4. 绘制音素标签药丸框（置于顶部留白下方）
            string labelText = !string.IsNullOrEmpty(phoneme.phonemeMapped) ? phoneme.phonemeMapped : phoneme.phoneme;
            if (!string.IsNullOrEmpty(labelText))
            {
                bool isCustom = phoneme.phoneme != phoneme.rawPhoneme;
                TextLayout textLayout = TextLayoutCache.Get(labelText, textBrush, 11, isCustom);
                double pillWidth = textLayout.Width + 8.0;
                double pillHeight = textLayout.Height + 2.0;
                double pillX = posX + 2.0;

                Rect pillRect = new Rect(pillX, labelY, pillWidth, pillHeight);
                context.DrawRectangle(textBgBrush, textBorderPen, pillRect, 3, 3);
                using (context.PushTransform(Matrix.CreateTranslation(pillX + 4.0, labelY + 1.0)))
                {
                    textLayout.Draw(context, new Point(0, 0));
                }
            }
        }

        if (_activeHandleType != AdvancedHandleType.None)
        {
            RenderResetTarget(context);
        }
    }

    private void RenderResetTarget(DrawingContext context)
    {
        double progress = _resetTargetAnimProgress;
        double targetSize = Interpolate(
            ThemeSemPhonemePanelTokens.ResetTargetSize,
            ThemeSemPhonemePanelTokens.ResetTargetActiveSize,
            progress);
        double iconSize = Interpolate(
            ThemeSemPhonemePanelTokens.ResetTargetIconSize,
            ThemeSemPhonemePanelTokens.ResetTargetIconActiveSize,
            progress);
        Point center = GetResetTargetCenter();

        _resetTargetBackgroundBrush.Color = InterpolateColor(
            _resetTargetIdleBackgroundColor,
            _resetTargetActiveBackgroundColor,
            progress);
        _resetTargetIconBrush.Color = InterpolateColor(
            _resetTargetIdleIconColor,
            _resetTargetActiveIconColor,
            progress);
        context.DrawEllipse(
            _resetTargetBackgroundBrush,
            null,
            center,
            targetSize * 0.5,
            targetSize * 0.5);

        Rect iconBounds = _resetIconGeometry.Bounds;
        double iconScale = iconSize / Math.Max(iconBounds.Width, iconBounds.Height);
        Matrix iconTransform = Matrix.CreateTranslation(-iconBounds.Center.X, -iconBounds.Center.Y)
            * Matrix.CreateScale(iconScale, iconScale)
            * Matrix.CreateTranslation(center.X, center.Y);
        using (context.PushTransform(iconTransform))
        {
            context.DrawGeometry(_resetTargetIconBrush, null, _resetIconGeometry);
        }
    }

    private static double Interpolate(double from, double to, double progress)
    {
        return from + (to - from) * progress;
    }

    private static Color InterpolateColor(Color from, Color to, double progress)
    {
        byte alpha = (byte)Math.Round(Interpolate(from.A, to.A, progress));
        byte red = (byte)Math.Round(Interpolate(from.R, to.R, progress));
        byte green = (byte)Math.Round(Interpolate(from.G, to.G, progress));
        byte blue = (byte)Math.Round(Interpolate(from.B, to.B, progress));
        return Color.FromArgb(alpha, red, green, blue);
    }

    private void CacheResetTargetColors()
    {
        _resetTargetIdleBackgroundColor = ThemeResources.GetColor("Sem.Color.ErrorContainer");
        _resetTargetActiveBackgroundColor = ThemeResources.GetColor("Sem.Color.Error");
        _resetTargetIdleIconColor = ThemeResources.GetColor("Sem.Color.OnErrorContainer");
        _resetTargetActiveIconColor = ThemeResources.GetColor("Sem.Color.OnError");
    }

    private Point GetResetTargetCenter()
    {
        double halfHitSize = ThemeSemPhonemePanelTokens.ResetTargetHitSize * 0.5;
        double targetInset = ThemeSemPhonemePanelTokens.ResetTargetOuterInset;
        return new Point(
            targetInset + halfHitSize,
            targetInset + halfHitSize);
    }

    private bool IsInsideResetTarget(Point point)
    {
        double hitSize = ThemeSemPhonemePanelTokens.ResetTargetHitSize;
        Point center = GetResetTargetCenter();
        Rect hitRect = new(
            center.X - hitSize * 0.5,
            center.Y - hitSize * 0.5,
            hitSize,
            hitSize);
        return hitRect.Contains(point);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Part == null || DocManager.Inst.Project == null)
        {
            return;
        }

        Point pos = e.GetPosition(this);
        double partPos = Part.position;
        double currentTick = pos.X / TickWidth + TickOffset - partPos;

        // 1. 测试是否击中控制手柄
        (AdvancedHandleType hitType, UPhoneme? hitPhoneme) = HitTestHandle(pos);
        if (hitType != AdvancedHandleType.None && hitPhoneme != null)
        {
            CacheResetTargetColors();
            _activeHandleType = hitType;
            _activePhoneme = hitPhoneme;
            _isResetTargetActive = false;
            _resetTargetAnimProgress = 0.0;
            _resetTargetAnimTimer?.Stop();
            if (hitType == AdvancedHandleType.TimingLine)
            {
                _animatingTimingPhoneme = hitPhoneme;
                StartDragAnimation(1.0);
            }
            _dragStartPointerX = pos.X;
            UPhonemeOverride overrideData = hitPhoneme.Parent.GetPhonemeOverride(hitPhoneme.index);

            _initialDelta = hitType switch
            {
                AdvancedHandleType.TimingLine => overrideData.offset ?? 0,
                AdvancedHandleType.Preutter => overrideData.preutterDelta ?? 0,
                AdvancedHandleType.Overlap => overrideData.overlapDelta ?? 0,
                _ => 0
            };

            e.Pointer.Capture(this);
            e.Handled = true;
            DocManager.Inst.StartUndoGroup();

            string phonemeName = !string.IsNullOrEmpty(hitPhoneme.phonemeMapped) ? hitPhoneme.phonemeMapped : hitPhoneme.phoneme;
            if (ViewModel != null)
            {
                ViewModel.EditingTip = hitType switch
                {
                    AdvancedHandleType.TimingLine => $"[{phonemeName}] Offset: {(int)_initialDelta:+0;-0;0} tick",
                    AdvancedHandleType.Preutter => $"[{phonemeName}] Preutter: {_initialDelta:+0.0;-0.0;0.0} ms",
                    AdvancedHandleType.Overlap => $"[{phonemeName}] Overlap: {_initialDelta:+0.0;-0.0;0.0} ms",
                    _ => string.Empty
                };
            }
            return;
        }

        // 2. 双击检测：打开音素别名编辑弹窗
        DateTime now = DateTime.UtcNow;
        double elapsedMs = (now - _lastClickTime).TotalMilliseconds;
        double dist = Math.Abs(pos.X - _lastClickPoint.X) + Math.Abs(pos.Y - _lastClickPoint.Y);

        if (elapsedMs < DoubleClickMaxTimeMs && dist < DoubleClickMaxDistance)
        {
            UPhoneme? clickedPhoneme = FindPhonemeAtTick(currentTick);
            if (clickedPhoneme != null && clickedPhoneme.Parent != null)
            {
                ViewModel?.RaiseRequestEditPhoneme(Part, clickedPhoneme.Parent, clickedPhoneme.index);
                e.Handled = true;
                _lastClickTime = DateTime.MinValue;
                return;
            }
        }

        _lastClickTime = now;
        _lastClickPoint = pos;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_activeHandleType == AdvancedHandleType.None || _activePhoneme == null || Part == null || DocManager.Inst.Project == null)
        {
            return;
        }

        Point pos = e.GetPosition(this); // 手指坐标
        bool isInsideResetTarget = IsInsideResetTarget(pos);
        // 重置意图
        if (_isResetTargetActive != isInsideResetTarget)
        {
            _isResetTargetActive = isInsideResetTarget;
            StartResetTargetAnimation(_isResetTargetActive ? 1.0 : 0.0);
        }

        if (_isResetTargetActive)
        {
            if (ViewModel != null)
            {
                ViewModel.EditingTip = L.S("PhonemePanel.Reset.ReleaseHint");
            }
            e.Handled = true;
            return;
        }

        double deltaPx = pos.X - _dragStartPointerX; // X偏移量（屏幕坐标）
        double deltaTicks = deltaPx / TickWidth; // Tick偏移量
        double deltaMs = DocManager.Inst.Project.timeAxis.TickPosToMsPos(Part.position + _activePhoneme.position + (int)deltaTicks)
                       - DocManager.Inst.Project.timeAxis.TickPosToMsPos(Part.position + _activePhoneme.position); //ms偏移量

        string phonemeName = !string.IsNullOrEmpty(_activePhoneme.phonemeMapped) ? _activePhoneme.phonemeMapped : _activePhoneme.phoneme;
        switch (_activeHandleType)
        {
            case AdvancedHandleType.TimingLine:
                int newOffset = (int)(_initialDelta + deltaTicks);
                DocManager.Inst.ExecuteCmd(new PhonemeOffsetCommand(Part, _activePhoneme.Parent, _activePhoneme.index, newOffset));
                double offsetMs = DocManager.Inst.Project.timeAxis.TickPosToMsPos(Part.position + _activePhoneme.rawPosition + newOffset)
                                - DocManager.Inst.Project.timeAxis.TickPosToMsPos(Part.position + _activePhoneme.rawPosition);
                if (ViewModel != null)
                {
                    ViewModel.EditingTip = $"[{phonemeName}] Offset: {newOffset:+0;-0;0} tick ({offsetMs:+0.0;-0.0;0.0} ms)";
                }
                break;
            case AdvancedHandleType.Preutter:
                float newPreutter = (float)(_initialDelta - deltaMs);
                DocManager.Inst.ExecuteCmd(new PhonemePreutterCommand(Part, _activePhoneme.Parent, _activePhoneme.index, _activePhoneme, newPreutter));
                if (ViewModel != null)
                {
                    ViewModel.EditingTip = $"[{phonemeName}] Preutter: {newPreutter:+0.0;-0.0;0.0} ms";
                }
                break;
            case AdvancedHandleType.Overlap:
                float newOverlap = (float)(_initialDelta + deltaMs);
                DocManager.Inst.ExecuteCmd(new PhonemeOverlapCommand(Part, _activePhoneme.Parent, _activePhoneme.index, _activePhoneme, newOverlap));
                if (ViewModel != null)
                {
                    ViewModel.EditingTip = $"[{phonemeName}] Overlap: {newOverlap:+0.0;-0.0;0.0} ms";
                }
                break;
        }

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_activeHandleType != AdvancedHandleType.None)
        {
            _isResetTargetActive = IsInsideResetTarget(e.GetPosition(this));
            if (_isResetTargetActive)
            {
                ResetActiveParameter();
            }
            if (_activeHandleType == AdvancedHandleType.TimingLine)
            {
                StartDragAnimation(0.0);
            }
            _activeHandleType = AdvancedHandleType.None;
            _activePhoneme = null;
            _isResetTargetActive = false;
            _resetTargetAnimProgress = 0.0;
            _resetTargetAnimTimer?.Stop();
            DocManager.Inst.EndUndoGroup();
            e.Pointer.Capture(null);
            if (ViewModel != null)
            {
                ViewModel.EditingTip = string.Empty;
            }
            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_activeHandleType != AdvancedHandleType.None)
        {
            if (_activeHandleType == AdvancedHandleType.TimingLine)
            {
                StartDragAnimation(0.0);
            }
            _activeHandleType = AdvancedHandleType.None;
            _activePhoneme = null;
            _isResetTargetActive = false;
            _resetTargetAnimProgress = 0.0;
            _resetTargetAnimTimer?.Stop();
            DocManager.Inst.EndUndoGroup();
            if (ViewModel != null)
            {
                ViewModel.EditingTip = string.Empty;
            }
            InvalidateVisual();
        }
    }

    private void ResetActiveParameter()
    {
        if (Part == null || _activePhoneme?.Parent == null)
        {
            return;
        }

        switch (_activeHandleType)
        {
            case AdvancedHandleType.TimingLine:
                DocManager.Inst.ExecuteCmd(new PhonemeOffsetCommand(
                    Part, _activePhoneme.Parent, _activePhoneme.index, 0));
                break;
            case AdvancedHandleType.Preutter:
                DocManager.Inst.ExecuteCmd(new PhonemePreutterCommand(
                    Part, _activePhoneme.Parent, _activePhoneme.index, _activePhoneme, 0));
                break;
            case AdvancedHandleType.Overlap:
                DocManager.Inst.ExecuteCmd(new PhonemeOverlapCommand(
                    Part, _activePhoneme.Parent, _activePhoneme.index, _activePhoneme, 0));
                break;
        }
    }

    private (AdvancedHandleType, UPhoneme?) HitTestHandle(Point pointerPos)
    {
        if (Part == null || DocManager.Inst.Project == null)
        {
            return (AdvancedHandleType.None, null);
        }

        double totalHeight = Bounds.Height;
        double envelopeTopY = TopMargin + LabelHeight + 4.0;
        double envelopeHeight = Math.Max(20.0, totalHeight - envelopeTopY - BottomMargin);
        TimeAxis timeAxis = DocManager.Inst.Project.timeAxis;

        foreach (UPhoneme phoneme in Part.phonemes)
        {
            if (phoneme.Parent == null || phoneme.Error || phoneme.envelope.data.Count < 5)
            {
                continue;
            }

            double posMs = phoneme.PositionMs;

            // 点 0：Preutter (左下角)
            double x0 = (timeAxis.MsPosToTickPos(posMs + phoneme.envelope.data[0].X) - TickOffset) * TickWidth;
            double y0 = envelopeTopY + (1.0 - phoneme.envelope.data[0].Y / 100.0) * envelopeHeight;
            if (Math.Abs(pointerPos.X - x0) <= HandleHitRadius && Math.Abs(pointerPos.Y - y0) <= HandleHitRadius)
            {
                return (AdvancedHandleType.Preutter, phoneme);
            }

            // 点 1：Overlap (左上角起振点)
            double x1 = (timeAxis.MsPosToTickPos(posMs + phoneme.envelope.data[1].X) - TickOffset) * TickWidth;
            double y1 = envelopeTopY + (1.0 - phoneme.envelope.data[1].Y / 100.0) * envelopeHeight;
            if (Math.Abs(pointerPos.X - x1) <= HandleHitRadius && Math.Abs(pointerPos.Y - y1) <= HandleHitRadius)
            {
                return (AdvancedHandleType.Overlap, phoneme);
            }

            // 位置基准线
            double posX = (Part.position + phoneme.position - TickOffset) * TickWidth;
            if (Math.Abs(pointerPos.X - posX) <= HandleHitRadius * 0.75 && pointerPos.Y >= TopMargin && pointerPos.Y <= envelopeTopY + envelopeHeight + 6)
            {
                return (AdvancedHandleType.TimingLine, phoneme);
            }
        }

        return (AdvancedHandleType.None, null);
    }

    private UPhoneme? FindPhonemeAtTick(double partRelativeTick)
    {
        if (Part == null)
        {
            return null;
        }

        foreach (UPhoneme phoneme in Part.phonemes)
        {
            if (partRelativeTick >= phoneme.position && partRelativeTick <= phoneme.End)
            {
                return phoneme;
            }
        }
        return null;
    }

    public void OnNext(UCommand cmd, bool isUndo)
    {
        switch (cmd)
        {
            case NoteCommand:
            case PartCommand:
            case PhonemizedNotification:
            case ExpCommand:
                InvalidateVisual();
                break;
        }
    }
}
