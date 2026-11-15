using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using DynamicData;
using OpenUtau.Core.Ustx;
using IconPacks.Avalonia.PhosphorIcons;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Themes.OpenUtauMobile.Controls;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtauMobile.ViewModels;

public partial class PianoRollViewModel
{
    /// <summary>音符笔模式的浮动操作，批量编辑歌词始终位于溢出面板。</summary>
    [Reactive]
    public IReadOnlyList<FloatingToolbarAction> NoteToolbarActions { get; private set; } = [];

    /// <summary>最后加入选区的音符；拖拽时内部重排集合不改变工具栏的锚点。</summary>
    public UNote? NoteToolbarAnchorNote { get; private set; }

    /// <summary>跟踪实际新增选择，集合重置时优先保留仍在选区中的锚点。</summary>
    private void UpdateNoteToolbarSelection(NotifyCollectionChangedEventArgs change)
    {
        if (change.NewItems is { Count: > 0 } added)
        {
            NoteToolbarAnchorNote = (UNote)added[^1]!;
        }
        else
        {
            UNote? newlySelected = SelectedNotes.LastOrDefault(note => !_selectedNoteLookup.Contains(note));
            if (newlySelected != null) NoteToolbarAnchorNote = newlySelected;
            else if (NoteToolbarAnchorNote == null || !SelectedNotes.Contains(NoteToolbarAnchorNote))
            {
                NoteToolbarAnchorNote = SelectedNotes.LastOrDefault();
            }
        }
    }

    /// <summary>选择或模式变化时更新操作；与侧栏共用现有编辑命令及撤销事务。</summary>
    private void RebuildNoteToolbarActions()
    {
        if (EditMode != PianoRollEditMode.Note || SelectedNotes.Count == 0 || EditingVoicePart == null)
        {
            NoteToolbarActions = [];
            return;
        }
        NoteToolbarActions =
        [
            new FloatingToolbarAction
            {
                Text = L.S("Common.Delete"),
                Icon = PackIconPhosphorIconsKind.Trash,
                Command = ReactiveCommand.Create(DeleteSelectedNotes)
            },
            new FloatingToolbarAction
            {
                Text = L.S("Common.Copy"),
                Icon = PackIconPhosphorIconsKind.Copy,
                Command = ReactiveCommand.Create(CopySelectedNotes)
            },
            new FloatingToolbarAction
            {
                Text = L.S("LyricEdit.Title"),
                Icon = PackIconPhosphorIconsKind.PencilSimple,
                Command = ReactiveCommand.Create(EditLastSelectedNoteLyric)
            },
            new FloatingToolbarAction
            {
                Text = L.S("PianoRoll.Action.Split"),
                Icon = PackIconPhosphorIconsKind.SplitHorizontal,
                Command = ReactiveCommand.Create(SplitSelectedNotes)
            },
            new FloatingToolbarAction
            {
                Text = L.S("BulkLyricEdit.Title"),
                Icon = PackIconPhosphorIconsKind.TextAlignLeft,
                Command = ReactiveCommand.CreateFromTask(ShowBulkLyricEditPopupAsync),
                AlwaysInOverflow = true
            }
        ];
    }

    /// <summary>编辑最后一个选中音符的歌词，沿用双击音符的弹窗流程。</summary>
    private void EditLastSelectedNoteLyric()
    {
        RequestEditLyric?.Invoke(EditingVoicePart!, EditingVoicePart!.notes.IndexOf(NoteToolbarAnchorNote!));
    }
}
