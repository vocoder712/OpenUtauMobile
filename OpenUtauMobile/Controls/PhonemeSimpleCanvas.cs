using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Themes.OpenUtauMobile.Runtime;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Controls;

/// <summary>
/// 简易音素画布，SynthV 风格：独立的圆角音素卡片，卡片之间有清晰间距与显著的垂直边界手柄。
/// 双击音素卡片直接唤起音素别名编辑弹窗。
/// </summary>
public class PhonemeSimpleCanvas : Control, ICmdSubscriber
{
    public static readonly StyledProperty<UVoicePart?> PartProperty =
        AvaloniaProperty.Register<PhonemeSimpleCanvas, UVoicePart?>(nameof(Part));

    public static readonly StyledProperty<double> TickWidthProperty =
        AvaloniaProperty.Register<PhonemeSimpleCanvas, double>(nameof(TickWidth), 0.1);

    public static readonly StyledProperty<double> TickOffsetProperty =
        AvaloniaProperty.Register<PhonemeSimpleCanvas, double>(nameof(TickOffset));

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

    // 拖拽与双击状态
    private bool _isDraggingBoundary;
    private UPhoneme? _draggingPhoneme;
    private UPhoneme? _animatingPhoneme;
    private double _dragStartPointerX;
    private int _dragInitialOffset;
    private int _lastPushedOffset;

    // 手柄展开动效状态
    private DispatcherTimer? _animTimer;
    private double _animProgress;
    private double _animStartProgress;
    private double _animTargetProgress;
    private DateTime _animStartTime;
    private const double AnimDurationMs = 130.0;

    private DateTime _lastClickTime = DateTime.MinValue;
    private Point _lastClickPoint;
    private const double DoubleClickMaxTimeMs = 350;
    private const double DoubleClickMaxDistance = 24.0;
    private const double BoundaryHitRadius = 20.0;
    private const double ChipMargin = 5.0; // 音素卡片与边界手柄之间的水平间隙（卡片之间共留出 10px 间距）

    public PhonemeSimpleCanvas()
    {
        ClipToBounds = true;
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
            if (_animProgress <= 0.0 && !_isDraggingBoundary)
            {
                _animatingPhoneme = null;
            }
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
        if (Part == null || Bounds.Width <= 0 || Bounds.Height <= 0)
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

        IBrush defaultChipFill = ThemeResources.GetBrush("Sem.Color.SurfaceContainerHigh");
        IBrush selectedChipFill = ThemeResources.GetBrush("Sem.Color.PrimaryContainer");
        IPen chipBorderPen = ThemeResources.GetPen("Sem.Color.OutlineVariant", 1.0);
        IBrush textBrush = ThemeResources.GetBrush("Sem.Color.OnSurface");
        IBrush selectedTextBrush = ThemeResources.GetBrush("Sem.Color.OnPrimaryContainer");

        IBrush boundaryDefaultBrush = ThemeResources.GetBrush("Sem.Color.Outline");
        IBrush boundaryActiveBrush = ThemeResources.GetBrush("Sem.Color.Primary");

        double canvasHeight = Bounds.Height;
        // 顶部预留 14dp 空隙避开上方悬浮的分割条胶囊手柄，底部预留 6dp
        const double topMargin = 14.0;
        const double bottomMargin = 6.0;
        double blockHeight = Math.Max(16.0, canvasHeight - topMargin - bottomMargin);

        foreach (UPhoneme phoneme in Part.phonemes)
        {
            if (phoneme.Parent == null)
            {
                continue;
            }

            double phonemeAbsStart = partPos + phoneme.position;
            double phonemeAbsEnd = partPos + phoneme.End;

            if (phonemeAbsEnd < viewLeftTick || phonemeAbsStart > viewRightTick)
            {
                continue;
            }

            double x1 = (phonemeAbsStart - TickOffset) * TickWidth;
            double x2 = (phonemeAbsEnd - TickOffset) * TickWidth;

            // 卡片左右各留出间隙，让边界手柄位于卡片之间而不是重叠在卡片边框上
            double totalWidth = x2 - x1;
            double actualMargin = Math.Min(ChipMargin, Math.Max(1.0, totalWidth * 0.15));
            double chipLeft = x1 + actualMargin;
            double chipRight = x2 - actualMargin;
            double chipWidth = Math.Max(2.0, chipRight - chipLeft);

            bool isSelected = ViewModel?.SelectedNotes.Contains(phoneme.Parent) ?? false;
            IBrush fillBrush = isSelected ? selectedChipFill : defaultChipFill;
            IBrush currentTextBrush = isSelected ? selectedTextBrush : textBrush;

            // 1. 绘制音素卡片（SynthV 风格独立圆角药丸框）
            Rect blockRect = new Rect(chipLeft, topMargin, chipWidth, blockHeight);
            context.DrawRectangle(fillBrush, chipBorderPen, blockRect, 4, 4);

            // 2. 绘制音素文字
            string displayText = !string.IsNullOrEmpty(phoneme.phonemeMapped) ? phoneme.phonemeMapped : phoneme.phoneme;
            if (!string.IsNullOrEmpty(displayText) && chipWidth > 8.0)
            {
                bool isCustom = phoneme.phoneme != phoneme.rawPhoneme;
                TextLayout textLayout = TextLayoutCache.Get(displayText, currentTextBrush, 12, isCustom);
                if (textLayout.Width <= chipWidth - 4.0)
                {
                    double textX = chipLeft + (chipWidth - textLayout.Width) * 0.5;
                    double textY = topMargin + (blockHeight - textLayout.Height) * 0.5;
                    using (context.PushTransform(Matrix.CreateTranslation(textX, textY)))
                    {
                        textLayout.Draw(context, new Point(0, 0));
                    }
                }
                else
                {
                    double textX = chipLeft + 2.0;
                    double textY = topMargin + (blockHeight - textLayout.Height) * 0.5;
                    using (context.PushClip(new Rect(chipLeft + 1, topMargin, chipWidth - 2, blockHeight)))
                    using (context.PushTransform(Matrix.CreateTranslation(textX, textY)))
                    {
                        textLayout.Draw(context, new Point(0, 0));
                    }
                }
            }

            // 3. 绘制卡片之间的垂直边界手柄条（居中在卡片间隙）
            bool isModified = phoneme.rawPosition != phoneme.position;
            bool isAnimTarget = _animatingPhoneme == phoneme && _animProgress > 0.001;
            double progress = isAnimTarget ? _animProgress : 0.0;

            double expansion = 5.0 * progress; // 拖拽时平滑上下延伸 5dp，适度突出于卡片
            double widthBonus = 1.2 * progress;
            double handleWidth = (isModified ? 3.5 : 2.5) + widthBonus;
            IBrush handleBrush = (isModified || progress > 0.001) ? boundaryActiveBrush : boundaryDefaultBrush;
            double handleX = x1 - handleWidth * 0.5;
            double handleY = topMargin + 2.0 - expansion;
            double handleH = Math.Max(4.0, blockHeight - 4.0) + expansion * 2.0;

            Rect handleRect = new Rect(handleX, handleY, handleWidth, handleH);
            context.DrawRectangle(handleBrush, null, handleRect, handleWidth * 0.5, handleWidth * 0.5);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Part == null)
        {
            return;
        }

        Point pos = e.GetPosition(this);
        double partPos = Part.position;
        double currentTick = pos.X / TickWidth + TickOffset - partPos;

        // 1. 测试是否击中边界手柄（支持滑动调整 timing offset）
        UPhoneme? hitBoundaryPhoneme = FindPhonemeBoundaryAt(pos.X);
        if (hitBoundaryPhoneme != null && hitBoundaryPhoneme.Parent != null)
        {
            _isDraggingBoundary = true;
            _draggingPhoneme = hitBoundaryPhoneme;
            _animatingPhoneme = hitBoundaryPhoneme;
            _dragStartPointerX = pos.X;
            _dragInitialOffset = hitBoundaryPhoneme.Parent.GetPhonemeOverride(hitBoundaryPhoneme.index).offset ?? 0;
            _lastPushedOffset = _dragInitialOffset;

            StartDragAnimation(1.0);
            e.Pointer.Capture(this);
            e.Handled = true;
            DocManager.Inst.StartUndoGroup();

            string phonemeName = !string.IsNullOrEmpty(hitBoundaryPhoneme.phonemeMapped) ? hitBoundaryPhoneme.phonemeMapped : hitBoundaryPhoneme.phoneme;
            double offsetMs = DocManager.Inst.Project != null
                ? DocManager.Inst.Project.timeAxis.TickPosToMsPos(Part.position + hitBoundaryPhoneme.rawPosition + _dragInitialOffset)
                  - DocManager.Inst.Project.timeAxis.TickPosToMsPos(Part.position + hitBoundaryPhoneme.rawPosition)
                : 0.0;
            if (ViewModel != null)
            {
                ViewModel.EditingTip = $"[{phonemeName}] Offset: {_dragInitialOffset:+0;-0;0} tick ({offsetMs:+0.0;-0.0;0.0} ms)";
            }
            return;
        }

        // 2. 双击检测：打开音素别名编辑弹窗
        DateTime now = DateTime.UtcNow;
        double elapsedMs = (now - _lastClickTime).TotalMilliseconds;
        double dist = Math.Abs(pos.X - _lastClickPoint.X) + Math.Abs(pos.Y - _lastClickPoint.Y);

        if (elapsedMs < DoubleClickMaxTimeMs && dist < DoubleClickMaxDistance)
        {
            UPhoneme? hitPhoneme = FindPhonemeAtTick(currentTick);
            if (hitPhoneme != null && hitPhoneme.Parent != null)
            {
                ViewModel?.RaiseRequestEditPhoneme(Part, hitPhoneme.Parent, hitPhoneme.index);
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
        if (!_isDraggingBoundary || _draggingPhoneme == null || Part == null)
        {
            return;
        }

        Point pos = e.GetPosition(this);
        double deltaPx = pos.X - _dragStartPointerX;
        int deltaTicks = (int)Math.Round(deltaPx / TickWidth);
        int newOffset = _dragInitialOffset + deltaTicks;

        if (newOffset != _lastPushedOffset)
        {
            _lastPushedOffset = newOffset;
            DocManager.Inst.ExecuteCmd(new PhonemeOffsetCommand(Part, _draggingPhoneme.Parent, _draggingPhoneme.index, newOffset));
            InvalidateVisual();
        }

        string phonemeName = !string.IsNullOrEmpty(_draggingPhoneme.phonemeMapped) ? _draggingPhoneme.phonemeMapped : _draggingPhoneme.phoneme;
        double offsetMs = DocManager.Inst.Project != null
            ? DocManager.Inst.Project.timeAxis.TickPosToMsPos(Part.position + _draggingPhoneme.rawPosition + newOffset)
              - DocManager.Inst.Project.timeAxis.TickPosToMsPos(Part.position + _draggingPhoneme.rawPosition)
            : 0.0;
        if (ViewModel != null)
        {
            ViewModel.EditingTip = $"[{phonemeName}] Offset: {newOffset:+0;-0;0} tick ({offsetMs:+0.0;-0.0;0.0} ms)";
        }
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_isDraggingBoundary)
        {
            _isDraggingBoundary = false;
            _draggingPhoneme = null;
            StartDragAnimation(0.0);
            DocManager.Inst.EndUndoGroup();
            e.Pointer.Capture(null);
            if (ViewModel != null)
            {
                ViewModel.EditingTip = string.Empty;
            }
            e.Handled = true;
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_isDraggingBoundary)
        {
            _isDraggingBoundary = false;
            _draggingPhoneme = null;
            StartDragAnimation(0.0);
            DocManager.Inst.EndUndoGroup();
            if (ViewModel != null)
            {
                ViewModel.EditingTip = string.Empty;
            }
        }
    }

    private UPhoneme? FindPhonemeBoundaryAt(double pointerX)
    {
        if (Part == null)
        {
            return null;
        }

        double partPos = Part.position;
        UPhoneme? best = null;
        double bestDist = BoundaryHitRadius;

        foreach (UPhoneme phoneme in Part.phonemes)
        {
            if (phoneme.Parent == null)
            {
                continue;
            }

            double x = (partPos + phoneme.position - TickOffset) * TickWidth;
            double dist = Math.Abs(pointerX - x);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = phoneme;
            }
        }

        return best;
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
