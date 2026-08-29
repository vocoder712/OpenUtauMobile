using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Themes.OpenUtauMobile.Runtime;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Controls;

/// <summary>
/// 参数曲线与表情画布：支持多音轨/声部的曲线绘制（Curve）以及逐音素表情参数（Numerical / Options）的绘制与擦除。
/// 顶部留有安全边距避开上方分割手柄。
/// </summary>
public class ParameterCanvas : Control, ICmdSubscriber
{
    public event Action<Point>? RequestMagnifierOpen;
    public event Action<Point>? RequestMagnifierUpdate;
    public event Action? RequestMagnifierClose;

    public static readonly StyledProperty<UVoicePart?> PartProperty =
        AvaloniaProperty.Register<ParameterCanvas, UVoicePart?>(nameof(Part));

    public static readonly StyledProperty<double> TickWidthProperty =
        AvaloniaProperty.Register<ParameterCanvas, double>(nameof(TickWidth), 0.1);

    public static readonly StyledProperty<double> TickOffsetProperty =
        AvaloniaProperty.Register<ParameterCanvas, double>(nameof(TickOffset));

    public static readonly StyledProperty<string> PrimaryKeyProperty =
        AvaloniaProperty.Register<ParameterCanvas, string>(nameof(PrimaryKey), "vel");

    public static readonly StyledProperty<string> SecondaryKeyProperty =
        AvaloniaProperty.Register<ParameterCanvas, string>(nameof(SecondaryKey), string.Empty);

    public static readonly StyledProperty<bool> IsEraseModeProperty =
        AvaloniaProperty.Register<ParameterCanvas, bool>(nameof(IsEraseMode));

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

    public string PrimaryKey
    {
        get => GetValue(PrimaryKeyProperty);
        set => SetValue(PrimaryKeyProperty, value);
    }

    public string SecondaryKey
    {
        get => GetValue(SecondaryKeyProperty);
        set => SetValue(SecondaryKeyProperty, value);
    }

    public bool IsEraseMode
    {
        get => GetValue(IsEraseModeProperty);
        set => SetValue(IsEraseModeProperty, value);
    }

    private const double TopMargin = 14.0;
    private const double BottomMargin = 6.0;

    // 绘制与触摸交互状态
    private bool _isDrawing;
    private bool _isMagnifierOpen;
    private int _lastTick;
    private int _lastValue;
    private Point? _drawingPointer;

    private readonly Geometry _pointGeometry = new EllipseGeometry(new Rect(-3.0, -3.0, 6.0, 6.0));
    private readonly Geometry _circleGeometry = new EllipseGeometry(new Rect(-4.0, -4.0, 8.0, 8.0));

    private PianoRollViewModel? _viewModel;
    private PianoRollViewModel? ViewModel => _viewModel ?? (DataContext as PianoRollViewModel);

    public ParameterCanvas()
    {
        ClipToBounds = true;
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
            change.Property == TickOffsetProperty ||
            change.Property == PrimaryKeyProperty ||
            change.Property == SecondaryKeyProperty ||
            change.Property == IsEraseModeProperty)
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
        UProject project = DocManager.Inst.Project;
        if (Part.trackNo < 0 || Part.trackNo >= project.tracks.Count)
        {
            return;
        }

        UTrack track = project.tracks[Part.trackNo];

        // 1. 绘制背景次要参数（低不透明度）
        if (!string.IsNullOrEmpty(SecondaryKey) && SecondaryKey != PrimaryKey)
        {
            if (track.TryGetExpDescriptor(project, SecondaryKey, out UExpressionDescriptor? secDesc))
            {
                using (context.PushOpacity(0.28))
                {
                    RenderExpression(context, project, track, secDesc, false);
                }
            }
        }

        // 2. 绘制主参数
        if (!string.IsNullOrEmpty(PrimaryKey))
        {
            if (track.TryGetExpDescriptor(project, PrimaryKey, out UExpressionDescriptor? priDesc))
            {
                RenderExpression(context, project, track, priDesc, true);
            }
        }

        // 3. 绘制触点光标
        if (_isDrawing && _drawingPointer.HasValue)
        {
            Point center = _drawingPointer.Value;
            IBrush haloBrush = ThemeResources.GetBrush(IsEraseMode ? "Sem.Color.Error" : "Sem.Color.Primary");
            using (context.PushOpacity(0.25))
            {
                context.DrawEllipse(haloBrush, null, center, 10.0, 10.0);
            }
            context.DrawEllipse(haloBrush, null, center, 3.5, 3.5);
        }
    }

    private void RenderExpression(
        DrawingContext context,
        UProject project,
        UTrack track,
        UExpressionDescriptor descriptor,
        bool isPrimary)
    {
        if (descriptor.max <= descriptor.min || Part == null || TickWidth <= 0)
        {
            return;
        }

        double partLeft = (Part.position - TickOffset) * TickWidth;
        double partRight = (Part.End - TickOffset) * TickWidth;
        double clipLeft = Math.Clamp(partLeft, 0, Bounds.Width);
        double clipRight = Math.Clamp(partRight, 0, Bounds.Width);
        if (clipRight <= clipLeft)
        {
            return;
        }

        using IDisposable partClip = context.PushClip(
            new Rect(clipLeft, 0, clipRight - clipLeft, Bounds.Height));

        double leftTick = TickOffset - 480;
        double rightTick = TickOffset + Bounds.Width / TickWidth + 480;
        double canvasHeight = Bounds.Height;
        double usableHeight = Math.Max(16.0, canvasHeight - TopMargin - BottomMargin);
        double valueRange = descriptor.max - descriptor.min;

        IPen primaryEditedPen = ThemeResources.GetPen("Sem.Color.Primary", 2.0); // 已编辑的主参数
        IPen primaryDefaultPen = ThemeResources.GetPen("Sem.Color.Primary", 0.5); // 未编辑的主参数
        IPen faintPen = ThemeResources.GetPen("Sem.Color.OutlineVariant"); // 参考线
        IBrush fillBrush = isPrimary ? 
            ThemeResources.GetBrush("Sem.Color.Primary") : ThemeResources.GetBrush("Sem.Color.Secondary");

        // ── 曲线类型 ─────────────────────────────────────────────────────────
        if (descriptor.type == UExpressionType.Curve)
        {
            UCurve? curve = Part.curves.FirstOrDefault(c => c.descriptor == descriptor || c.abbr == descriptor.abbr);
            double defaultHeight = Math.Round(TopMargin + usableHeight * (1.0 - (descriptor.defaultValue - descriptor.min) / valueRange));
            double partStartX = (Part.position - TickOffset) * TickWidth;
            double partEndX = (Part.End - TickOffset) * TickWidth;

            // 仅正在编辑的参数绘制默认值参考基线
            if (isPrimary)
            {
                context.DrawLine(faintPen, new Point(partStartX, defaultHeight), new Point(partEndX, defaultHeight));
            }

            // 没有任何编辑
            if (curve == null || curve.xs.Count == 0)
            {
                context.DrawLine(primaryDefaultPen, new Point(partStartX, defaultHeight), new Point(partEndX, defaultHeight));
                return;
            }

            double leftPartTick = Math.Max(0, leftTick - Part.position);
            double rightPartTick = Math.Min(Part.duration, rightTick - Part.position);
            int lTick = (int)Math.Floor(leftPartTick / 5) * 5; // 分片内屏幕左边界
            int rTick = (int)Math.Ceiling(rightPartTick / 5) * 5; // 分片内屏幕右边界

            int index = curve.xs.BinarySearch(lTick); // 运气很好，屏幕左边缘恰好有一个点
            if (index < 0)
            {
                index = ~index; // 得到第一个大于 lTick 的曲线点索引
                if (index == 0) // 补充从屏幕左侧至第一个点的默认值线
                {
                    double firstCurveX = Math.Clamp(
                        (Part.position + curve.xs[0] - TickOffset) * TickWidth,
                        partStartX,
                        partEndX);
                    context.DrawLine(
                        primaryDefaultPen,
                        new Point(partStartX, defaultHeight),
                        new Point(firstCurveX, defaultHeight));
                }
            }
            index = Math.Max(0, index - 1); // 向前移动一个其实索引，确保横跨左边界的曲线段也能被绘制

            while (index < curve.xs.Count - 1)
            {
                float tick1 = index < 0 ? lTick : curve.xs[index];
                float val1 = index < 0 ? descriptor.defaultValue : curve.ys[index];
                double x1 = (Part.position + tick1 - TickOffset) * TickWidth;
                double y1 = TopMargin + usableHeight * (1.0 - (val1 - descriptor.min) / valueRange);

                float tick2 = index == curve.xs.Count - 1 ? 
                    rTick : curve.xs[index + 1];
                float val2 = index == curve.xs.Count - 1 ? 
                    descriptor.defaultValue : curve.ys[index + 1];
                double x2 = (Part.position + tick2 - TickOffset) * TickWidth;
                double y2 = TopMargin + usableHeight * (1.0 - (val2 - descriptor.min) / valueRange);
                
                bool overridden = curve.ys[index] != descriptor.defaultValue || curve.ys[index + 1] != descriptor.defaultValue; // 是否是默认值

                context.DrawLine(overridden ? primaryEditedPen : primaryDefaultPen, new Point(x1, y1), new Point(x2, y2));

                index++;
                if (tick2 >= rTick) // 剪枝
                {
                    break;
                }
            }

            // 绘制曲线末端的默认值延伸线
            if (curve.xs[^1] >= rTick)
            {
                return;
            }

            double lastCurveX = Math.Clamp(
                (Part.position + curve.xs[^1] - TickOffset) * TickWidth,
                partStartX,
                partEndX);
            context.DrawLine(
                primaryDefaultPen,
                new Point(lastCurveX, defaultHeight),
                new Point(partEndX, defaultHeight));
            return;
        }

        // ── 数值与选项类型（逐音素显示） ──────────────────────────────────────────
        double optionHeight = descriptor.type == UExpressionType.Options && descriptor.options != null && descriptor.options.Length > 0
            ? usableHeight / descriptor.options.Length
            : 0;

        foreach (UPhoneme phoneme in Part.phonemes)
        {
            if (phoneme.Parent == null || phoneme.Error)
            {
                continue;
            }

            double phonemeAbsStart = Part.position + phoneme.position;
            double phonemeAbsEnd = Part.position + phoneme.End;

            if (phonemeAbsEnd < leftTick || phonemeAbsStart > rightTick)
            {
                continue;
            }

            double x1 = (phonemeAbsStart - TickOffset) * TickWidth;
            double x2 = (phonemeAbsEnd - TickOffset) * TickWidth;
            (float value, bool overridden) = phoneme.GetExpression(project, track, descriptor.abbr);

            if (descriptor.type == UExpressionType.Numerical)
            {
                double valY = Math.Round(TopMargin + usableHeight * (1.0 - (value - descriptor.min) / valueRange));
                double zeroY = Math.Round(TopMargin + usableHeight * (1.0 - (0f - descriptor.min) / valueRange));

                context.DrawLine(primaryEditedPen, new Point(x1 + 0.5, zeroY), new Point(x1 + 0.5, valY));
                context.DrawLine(primaryEditedPen, new Point(x1, valY), new Point(Math.Max(x1 + 4.0, x2 - 2.0), valY));

                using (context.PushTransform(Matrix.CreateTranslation(x1 + 0.5, valY)))
                {
                    context.DrawGeometry(overridden ? fillBrush : Brushes.Transparent, primaryEditedPen, _pointGeometry);
                }
            }
            else if (descriptor.type == UExpressionType.Options && descriptor.options != null)
            {
                for (int i = 0; i < descriptor.options.Length; i++)
                {
                    double y = TopMargin + optionHeight * (descriptor.options.Length - 1 - i + 0.5);
                    using (context.PushTransform(Matrix.CreateTranslation(x1 + 6.0, y)))
                    {
                        if ((int)value == i)
                        {
                            context.DrawGeometry(overridden ? fillBrush : Brushes.Transparent, primaryEditedPen, _circleGeometry);
                        }
                        else
                        {
                            context.DrawGeometry(null, faintPen, _circleGeometry);
                        }
                    }
                }
            }
        }

        // 绘制选项文字标签
        if (descriptor.type == UExpressionType.Options && descriptor.options != null && isPrimary)
        {
            IBrush labelBrush = ThemeResources.GetBrush("Sem.Color.OnSurfaceVariant");
            for (int i = 0; i < descriptor.options.Length; i++)
            {
                string optionName = descriptor.options[i];
                if (string.IsNullOrEmpty(optionName))
                {
                    optionName = $"[{i}]";
                }
                TextLayout textLayout = TextLayoutCache.Get(optionName, labelBrush, 10, false);
                double y = TopMargin + optionHeight * (descriptor.options.Length - 1 - i + 0.5) - textLayout.Height * 0.5;
                using (context.PushTransform(Matrix.CreateTranslation(6.0, y)))
                {
                    textLayout.Draw(context, new Point(0, 0));
                }
            }
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Part == null || DocManager.Inst.Project == null || string.IsNullOrEmpty(PrimaryKey))
        {
            return;
        }

        UProject project = DocManager.Inst.Project;
        if (Part.trackNo < 0 || Part.trackNo >= project.tracks.Count)
        {
            return;
        }

        UTrack track = project.tracks[Part.trackNo];
        if (!track.TryGetExpDescriptor(project, PrimaryKey, out UExpressionDescriptor? descriptor) || descriptor == null)
        {
            return;
        }

        Point pos = e.GetPosition(this);
        if (!TryGetPartTick(pos.X, false, out int tick))
        {
            return;
        }

        _isDrawing = true;
        _drawingPointer = pos;
        e.Pointer.Capture(this);
        DocManager.Inst.StartUndoGroup();

        int value = CalculateValueFromY(pos.Y, descriptor);

        _lastTick = tick;
        _lastValue = value;

        if (descriptor.type == UExpressionType.Curve)
        {
            _isMagnifierOpen = true;
            RequestMagnifierOpen?.Invoke(pos);
        }

        ApplyEditAt(project, track, descriptor, tick, value, tick, value);
        UpdateEditingTip(descriptor, tick, value);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_isDrawing || Part == null || DocManager.Inst.Project == null || string.IsNullOrEmpty(PrimaryKey))
        {
            return;
        }

        UProject project = DocManager.Inst.Project;
        if (Part.trackNo < 0 || Part.trackNo >= project.tracks.Count)
        {
            return;
        }

        UTrack track = project.tracks[Part.trackNo];
        if (!track.TryGetExpDescriptor(project, PrimaryKey, out UExpressionDescriptor? descriptor) || descriptor == null)
        {
            return;
        }

        Point pos = e.GetPosition(this);
        _drawingPointer = pos;
        if (_isMagnifierOpen)
        {
            RequestMagnifierUpdate?.Invoke(pos);
        }

        if (!TryGetPartTick(pos.X, true, out int tick))
        {
            return;
        }

        int value = CalculateValueFromY(pos.Y, descriptor);

        ApplyEditAt(project, track, descriptor, tick, value, _lastTick, _lastValue);
        UpdateEditingTip(descriptor, tick, value);

        _lastTick = tick;
        _lastValue = value;

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_isDrawing)
        {
            _isDrawing = false;
            _drawingPointer = null;
            DocManager.Inst.EndUndoGroup();
            e.Pointer.Capture(null);
            if (ViewModel != null)
            {
                ViewModel.EditingTip = string.Empty;
            }
            CloseMagnifier();
            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_isDrawing)
        {
            _isDrawing = false;
            _drawingPointer = null;
            DocManager.Inst.EndUndoGroup();
            if (ViewModel != null)
            {
                ViewModel.EditingTip = string.Empty;
            }
            CloseMagnifier();
            InvalidateVisual();
        }
    }

    private void CloseMagnifier()
    {
        if (!_isMagnifierOpen)
        {
            return;
        }

        _isMagnifierOpen = false;
        RequestMagnifierClose?.Invoke();
    }

    private void UpdateEditingTip(UExpressionDescriptor descriptor, int tick, int value)
    {
        if (ViewModel == null)
        {
            return;
        }

        if (descriptor.type == UExpressionType.Curve)
        {
            if (IsEraseMode)
            {
                ViewModel.EditingTip = $"{descriptor.name}: Erase (Default: {descriptor.defaultValue:F0})";
            }
            else
            {
                ViewModel.EditingTip = $"{descriptor.name}: {value:+0;-0;0}";
            }
            return;
        }

        UPhoneme? phoneme = FindPhonemeAtTick(tick);
        string phonemePrefix = phoneme != null
            ? $"[{(string.IsNullOrEmpty(phoneme.phonemeMapped) ? phoneme.phoneme : phoneme.phonemeMapped)}] "
            : string.Empty;

        if (descriptor.type == UExpressionType.Numerical)
        {
            if (IsEraseMode)
            {
                ViewModel.EditingTip = $"{phonemePrefix}{descriptor.name}: Reset (Default: {descriptor.defaultValue:F0})";
            }
            else
            {
                ViewModel.EditingTip = $"{phonemePrefix}{descriptor.name}: {value}";
            }
        }
        else if (descriptor.type == UExpressionType.Options)
        {
            string optName = (descriptor.options != null && value >= 0 && value < descriptor.options.Length)
                ? descriptor.options[value]
                : value.ToString();
            if (IsEraseMode)
            {
                ViewModel.EditingTip = $"{phonemePrefix}{descriptor.name}: Reset";
            }
            else
            {
                ViewModel.EditingTip = $"{phonemePrefix}{descriptor.name}: {optName}";
            }
        }
    }

    private UPhoneme? FindPhonemeAtTick(int tick)
    {
        if (Part == null)
        {
            return null;
        }

        double partRelativeTick = tick - Part.position;
        foreach (UPhoneme phoneme in Part.phonemes)
        {
            if (partRelativeTick >= phoneme.position && partRelativeTick <= phoneme.End)
            {
                return phoneme;
            }
        }

        return null;
    }

    private int CalculateValueFromY(double y, UExpressionDescriptor descriptor)
    {
        double usableHeight = Math.Max(16.0, Bounds.Height - TopMargin - BottomMargin);
        if (descriptor.type == UExpressionType.Options && descriptor.options != null && descriptor.options.Length > 0)
        {
            double optionHeight = usableHeight / descriptor.options.Length;
            int optionIndex = descriptor.options.Length - 1 - (int)Math.Clamp(Math.Floor((y - TopMargin) / optionHeight), 0, descriptor.options.Length - 1);
            return optionIndex;
        }

        double ratio = Math.Clamp(1.0 - (y - TopMargin) / usableHeight, 0.0, 1.0);
        float val = descriptor.min + (float)(ratio * (descriptor.max - descriptor.min));
        if (IsEraseMode)
        {
            return (int)descriptor.defaultValue;
        }
        return (int)Math.Round(val);
    }

    private bool TryGetPartTick(double x, bool clampToPart, out int tick)
    {
        tick = 0;
        if (Part == null || TickWidth <= 0)
        {
            return false;
        }

        double absoluteTick = x / TickWidth + TickOffset;
        if (!clampToPart && (absoluteTick < Part.position || absoluteTick > Part.End))
        {
            return false;
        }

        tick = (int)Math.Clamp(absoluteTick, Part.position, Part.End);
        return true;
    }

    private void ApplyEditAt(
        UProject project,
        UTrack track,
        UExpressionDescriptor descriptor,
        int tick,
        int value,
        int lastTick,
        int lastValue)
    {
        if (Part == null)
        {
            return;
        }

        if (descriptor.type == UExpressionType.Curve)
        {
            int targetVal = IsEraseMode ? (int)descriptor.defaultValue : value;
            int targetLastVal = IsEraseMode ? (int)descriptor.defaultValue : lastValue;
            int partTick = Math.Clamp(tick - Part.position, 0, Part.duration);
            int lastPartTick = Math.Clamp(lastTick - Part.position, 0, Part.duration);
            DocManager.Inst.ExecuteCmd(new SetCurveCommand(
                project,
                Part,
                descriptor.abbr,
                partTick,
                targetVal,
                lastPartTick,
                targetLastVal));
            return;
        }

        // 数值或选项：找到触摸点对应的音素
        double partRelativeTick = tick - Part.position;
        foreach (UPhoneme phoneme in Part.phonemes)
        {
            if (partRelativeTick >= phoneme.position && partRelativeTick <= phoneme.End)
            {
                float? newVal = IsEraseMode ? null : (float?)value;
                DocManager.Inst.ExecuteCmd(new SetPhonemeExpressionCommand(project, track, Part, phoneme, descriptor.abbr, newVal));
                break;
            }
        }
    }

    public void OnNext(UCommand cmd, bool isUndo)
    {
        switch (cmd)
        {
            case NoteCommand:
            case PartCommand:
            case SetCurveCommand:
            case ExpCommand:
            case PhonemizedNotification:
                InvalidateVisual();
                break;
        }
    }
}
