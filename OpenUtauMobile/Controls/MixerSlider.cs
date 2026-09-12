using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace OpenUtauMobile.Controls;

/// <summary>混音参数滑块，沿用普通 Slider 主题，仅增加双击复位手势。</summary>
public class MixerSlider : Slider
{
    public static readonly StyledProperty<double> DefaultValueProperty =
        AvaloniaProperty.Register<MixerSlider, double>(nameof(DefaultValue));

    public double DefaultValue
    {
        get => GetValue(DefaultValueProperty);
        set => SetValue(DefaultValueProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(Slider);
    private IPointer? _resetPointer;

    public MixerSlider()
    {
        // 在模板中的 Thumb 和轨道按钮处理按下之前拦截第二次点击。
        AddHandler(PointerPressedEvent, OnResetPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnResetReleased, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnResetMoved, RoutingStrategies.Tunnel);
    }

    private void OnResetPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount != 2 ||
            (e.Pointer.Type != PointerType.Touch && !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)) return;
        SetCurrentValue(ValueProperty, DefaultValue);
        _resetPointer = e.Pointer;
        e.Handled = true;
        e.Pointer.Capture(this);
    }

    private void OnResetMoved(object? sender, PointerEventArgs e)
    {
        if (e.Pointer == _resetPointer) e.Handled = true;
    }

    private void OnResetReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.Pointer != _resetPointer) return;
        _resetPointer = null;
        e.Handled = true;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        _resetPointer = null;
        base.OnPointerCaptureLost(e);
    }
}
