using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using OpenUtauMobile.ViewModels;

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
    private MixerViewModel? _editingMixer;

    public MixerSlider()
    {
        // 在模板中的 Thumb 和轨道按钮处理按下之前拦截第二次点击。
        AddHandler(PointerPressedEvent, OnResetPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnResetReleased, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnResetMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, (_, _) => EndEdit(), RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown) BeginEdit();
        }, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, (_, _) => EndEdit(), RoutingStrategies.Bubble, handledEventsToo: true);
        LostFocus += (_, _) => EndEdit();
    }

    private void OnResetPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Pointer.Type == PointerType.Touch || e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginEdit();
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
        EndEdit();
        base.OnPointerCaptureLost(e);
    }

    private void BeginEdit()
    {
        _editingMixer ??= this.FindAncestorOfType<MixerPanel>()?.ViewModel;
        _editingMixer?.BeginEdit();
    }

    private void EndEdit()
    {
        _editingMixer?.EndEdit();
        _editingMixer = null;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        EndEdit();
        base.OnDetachedFromVisualTree(e);
    }
}
