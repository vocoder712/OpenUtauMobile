using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Controls;

public partial class PhonemeEditPopup : UserControl
{
    public PhonemeEditPopup()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is PhonemeEditViewModel vm)
        {
            vm.FocusRequested += FocusInput;
            Dispatcher.UIThread.Post(FocusInput, DispatcherPriority.Loaded);
        }
    }

    private void FocusInput()
    {
        PhonemeAliasInput.Focus();
        PhonemeAliasInput.SelectAll();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Enter && DataContext is PhonemeEditViewModel vm)
        {
            vm.ConfirmCommand.Execute().Subscribe();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && DataContext is PhonemeEditViewModel vmEscape)
        {
            vmEscape.CancelCommand.Execute().Subscribe();
            e.Handled = true;
        }
    }
}
