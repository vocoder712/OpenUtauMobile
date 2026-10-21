using System;
using Avalonia;
using Avalonia.Input;
using Avalonia.Threading;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Controls;

public partial class PhonemeEditPopup : PopupDialogControl
{
    private PhonemeEditViewModel? _viewModel;
    public PhonemeEditPopup()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel != null) _viewModel.FocusRequested -= FocusInput;
        _viewModel = DataContext as PhonemeEditViewModel;
        if (_viewModel != null) _viewModel.FocusRequested += FocusInput;
    }

    /// <summary>从错误详情返回编辑时恢复输入焦点，沿用原有别名草稿。</summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        OnDataContextChanged(this, EventArgs.Empty);
        Dispatcher.UIThread.Post(FocusInput, DispatcherPriority.Loaded);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_viewModel != null) _viewModel.FocusRequested -= FocusInput;
        _viewModel = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void FocusInput()
    {
        PhonemeAliasInput.Focus();
        PhonemeAliasInput.SelectAll();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Escape && DataContext is PhonemeEditViewModel vmEscape)
        {
            vmEscape.CancelCommand.Execute().Subscribe();
            e.Handled = true;
        }
    }
}
