using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using DynamicData;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using IconPacks.Avalonia.PhosphorIcons;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Themes.OpenUtauMobile.Controls;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtauMobile.ViewModels;

public partial class PianoRollViewModel
{
    /// <summary>音符、锚点选区及新建占位框共用的浮动操作；属性及批量编辑歌词始终收纳。</summary>
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

    /// <summary>选择或模式变化时更新操作，复用现有命令及撤销事务。</summary>
    private void RebuildNoteToolbarActions()
    {
        if (EditingVoicePart == null)
        {
            NoteToolbarActions = [];
            return;
        }
        if (EditMode == PianoRollEditMode.Note && IsNoteCreationPreviewVisible)
        {
            NoteToolbarActions = DocManager.Inst.NotesClipboard is { Count: > 0 }
                ? [new FloatingToolbarAction
                {
                    Text = L.S("Common.Paste"),
                    Icon = PackIconPhosphorIconsKind.Clipboard,
                    Command = ReactiveCommand.Create(PasteNotesAtCreationPreview),
                    HoldCommand = ReactiveCommand.Create(ClearNotesClipboard)
                }]
                : [];
            return;
        }
        if (EditMode == PianoRollEditMode.Anchor)
        {
            RebuildAnchorToolbarActions();
            return;
        }
        if (EditMode is not (PianoRollEditMode.Note or PianoRollEditMode.MultiSelect) || SelectedNotes.Count == 0)
        {
            NoteToolbarActions = [];
            return;
        }
        List<FloatingToolbarAction> actions =
        [
            new FloatingToolbarAction
            {
                Text = L.S("Common.SelectAll"),
                Icon = PackIconPhosphorIconsKind.SelectionAll,
                Command = ReactiveCommand.Create(SelectAllNotes)
            },
            new FloatingToolbarAction
            {
                Text = L.S("Common.Copy"),
                Icon = PackIconPhosphorIconsKind.Copy,
                Command = ReactiveCommand.Create(CopySelectedNotes)
            },
            new FloatingToolbarAction
            {
                Text = L.S("Common.Delete"),
                Icon = PackIconPhosphorIconsKind.Trash,
                Command = ReactiveCommand.Create(DeleteSelectedNotes),
                IsDanger = true
            },
            new FloatingToolbarAction
            {
                Text = L.S("Common.Cut"),
                Icon = PackIconPhosphorIconsKind.Scissors,
                Command = ReactiveCommand.Create(CutSelectedNotes)
            },
            new FloatingToolbarAction
            {
                Text = L.S("PianoRoll.Action.Split"),
                Icon = PackIconPhosphorIconsKind.SplitHorizontal,
                Command = ReactiveCommand.Create(SplitSelectedNotes)
            }
        ];
        if (EditMode == PianoRollEditMode.Note)
        {
            actions.Add(new FloatingToolbarAction
            {
                Text = L.S("LyricEdit.Title"),
                Icon = PackIconPhosphorIconsKind.PencilSimple,
                Command = ReactiveCommand.Create(EditLastSelectedNoteLyric)
            });
        }
        actions.Add(new FloatingToolbarAction
        {
            Text = L.S("BulkLyricEdit.Title"),
            Icon = PackIconPhosphorIconsKind.TextAlignLeft,
            Command = ReactiveCommand.CreateFromTask(ShowBulkLyricEditPopupAsync),
            AlwaysInOverflow = true
        });
        actions.Add(new FloatingToolbarAction
        {
            Text = L.S("PianoRoll.Action.Properties"),
            Icon = PackIconPhosphorIconsKind.Wrench,
            Command = ReactiveCommand.CreateFromTask(EditNotePropertiesAsync),
            AlwaysInOverflow = true
        });
        NoteToolbarActions = actions;
    }

    /// <summary>锚点选区承载形状切换与删除，仅单独选择首锚点时提供吸附。</summary>
    private void RebuildAnchorToolbarActions()
    {
        if (SelectedAnchors.Count == 0)
        {
            NoteToolbarActions = [];
            return;
        }
        List<FloatingToolbarAction> actions =
        [
            new FloatingToolbarAction
            {
                Text = L.S("PianoRoll.Action.ControlPointShape"),
                Icon = PackIconPhosphorIconsKind.BezierCurve,
                Command = ReactiveCommand.Create(CyclePitchPointShape)
            },
            new FloatingToolbarAction
            {
                Text = L.S("Common.Delete"),
                Icon = PackIconPhosphorIconsKind.Trash,
                Command = ReactiveCommand.Create(DeleteSelectedAnchors),
                IsDanger = true
            }
        ];
        if (GetSingleSelectedFirstAnchorNote() is { } note)
        {
            actions.Add(new FloatingToolbarAction
            {
                Text = L.S("PianoRoll.Action.ToggleFirstAnchorSnap"),
                Icon = note.pitch.snapFirst ? PackIconPhosphorIconsKind.MagnetFill : PackIconPhosphorIconsKind.Magnet,
                Command = ReactiveCommand.Create(ToggleFirstAnchorSnap)
            });
        }
        NoteToolbarActions = actions;
    }

    /// <summary>将当前选区的工具栏锚点映射为音符画布坐标，锚点模式使用最后选中的控制点。</summary>
    public Rect? GetSelectionToolbarAnchorRect()
    {
        if (EditingVoicePart == null || NoteToolbarActions.Count == 0) return null;
        if (IsNoteCreationPreviewVisible) return NoteCreationPreviewRect;
        if (EditMode == PianoRollEditMode.Anchor)
        {
            PitchPoint? anchor = SelectedAnchors.LastOrDefault();
            if (anchor == null) return null;
            UNote? owner = SelectedNotes.FirstOrDefault(candidate => candidate.pitch.data.Contains(anchor)) ?? FindNoteForPitchPoint(anchor);
            return owner == null ? null : new Rect(GetPitchPointCanvasPoint(owner, anchor), default(Size));
        }
        if (EditMode is not (PianoRollEditMode.Note or PianoRollEditMode.MultiSelect) || NoteToolbarAnchorNote is not { } note) return null;
        return new Rect(TickPitchToPoint(EditingVoicePart.position + note.position, note.AdjustedTone), TickToneToSize(note.duration, 1));
    }

    /// <summary>编辑最后一个选中音符的歌词，沿用双击音符的弹窗流程。</summary>
    private void EditLastSelectedNoteLyric()
    {
        RequestEditLyric?.Invoke(EditingVoicePart!, EditingVoicePart!.notes.IndexOf(NoteToolbarAnchorNote!));
    }
}
