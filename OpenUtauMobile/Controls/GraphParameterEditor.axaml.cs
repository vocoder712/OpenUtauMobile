using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Controls;

/// <summary>标准参数控件组合；滑块的预览、提交和取消只影响字段缓冲。</summary>
public partial class GraphParameterEditor : UserControl
{
    private bool _releasing;
    private bool _keyboard;
    private GraphParameterEdit? Field => DataContext as GraphParameterEdit;

    /// <summary>复用标准输入主题，捕获丢失与离页均取消尚未提交的滑块预览。</summary>
    public GraphParameterEditor()
    {
        InitializeComponent();
        ParameterSlider.AddHandler(PointerPressedEvent, (_, _) =>
        {
            _releasing = false; Field?.BeginSlider();
        }, RoutingStrategies.Tunnel, true);
        ParameterSlider.AddHandler(PointerReleasedEvent, (_, _) =>
        {
            _releasing = true;
        }, RoutingStrategies.Tunnel, true);
        ParameterSlider.AddHandler(PointerReleasedEvent, (_, _) =>
        {
            // 等标准滑块处理完释放后再提交，正常释放捕获不会被当作系统取消。
            try { Field?.EndSlider(false); }
            finally { _releasing = false; }
        }, RoutingStrategies.Bubble, true);
        ParameterSlider.PointerCaptureLost += (_, _) => { if (!_releasing) Field?.EndSlider(true); };
        ParameterSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty && Field?.IsSliding == true) Field.SliderValue = ParameterSlider.Value;
        };
        ParameterSlider.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown)
            {
                _keyboard = true; Field?.BeginSlider();
            }
        }, RoutingStrategies.Tunnel, true);
        ParameterSlider.AddHandler(KeyUpEvent, (_, _) =>
        {
            if (!_keyboard) return;
            _keyboard = false; Field?.EndSlider(false);
        }, RoutingStrategies.Bubble, true);
        ParameterSlider.LostFocus += (_, _) => { _keyboard = false; Field?.EndSlider(true); };
    }

    private void TextKeyDown(object? sender, KeyEventArgs e)
    {
        if (Field == null) return;
        if (e.Key == Key.Enter) { Field.ApplyCommand.Execute().Subscribe(); e.Handled = true; }
        if (e.Key == Key.Escape) { Field.CancelCommand.Execute().Subscribe(); e.Handled = true; }
    }

    /// <summary>校验失败时滚到字段，保持错误和取消动作可访问。</summary>
    public void Reveal()
    {
        Control? input = Field?.IsChoice == true ? this.FindControl<ComboBox>("ParameterChoice")
            : Field?.IsBool == true ? this.FindControl<CheckBox>("ParameterBool") : this.FindControl<TextBox>("ParameterText");
        input?.Focus(); this.BringIntoView();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Field?.EndSlider(true); _keyboard = false; _releasing = false;
        base.OnDetachedFromVisualTree(e);
    }
}
