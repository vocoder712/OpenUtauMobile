using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Controls;
using OpenUtauMobile.Services;
using OpenUtauMobile.Services.Platform;
using Avalonia.Input;

namespace OpenUtauMobile.Controls;

/// <summary>
/// DAW 风格的旋钮控件，继承 RangeBase。
/// - 垂直拖拽改变值（向上增加，向下减少）
/// - Shift 键微调（灵敏度 1/10）
/// - 双击重置为默认值
/// - RotateTransform 显示 -135° 到 +135° 的旋转
/// - 拖拽时通过事件向宿主 HUD 推送数值
/// </summary>
public class DawKnob : RangeBase
{
    public enum DawKnobSemanticRole
    {
        Volume,
        Pan,
    }

    public sealed class DawKnobAdjustEventArgs : EventArgs
    {
        public DawKnobSemanticRole Role { get; init; }
        public double Value { get; init; }
        public double Minimum { get; init; }
        public double Maximum { get; init; }
        public double Normalized { get; init; }
        public bool IsFineAdjust { get; init; }
        public int PointerId { get; init; }
    }

    // 用于绑定的旋转角度属性 (范围 -135度 到 +135度)
    public static readonly StyledProperty<double> AngleProperty =
        AvaloniaProperty.Register<DawKnob, double>(nameof(Angle));

    public static readonly StyledProperty<DawKnobSemanticRole> SemanticRoleProperty =
        AvaloniaProperty.Register<DawKnob, DawKnobSemanticRole>(nameof(SemanticRole), DawKnobSemanticRole.Volume);

    public double Angle
    {
        get => GetValue(AngleProperty);
        private set => SetValue(AngleProperty, value);
    }

    public DawKnobSemanticRole SemanticRole
    {
        get => GetValue(SemanticRoleProperty);
        set => SetValue(SemanticRoleProperty, value);
    }

    // 用于显示数值的属性
    public static readonly StyledProperty<string> ValueTextProperty =
        AvaloniaProperty.Register<DawKnob, string>(nameof(ValueText));

    public string ValueText
    {
        get => GetValue(ValueTextProperty);
        private set => SetValue(ValueTextProperty, value);
    }

    private Point _startPosition;
    private double _startValue;
    private bool _isDragging;
    private int _activePointerId;
    private IDesktopPointerDrag? _desktopDrag;
    private IPointer? _pointer;
    private Window? _window;

    public event EventHandler<DawKnobAdjustEventArgs>? AdjustStarted;
    public event EventHandler<DawKnobAdjustEventArgs>? AdjustChanged;
    public event EventHandler<DawKnobAdjustEventArgs>? AdjustCompleted;
    public event EventHandler<DawKnobAdjustEventArgs>? AdjustCancelled;

    /// <summary>
    /// 双击重置的目标值。默认为 0。
    /// </summary>
    public double DefaultValue { get; set; }

    public DawKnob()
    {
        // 初始化时调用一次以确保角度被正确设置
        UpdateAngle();
        UpdateValueText();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // 当值或范围发生变化时，重新计算角度和文本
        if (change.Property == ValueProperty ||
            change.Property == MinimumProperty ||
            change.Property == MaximumProperty)
        {
            UpdateAngle();
            UpdateValueText();
        }
    }

    private void UpdateAngle()
    {
        if (Maximum <= Minimum) return;

        // 计算当前值在范围内的百分比
        double percent = (Value - Minimum) / (Maximum - Minimum);

        // 映射到 -135 到 135 度的范围 (总行程270度)
        Angle = -135.0 + (percent * 270.0);
    }

    private void UpdateValueText()
    {
        // 根据数值格式显示（如果是分数就显示整数，否则显示 1 位小数）
        if (Value % 1 == 0)
        {
            ValueText = Value.ToString("F0", CultureInfo.InvariantCulture);
        }
        else
        {
            ValueText = Value.ToString("F1", CultureInfo.InvariantCulture);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        PointerPoint point = e.GetCurrentPoint(this);

        if (point.Properties.IsLeftButtonPressed)
        {
            // 双击重置为默认值
            if (e.ClickCount == 2)
            {
                RaiseAdjustEvent(AdjustStarted, e, false);
                SetCurrentValue(ValueProperty, DefaultValue);
                RaiseAdjustEvent(AdjustCompleted, e, false);
                e.Handled = true;
                return;
            }

            // 记录初始拖拽状态
            _startPosition = e.GetPosition(this);
            _startValue = Value;
            _isDragging = true;
            _activePointerId = e.Pointer.Id;

            _pointer = e.Pointer;
            e.Pointer.Capture(this); // 捕获鼠标
            if (e.Pointer.Type == PointerType.Mouse && ServiceHub.DesktopPointerDragFactory is { } factory)
            {
                _desktopDrag = factory(this);
                _desktopDrag?.Move(_startPosition);
            }
            RaiseAdjustEvent(AdjustStarted, e, false);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_isDragging)
        {
            Point currentPos = e.GetPosition(this);

            // DAW旋钮的标准逻辑：向上拖拽增加，向下拖拽减少
            double deltaY = _startPosition.Y - currentPos.Y;

            // 基础灵敏度：拖拽1500像素走完全程
            double sensitivity = (Maximum - Minimum) / 1500.0;

            // Shift键微调模式 (灵敏度降低为1/10)
            bool isFineAdjust = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            if (isFineAdjust)
            {
                sensitivity *= 0.1;
            }

            // 计算新值并限制在 Min 和 Max 之间
            double newValue;
            if (_desktopDrag != null)
            {
                Vector delta = _desktopDrag.Move(currentPos);
                newValue = Value + (delta.X - delta.Y) * sensitivity * 3;
            }
            else newValue = _startValue + (deltaY * sensitivity);
            SetCurrentValue(ValueProperty, Math.Clamp(newValue, Minimum, Maximum));
            RaiseAdjustEvent(AdjustChanged, e, isFineAdjust);

            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_isDragging)
        {
            _isDragging = false;
            ReleaseDesktopDrag();
            RaiseAdjustEvent(AdjustCompleted, e, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            e.Pointer.Capture(null); // 释放鼠标
            _activePointerId = 0;
            e.Handled = true;
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_isDragging)
        {
            _isDragging = false;
            ReleaseDesktopDrag();
            RaiseAdjustEvent(AdjustCancelled, null, false);
            _activePointerId = 0;
        }
    }

    private void ReleaseDesktopDrag()
    {
        _desktopDrag?.Dispose();
        _desktopDrag = null;
        _pointer = null;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _window = TopLevel.GetTopLevel(this) as Window;
        if (_window != null) _window.Deactivated += CancelDrag;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CancelDrag(this, EventArgs.Empty);
        if (_window != null) _window.Deactivated -= CancelDrag;
        _window = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void CancelDrag(object? sender, EventArgs e)
    {
        if (!_isDragging) return;
        _pointer?.Capture(null);
        if (_isDragging)
        {
            _isDragging = false;
            ReleaseDesktopDrag();
            RaiseAdjustEvent(AdjustCancelled, null, false);
        }
    }

    private DawKnobAdjustEventArgs BuildAdjustEventArgs(bool isFineAdjust)
    {
        double normalized = 0;
        if (Maximum > Minimum)
        {
            normalized = (Value - Minimum) / (Maximum - Minimum);
            normalized = Math.Clamp(normalized, 0, 1);
        }

        return new DawKnobAdjustEventArgs
        {
            Role = SemanticRole,
            Value = Value,
            Minimum = Minimum,
            Maximum = Maximum,
            Normalized = normalized,
            IsFineAdjust = isFineAdjust,
            PointerId = _activePointerId,
        };
    }

    private void RaiseAdjustEvent(EventHandler<DawKnobAdjustEventArgs>? handler, PointerEventArgs? e, bool isFineAdjust)
    {
        _activePointerId = e?.Pointer.Id ?? _activePointerId;
        handler?.Invoke(this, BuildAdjustEventArgs(isFineAdjust));
    }
}