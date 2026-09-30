using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Rendering;

namespace OpenUtauMobile.Themes.OpenUtauMobile.Controls
{
    /// <summary>以按下时的数值和指针位置为基准拖动，避免命中把手边缘时跳值。</summary>
    public class RelativeSliderTrack : Track, ICustomHitTest
    {
        private IPointer? _pointer;
        private Slider? _slider;
        private Point _origin;
        private double _initialValue;
        private double _valuePerPixel;
        private bool _vertical;

        public RelativeSliderTrack()
        {
            // 在 Thumb 和轨道按钮之前接管输入，阻止框架启动绝对定位拖动。
            AddHandler(PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel);
            AddHandler(PointerMovedEvent, OnMoved, RoutingStrategies.Tunnel);
            AddHandler(PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel);
        }

        // 整个轨道都是拖动区域，不依赖把手与按钮当前的绘制命中结果。
        public bool HitTest(Point point) => new Rect(Bounds.Size).Contains(point);

        private void OnPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!IsEffectivelyEnabled || TemplatedParent is not Slider slider ||
                (e.Pointer.Type != PointerType.Touch && !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed))
            {
                return;
            }

            // 指针捕获本身不能阻止父级滚动手势抢占；从按下起由滑块独占本次手势。
            e.PreventGestureRecognition();
            e.Handled = true;
            if (_pointer != null) return;

            _vertical = slider.Orientation == Orientation.Vertical;
            double extent = _vertical
                ? Bounds.Height - (Thumb?.Bounds.Height ?? 0)
                : Bounds.Width - (Thumb?.Bounds.Width ?? 0);
            _valuePerPixel = extent > 0 ? (slider.Maximum - slider.Minimum) / extent : 0;
            if (_vertical != slider.IsDirectionReversed) _valuePerPixel = -_valuePerPixel;
            _origin = e.GetPosition(this);
            _initialValue = slider.Value;
            _slider = slider;
            _pointer = e.Pointer;
            slider.Focus(NavigationMethod.Pointer);
            e.Pointer.Capture(this);
            ((IPseudoClasses)slider.Classes).Set(":pressed", true);
        }

        private void OnMoved(object? sender, PointerEventArgs e)
        {
            if (e.Pointer != _pointer || _slider == null) return;
            e.PreventGestureRecognition();
            e.Handled = true;
            if (!IsEffectivelyEnabled)
            {
                EndDrag();
                return;
            }

            Point position = e.GetPosition(this);
            double delta = _vertical ? position.Y - _origin.Y : position.X - _origin.X;
            // 始终使用起始值计算，避免吸附和双向绑定的舍入吞掉小幅移动。
            double value = Math.Clamp(_initialValue + delta * _valuePerPixel, _slider.Minimum, _slider.Maximum);
            if (delta == 0) value = _initialValue;
            else if (_slider.IsSnapToTickEnabled) value = SnapToTick(_slider, value);
            _slider.SetCurrentValue(Slider.ValueProperty, value);
        }

        private void OnReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (e.Pointer != _pointer) return;
            e.PreventGestureRecognition();
            e.Handled = true;
            EndDrag();
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            if (e.Pointer == _pointer) EndDrag();
            base.OnPointerCaptureLost(e);
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            EndDrag();
            base.OnDetachedFromVisualTree(e);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == IsEffectivelyEnabledProperty && !IsEffectivelyEnabled) EndDrag();
        }

        private void EndDrag()
        {
            IPointer? pointer = _pointer;
            Slider? slider = _slider;
            _pointer = null;
            _slider = null;
            if (slider != null) ((IPseudoClasses)slider.Classes).Set(":pressed", false);
            if (pointer?.Captured == this) pointer.Capture(null);
        }

        private static double SnapToTick(Slider slider, double value)
        {
            double lower = slider.Minimum;
            double upper = slider.Maximum;
            if (slider.Ticks is { Count: > 0 } ticks)
            {
                foreach (double tick in ticks)
                {
                    if (tick == value) return value;
                    if (tick < value && tick > lower) lower = tick;
                    if (tick > value && tick < upper) upper = tick;
                }
            }
            else if (slider.TickFrequency > 0 && double.IsFinite(slider.TickFrequency))
            {
                lower += Math.Floor((value - lower) / slider.TickFrequency) * slider.TickFrequency;
                upper = Math.Min(slider.Maximum, lower + slider.TickFrequency);
            }
            return value - lower < upper - value ? lower : upper;
        }
    }
}
