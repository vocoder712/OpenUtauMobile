using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Controls;

public partial class LyricEditPopup : PopupDialogControl
{
    private LyricEditViewModel? _viewModel;

    protected override PopupDialogWidthPreset WidthPreset => PopupDialogWidthPreset.Compact;

    public LyricEditPopup()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is LyricEditViewModel vm)
        {
            _viewModel = vm;

            // 订阅焦点请求事件：仅在加载新歌词时触发，不涉及用户输入
            vm.FocusRequested += OnFocusRequested;

            // 立即调用一次，处理初始化时的焦点请求
            // 因为 ViewModel 构造函数中已经调用了 LoadCurrentNote()，触发了 FocusRequested
            // 但此时还没有订阅事件，所以需要手动调用一次
            OnFocusRequested();
        }
    }

    /// <summary>
    /// 焦点请求的处理：获取焦点并全选文本
    /// </summary>
    private void OnFocusRequested()
    {
        // 在下一帧执行，确保控件已完全加载
        Dispatcher.UIThread.InvokeAsync(() => { FocusAndSelectInputBox(); });
    }

    /// <summary>
    /// 将焦点设置到歌词输入框并全选文本
    /// </summary>
    private void FocusAndSelectInputBox()
    {
        TextBox? inputBox = this.FindControl<TextBox>("LyricInput");
        if (inputBox != null)
        {
            inputBox.Focus();
            inputBox.SelectAll();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        // 在 View 卸载时取消事件订阅
        if (_viewModel != null)
        {
            _viewModel.FocusRequested -= OnFocusRequested;
        }
    }
}