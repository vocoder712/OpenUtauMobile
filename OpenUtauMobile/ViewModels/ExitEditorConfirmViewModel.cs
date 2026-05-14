using System.Reactive;
using ReactiveUI;

namespace OpenUtauMobile.ViewModels;

/// <summary>
/// 退出编辑确认弹窗返回寄存器（2 位）。
/// </summary>
public enum ExitEditorActionRegister : byte
{
    Cancel = 0b00,
    ExitWithoutSave = 0b01,
    SaveAndExit = 0b10,
}

public class ExitEditorConfirmViewModel : PopupViewModelBase
{
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }
    public ReactiveCommand<Unit, Unit> ExitWithoutSaveCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveAndExitCommand { get; }

    public ExitEditorConfirmViewModel()
    {
        CancelCommand = ReactiveCommand.Create(() => RaiseClose((byte)ExitEditorActionRegister.Cancel));
        ExitWithoutSaveCommand = ReactiveCommand.Create(() => RaiseClose((byte)ExitEditorActionRegister.ExitWithoutSave));
        SaveAndExitCommand = ReactiveCommand.Create(() => RaiseClose((byte)ExitEditorActionRegister.SaveAndExit));
    }

    public override void RequestBack()
    {
        RaiseClose((byte)ExitEditorActionRegister.Cancel);
    }
}
