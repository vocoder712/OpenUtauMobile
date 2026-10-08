using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Controls;

public partial class RendererPickerPopup : PopupDialogControl
{
    protected override PopupDialogWidthPreset WidthPreset => PopupDialogWidthPreset.Regular;

    public RendererPickerPopup()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is RendererPickerViewModel vm) vm.CloseDropDown = CloseDropDown;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is RendererPickerViewModel vm) vm.CloseDropDown = null;
        base.OnDetachedFromVisualTree(e);
    }

    private bool CloseDropDown()
    {
        // 系统返回先收起选择列表，下一次返回才丢弃草稿。
        foreach (ComboBox selector in new[] { RendererSelector, ResamplerSelector, WavtoolSelector })
        {
            if (!selector.IsDropDownOpen) continue;
            selector.IsDropDownOpen = false;
            return true;
        }
        return false;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!e.Handled && e.Key == Key.Escape && DataContext is RendererPickerViewModel vm)
        {
            vm.RequestBack();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }
}