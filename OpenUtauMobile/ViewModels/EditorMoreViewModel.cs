using System.Reactive;
using ReactiveUI;

namespace OpenUtauMobile.ViewModels;

public enum EditorMoreAction
{
    None, // 无操作
    ImportAudio, // 导入音频
    ImportMidi, // 导入MIDI
    ImportTrack, // 导入轨道
    ExportAudio, // 导出音频
    SaveAs // 另存为
}

public class EditorMoreViewModel : PopupViewModelBase
{
    public ReactiveCommand<EditorMoreAction, Unit> ConfirmCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    public EditorMoreViewModel()
    {
        ConfirmCommand = ReactiveCommand.Create<EditorMoreAction>(action => { RaiseClose(action); });

        CancelCommand = ReactiveCommand.Create(() => { RaiseClose(EditorMoreAction.None); });
    }
}