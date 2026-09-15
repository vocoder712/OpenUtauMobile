using System.Reactive;
using ReactiveUI;
using OpenUtau.Core;

namespace OpenUtauMobile.ViewModels;

public enum EditorMoreAction
{
    None, // 无操作
    ImportAudio, // 导入音频
    ImportTrack, // 导入轨道
    ExportAudio, // 导出音频
    SaveAs, // 另存为
    Undo,
    Redo
}

public class EditorMoreViewModel : PopupViewModelBase
{
    public bool CanUndo => !DocManager.Inst.HasOpenUndoGroup && DocManager.Inst.GetUndoState(out _);
    public bool CanRedo => !DocManager.Inst.HasOpenUndoGroup && DocManager.Inst.GetRedoState(out _);

    public ReactiveCommand<EditorMoreAction, Unit> ConfirmCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    public EditorMoreViewModel()
    {
        ConfirmCommand = ReactiveCommand.Create<EditorMoreAction>(action => { RaiseClose(action); });

        CancelCommand = ReactiveCommand.Create(() => { RaiseClose(EditorMoreAction.None); });
    }
}
