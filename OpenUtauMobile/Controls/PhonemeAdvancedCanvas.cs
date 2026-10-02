using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Controls.Tokens;
using OpenUtauMobile.Themes.OpenUtauMobile.Runtime;
using OpenUtauMobile.Themes.OpenUtauMobile.Runtime.Resources;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Controls;

/// <summary>
/// 高级音素画布：包络梯形、先行发音、起音、收音及相邻音素重叠控制点。
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

    private bool SupportsPhonemeEnvelope
    {
        get
        {
            UProject project = DocManager.Inst.Project;
            return Part != null && project != null && Part.trackNo >= 0 && Part.trackNo < project.tracks.Count
                && (project.tracks[Part.trackNo].RendererSettings.Renderer?.SupportsPhonemeEnvelope ?? true);
        }
    }

    private bool CanEditActiveHandle => _activeHandleType == AdvancedHandleType.TimingLine || SupportsPhonemeEnvelope;

    private enum AdvancedHandleType
    {
        None,
        TimingLine,
        Preutter,
        Attack,
        Release,
        Overlap
    }

    private AdvancedHandleType _activeHandleType = AdvancedHandleType.None;
    private UPhoneme? _activePhoneme;
    private UPhoneme? _animatingTimingPhoneme;
    private double _dragStartPointerX;
    private double _dragStartHandleTick;
    private float _initialDelta;

    // 手柄展开动效状态
    private DispatcherTimer? _animTimer;
    private double _animProgress;
    private double _animStartProgress;
    private double _animTargetProgress;
    private DateTime _animStartTime;

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
    private readonly PhonemeResetTarget _resetTarget;
    private IPointer? _editingPointer;

    public PhonemeAdvancedCanvas()
    {
        ClipToBounds = true;
        PhonemeCanvasActions.Attach(this, () => Part, point => Part?.phonemes.FirstOrDefault(p => GetAliasLabelBounds(p).Contains(point)) ?? FindPhonemeAtTick(point.X / TickWidth + TickOffset - (Part?.position ?? 0)));
        _resetTarget = new PhonemeResetTarget(this);
    }

    public Rect GetAliasLabelBounds(UPhoneme phoneme)
    {
        string text = !string.IsNullOrEmpty(phoneme.phonemeMapped) ? phoneme.phonemeMapped : phoneme.phoneme;
        TextLayout layout = TextLayoutCache.Get(text, ThemeResources.GetBrush("Sem.Color.OnSurface"), 11, phoneme.phoneme != phoneme.rawPhoneme);
        return new Rect(((Part?.position ?? 0) + phoneme.position - TickOffset) * TickWidth + 2, TopMargin + 2, layout.Width + 8, layout.Height + 2);
    }

    private void StartDragAnimation(double target)
    {
        _animStartProgress = _animProgress;
        _animTargetProgress = target;
        _animStartTime = DateTime.UtcNow;

        if (_animTimer == null)
        {
            _animTimer = new DispatcherTimer { Interval = PhonemeCanvasTokens.FrameInterval };
            _animTimer.Tick += OnAnimTimerTick;
        }
        _animTimer.Start();
    }

    private void OnAnimTimerTick(object? sender, EventArgs e)
    {
        double elapsed = (DateTime.UtcNow - _animStartTime).TotalMilliseconds;
        double t = Math.Clamp(elapsed / PhonemeCanvasTokens.SelectionAnimationDuration.TotalMilliseconds, 0.0, 1.0);
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

    protected override void OnDataContextChanged(EventArgs e)
    {
        _editingPointer?.Capture(null);
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
        _editingPointer?.Capture(null);
        base.OnDetachedFromVisualTree(e);
        DocManager.Inst.RemoveSubscriber(this);
        _animTimer?.Stop();
        _resetTarget.End();
        if (_viewModel != null)
        {
            _viewModel.RequestInvalidateVisual -= InvalidateVisual;
            _viewModel = null;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PartProperty) _editingPointer?.Capture(null);
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
        using (context.PushOpacity(0.6))
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

        bool supportsEnvelope = SupportsPhonemeEnvelope;

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

            bool isSelected = ViewModel?.IsNoteSelected(phoneme.Parent) ?? false;
            IPen pen = isSelected ? selectedPen : defaultPen;
            IBrush fill = isSelected ? selectedBrush : defaultBrush;

            double posX = (phonemeAbsStart - TickOffset) * TickWidth;

            // 不使用包络的渲染器显示音素时长条，位置线仍可拖动。
            if (!phoneme.Error && !supportsEnvelope)
            {
                double endX = (phonemeAbsEnd - TickOffset) * TickWidth;
                using (context.PushOpacity(0.40))
                {
                    context.DrawRectangle(fill, null,
                        new Rect(posX, envelopeTopY, Math.Max(0, endX - posX), envelopeHeight));
                }
            }
            // 1. 绘制包络梯形（5点）
            else if (!phoneme.Error && phoneme.envelope.data.Count >= 5)
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

                // 2. 绘制桌面同义的四个包络手柄。
                // 控制点 0：先行发音（Preutter - 左下角）
                IBrush p0Brush = phoneme.preutterDelta.HasValue ? (pen.Brush ?? selectedBrush) : textBgBrush;
                using (context.PushTransform(Matrix.CreateTranslation(x0, y0)))
                {
                    context.DrawGeometry(p0Brush, pen, _handleGeometry);
                }

                // 控制点 1：起音（Attack）。
                IBrush p1Brush = phoneme.attackTimeDelta.HasValue ? (pen.Brush ?? selectedBrush) : textBgBrush;
                using (context.PushTransform(Matrix.CreateTranslation(x1, y1)))
                {
                    context.DrawGeometry(p1Brush, pen, _handleGeometry);
                }

                // 控制点 3：收音（Release）。
                IBrush p3Brush = phoneme.releaseTimeDelta.HasValue ? (pen.Brush ?? selectedBrush) : textBgBrush;
                using (context.PushTransform(Matrix.CreateTranslation(x3, y3)))
                {
                    context.DrawGeometry(p3Brush, pen, _handleGeometry);
                }

                // 控制点 4 的位置属于当前包络，但修改的是相邻下一音素的重叠。
                if (HasOverlapHandle(phoneme))
                {
                    IBrush p4Brush = phoneme.Next.overlapDelta.HasValue ? (pen.Brush ?? selectedBrush) : textBgBrush;
                    using (context.PushTransform(Matrix.CreateTranslation(x4, y4)))
                    {
                        context.DrawGeometry(p4Brush, pen, _handleGeometry);
                    }
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

        if (_activeHandleType != AdvancedHandleType.None && ViewModel?.UseDesktopMouseInput != true)
        {
            _resetTarget.Render(context);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.Pointer.Type == PointerType.Mouse && e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed) return;
        if (Part == null || DocManager.Inst.Project == null || DocManager.Inst.HasOpenUndoGroup)
        {
            return;
        }

        Point pos = e.GetPosition(this);
        double partPos = Part.position;
        double currentTick = pos.X / TickWidth + TickOffset - partPos;

        // 1. 测试是否击中控制手柄
        UPhoneme? labelPhoneme = Part.phonemes.FirstOrDefault(p => GetAliasLabelBounds(p).Contains(pos));
        UPhoneme? selectedPhoneme = labelPhoneme ?? FindPhonemeAtTick(currentTick);
        if (selectedPhoneme?.Parent is { } selectedNote && ViewModel is { } vm && !vm.IsNoteSelected(selectedNote))
        { vm.SelectedNotes.Clear(); vm.SelectedNotes.Add(selectedNote); }
        (AdvancedHandleType hitType, UPhoneme? hitPhoneme) = labelPhoneme != null ? (AdvancedHandleType.None, null) : HitTestHandle(pos, e.Pointer.Type == PointerType.Mouse);
        if (hitType != AdvancedHandleType.None && hitPhoneme != null)
        {
            _resetTarget.Begin();
            _activeHandleType = hitType;
            _activePhoneme = hitPhoneme;
            if (hitType == AdvancedHandleType.TimingLine)
            {
                _animatingTimingPhoneme = hitPhoneme;
                StartDragAnimation(1.0);
            }
            _dragStartPointerX = pos.X;
            // 用实际手柄所在时间计算毫秒位移，跨变速点时也不依赖音素起点的速度。
            UPhoneme envelopeOwner = hitType == AdvancedHandleType.Overlap ? hitPhoneme.Prev : hitPhoneme;
            int pointIndex = hitType switch
            {
                AdvancedHandleType.Attack => 1,
                AdvancedHandleType.Release => 3,
                AdvancedHandleType.Overlap => 4,
                _ => 0
            };
            _dragStartHandleTick = hitType == AdvancedHandleType.TimingLine
                ? Part.position + hitPhoneme.position
                : DocManager.Inst.Project.timeAxis.MsPosToNonExactTickPos(
                    envelopeOwner.PositionMs + envelopeOwner.envelope.data[pointIndex].X);
            UNote leadingNote = hitPhoneme.Parent.Extends ?? hitPhoneme.Parent;
            UPhonemeOverride overrideData = leadingNote.GetPhonemeOverride(hitPhoneme.index);

            _initialDelta = hitType switch
            {
                AdvancedHandleType.TimingLine => overrideData.offset ?? 0,
                AdvancedHandleType.Preutter => overrideData.preutterDelta ?? 0,
                AdvancedHandleType.Attack => overrideData.attackTimeDelta ?? 0,
                AdvancedHandleType.Release => overrideData.releaseTimeDelta ?? 0,
                AdvancedHandleType.Overlap => overrideData.overlapDelta ?? 0,
                _ => 0
            };

            _editingPointer = e.Pointer;
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
                    AdvancedHandleType.Attack => $"[{phonemeName}] Attack: {_initialDelta:+0.0;-0.0;0.0} ms",
                    AdvancedHandleType.Release => $"[{phonemeName}] Release: {_initialDelta:+0.0;-0.0;0.0} ms",
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
            UPhoneme? clickedPhoneme = labelPhoneme ?? FindPhonemeAtTick(currentTick);
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
        if (_activeHandleType == AdvancedHandleType.None || _activePhoneme == null || Part == null || DocManager.Inst.Project == null
            || !CanEditActiveHandle)
        {
            return;
        }

        Point pos = e.GetPosition(this); // 手指坐标
        _resetTarget.IsActive = ViewModel?.UseDesktopMouseInput != true && _resetTarget.Contains(pos);

        if (_resetTarget.IsActive)
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
        double deltaMs = DocManager.Inst.Project.timeAxis.TickPosToMsPos(_dragStartHandleTick + deltaTicks)
                       - DocManager.Inst.Project.timeAxis.TickPosToMsPos(_dragStartHandleTick);
        UNote leadingNote = _activePhoneme.Parent.Extends ?? _activePhoneme.Parent;

        string phonemeName = !string.IsNullOrEmpty(_activePhoneme.phonemeMapped) ? _activePhoneme.phonemeMapped : _activePhoneme.phoneme;
        switch (_activeHandleType)
        {
            case AdvancedHandleType.TimingLine:
                int newOffset = (int)(_initialDelta + deltaTicks);
                DocManager.Inst.ExecuteCmd(new PhonemeOffsetCommand(Part, leadingNote, _activePhoneme.index, newOffset));
                double offsetMs = DocManager.Inst.Project.timeAxis.TickPosToMsPos(Part.position + _activePhoneme.rawPosition + newOffset)
                                - DocManager.Inst.Project.timeAxis.TickPosToMsPos(Part.position + _activePhoneme.rawPosition);
                if (ViewModel != null)
                {
                    ViewModel.EditingTip = $"[{phonemeName}] Offset: {newOffset:+0;-0;0} tick ({offsetMs:+0.0;-0.0;0.0} ms)";
                }
                break;
            case AdvancedHandleType.Preutter:
                float newPreutter = (float)(_initialDelta - deltaMs);
                DocManager.Inst.ExecuteCmd(new PhonemePreutterCommand(Part, leadingNote, _activePhoneme.index, _activePhoneme, newPreutter));
                if (ViewModel != null)
                {
                    ViewModel.EditingTip = $"[{phonemeName}] Preutter: {newPreutter:+0.0;-0.0;0.0} ms";
                }
                break;
            case AdvancedHandleType.Attack:
                float newAttack = (float)(_initialDelta + deltaMs);
                DocManager.Inst.ExecuteCmd(new PhonemeAttackTimeCommand(Part, leadingNote, _activePhoneme.index, _activePhoneme, newAttack));
                if (ViewModel != null)
                {
                    ViewModel.EditingTip = $"[{phonemeName}] Attack: {leadingNote.GetPhonemeOverride(_activePhoneme.index).attackTimeDelta ?? 0:+0.0;-0.0;0.0} ms";
                }
                break;
            case AdvancedHandleType.Release:
                float newRelease = (float)(_initialDelta - deltaMs);
                DocManager.Inst.ExecuteCmd(new PhonemeReleaseTimeCommand(Part, leadingNote, _activePhoneme.index, _activePhoneme, newRelease));
                if (ViewModel != null)
                {
                    ViewModel.EditingTip = $"[{phonemeName}] Release: {leadingNote.GetPhonemeOverride(_activePhoneme.index).releaseTimeDelta ?? 0:+0.0;-0.0;0.0} ms";
                }
                break;
            case AdvancedHandleType.Overlap:
                float newOverlap = (float)(_initialDelta + deltaMs);
                DocManager.Inst.ExecuteCmd(new PhonemeOverlapCommand(Part, leadingNote, _activePhoneme.index, _activePhoneme, newOverlap));
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
        if (e.Pointer.Type == PointerType.Mouse && e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonReleased) return;
        if (_activeHandleType != AdvancedHandleType.None)
        {
            _resetTarget.IsActive = ViewModel?.UseDesktopMouseInput != true && _resetTarget.Contains(e.GetPosition(this));
            if (_resetTarget.IsActive)
            {
                ResetActiveParameter();
            }
            if (_activeHandleType == AdvancedHandleType.TimingLine)
            {
                StartDragAnimation(0.0);
            }
            _activeHandleType = AdvancedHandleType.None;
            _activePhoneme = null;
            _resetTarget.End();
            DocManager.Inst.EndUndoGroup();
            _editingPointer = null;
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
        _editingPointer = null;
        if (_activeHandleType != AdvancedHandleType.None)
        {
            if (_activeHandleType == AdvancedHandleType.TimingLine)
            {
                StartDragAnimation(0.0);
            }
            _activeHandleType = AdvancedHandleType.None;
            _activePhoneme = null;
            _resetTarget.End();
            if (DocManager.Inst.HasOpenUndoGroup)
            {
                DocManager.Inst.RollBackUndoGroup();
                DocManager.Inst.EndUndoGroup();
            }
            if (ViewModel != null)
            {
                ViewModel.EditingTip = string.Empty;
            }
            InvalidateVisual();
        }
    }

    private void ResetActiveParameter()
    {
        if (Part == null || _activePhoneme?.Parent == null || !CanEditActiveHandle)
        {
            return;
        }

        UNote leadingNote = _activePhoneme.Parent.Extends ?? _activePhoneme.Parent;
        switch (_activeHandleType)
        {
            case AdvancedHandleType.TimingLine:
                DocManager.Inst.ExecuteCmd(new PhonemeOffsetCommand(
                    Part, leadingNote, _activePhoneme.index, 0));
                break;
            case AdvancedHandleType.Preutter:
                DocManager.Inst.ExecuteCmd(new PhonemePreutterCommand(
                    Part, leadingNote, _activePhoneme.index, _activePhoneme, 0));
                break;
            case AdvancedHandleType.Attack:
                DocManager.Inst.ExecuteCmd(new PhonemeAttackTimeCommand(
                    Part, leadingNote, _activePhoneme.index, _activePhoneme, 0));
                break;
            case AdvancedHandleType.Release:
                DocManager.Inst.ExecuteCmd(new PhonemeReleaseTimeCommand(
                    Part, leadingNote, _activePhoneme.index, _activePhoneme, 0));
                break;
            case AdvancedHandleType.Overlap:
                DocManager.Inst.ExecuteCmd(new PhonemeOverlapCommand(
                    Part, leadingNote, _activePhoneme.index, _activePhoneme, 0));
                break;
        }
    }

    private static bool HasOverlapHandle(UPhoneme phoneme)
    {
        return phoneme.Next?.Parent != null && phoneme.Next.adjacent
            && phoneme.Next.position == phoneme.End && phoneme.Next.Prev == phoneme;
    }

    private (AdvancedHandleType, UPhoneme?) HitTestHandle(Point pointerPos, bool mouse)
    {
        if (Part == null || DocManager.Inst.Project == null)
        {
            return (AdvancedHandleType.None, null);
        }

        double hitRadius = mouse && ViewModel?.UseDesktopMouseInput == true ? 7 : HandleHitRadius;
        double totalHeight = Bounds.Height;
        double envelopeTopY = TopMargin + LabelHeight + 4.0;
        double envelopeHeight = Math.Max(20.0, totalHeight - envelopeTopY - BottomMargin);
        TimeAxis timeAxis = DocManager.Inst.Project.timeAxis;
        bool supportsEnvelope = SupportsPhonemeEnvelope;
        (AdvancedHandleType, UPhoneme?) nearest = (AdvancedHandleType.None, null);
        double nearestDistance = double.PositiveInfinity;

        // 先比较所有包络点的距离，避免相邻音素或短包络被遍历靠前的手柄抢占。
        foreach (UPhoneme phoneme in Part.phonemes)
        {
            if (phoneme.Parent == null || phoneme.Parent.OverlapError || phoneme.Error)
            {
                continue;
            }

            if (supportsEnvelope && phoneme.envelope.data.Count >= 5)
            {
                Consider(phoneme, 0, AdvancedHandleType.Preutter, phoneme);
                Consider(phoneme, 1, AdvancedHandleType.Attack, phoneme);
                Consider(phoneme, 3, AdvancedHandleType.Release, phoneme);
                if (HasOverlapHandle(phoneme))
                {
                    Consider(phoneme, 4, AdvancedHandleType.Overlap, phoneme.Next);
                }
            }
        }

        if (nearest.Item1 != AdvancedHandleType.None)
        {
            return nearest;
        }

        foreach (UPhoneme phoneme in Part.phonemes)
        {
            if (phoneme.Parent == null || phoneme.Parent.OverlapError || phoneme.Error)
            {
                continue;
            }
            // 位置基准线
            double posX = (Part.position + phoneme.position - TickOffset) * TickWidth;
            double distance = Math.Abs(pointerPos.X - posX);
            if (distance <= hitRadius * 0.75 && distance < nearestDistance
                && pointerPos.Y >= TopMargin && pointerPos.Y <= envelopeTopY + envelopeHeight + 6)
            {
                nearest = (AdvancedHandleType.TimingLine, phoneme);
                nearestDistance = distance;
            }
        }

        return nearest;

        void Consider(UPhoneme owner, int index, AdvancedHandleType type, UPhoneme target)
        {
            double x = (timeAxis.MsPosToTickPos(owner.PositionMs + owner.envelope.data[index].X) - TickOffset) * TickWidth;
            double y = envelopeTopY + (1.0 - owner.envelope.data[index].Y / 100.0) * envelopeHeight;
            double dx = pointerPos.X - x;
            double dy = pointerPos.Y - y;
            double distance = dx * dx + dy * dy;
            if (Math.Abs(dx) <= hitRadius && Math.Abs(dy) <= hitRadius && distance < nearestDistance)
            {
                nearest = (type, target);
                nearestDistance = distance;
            }
        }
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
            case TrackCommand:
            case LoadProjectNotification:
                InvalidateVisual();
                break;
        }
    }
}