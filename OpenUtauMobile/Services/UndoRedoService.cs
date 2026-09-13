using System.Collections.Generic;
using Avalonia;
using OpenUtau.Core;
using OpenUtauMobile.Helpers;

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
        string? name;
        bool available = redo ? manager.GetRedoState(out name) : manager.GetUndoState(out name);
        if (!available)
        {
            ToastService.Enqueue(L.S(redo ? "History.NoRedo" : "History.NoUndo"));
            return;
        }
        CommandDescriptions descriptions = new();
        manager.AddSubscriber(descriptions);
        try
        {
            if (redo) manager.Redo();
            else manager.Undo();
        }
        finally { manager.RemoveSubscriber(descriptions); }
        string description = !string.IsNullOrWhiteSpace(name) ? ResolveName(name)
            : descriptions.Items.Count > 0 ? string.Join(" / ", descriptions.Items) : L.S("History.Edit");
        ToastService.Enqueue(string.Format(L.S(redo ? "History.Redone" : "History.Undone"), description));
    }

    private static string ResolveName(string name)
    {
        // 历史组既可能使用资源键，也可能直接使用中文名称。
        return Application.Current != null && Application.Current.TryGetResource(name, null, out object? value) && value is string text
            ? text : name;
    }

    private sealed class CommandDescriptions : ICmdSubscriber
    {
        public List<string> Items { get; } = [];
        public void OnNext(UCommand cmd, bool isUndo)
        {
            if (cmd is UNotification) return;
            string description = cmd.ToString();
            if (!string.IsNullOrWhiteSpace(description) && !Items.Contains(description)) Items.Add(description);
        }
    }
}
