using OpenUtau.Core;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services.Dialogs;

namespace OpenUtauMobile.Services;

/// <summary>统一按钮和手势的历史操作提示，不改变工程命令或历史栈。</summary>
public static class UndoRedoService
{
    public static void Undo() => Apply(false);
    public static void Redo() => Apply(true);

    private static void Apply(bool redo)
    {
        DocManager manager = DocManager.Inst;
        if (manager.HasOpenUndoGroup) return;
        bool available = redo ? manager.GetRedoState(out _) : manager.GetUndoState(out _);
        if (!available) return;
        if (redo) manager.Redo();
        else manager.Undo();
        if (ServiceHub.DesktopWindowFactory == null)
            ToastService.Enqueue(L.S(redo ? "Editor.Redone" : "Editor.Undone"), 1000);
    }
}
