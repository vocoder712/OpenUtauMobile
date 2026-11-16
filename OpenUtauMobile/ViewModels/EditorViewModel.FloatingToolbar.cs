using System.Collections.Generic;
using System.Linq;
using Avalonia;
using IconPacks.Avalonia.PhosphorIcons;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Themes.OpenUtauMobile.Controls;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtauMobile.ViewModels;

public partial class EditorViewModel
{
    /// <summary>普通与多选模式共用的分片选区操作，按数量和分片类型提供适用命令。</summary>
    [Reactive]
    public IReadOnlyList<FloatingToolbarAction> PartToolbarActions { get; private set; } = [];

    /// <summary>普通模式下由再次点击已选分片控制展开，不随长期选中状态常驻显示。</summary>
    [Reactive]
    public bool IsPartToolbarExpanded { get; private set; }

    /// <summary>再次点击已选分片时切换工具栏；选择与拖动逻辑独立保持。</summary>
    public void TogglePartToolbar()
    {
        if (TrackEditMode == TrackEditMode.Normal && SelectedParts.Count > 0)
        {
            IsPartToolbarExpanded = !IsPartToolbarExpanded;
        }
    }

    /// <summary>选区变化时重建操作；布局与溢出交给共用浮动工具栏。</summary>
    private void RebuildPartToolbarActions()
    {
        if (SelectedParts.Count == 0)
        {
            PartToolbarActions = [];
            return;
        }
        List<FloatingToolbarAction> actions =
        [
            new()
            {
                Text = L.S("Common.Delete"),
                Icon = PackIconPhosphorIconsKind.Trash,
                Command = ReactiveCommand.Create(DeleteSelectedParts),
                IsDanger = true
            }
        ];
        if (SelectedParts.Count == 1)
        {
            actions.Add(new FloatingToolbarAction
            {
                Text = L.S("Editor.Action.Rename"),
                Icon = PackIconPhosphorIconsKind.Textbox,
                Command = ReactiveCommand.CreateFromTask(RenameSelectedPart)
            });
        }
        actions.AddRange([
            new()
            {
                Text = L.S("Common.SelectAll"),
                Icon = PackIconPhosphorIconsKind.SelectionAll,
                Command = ReactiveCommand.Create(SelectAllParts)
            },
            new()
            {
                Text = L.S("Common.Copy"),
                Icon = PackIconPhosphorIconsKind.Copy,
                Command = ReactiveCommand.Create(CopySelectedParts)
            },
            new()
            {
                Text = L.S("Common.Cut"),
                Icon = PackIconPhosphorIconsKind.Scissors,
                Command = ReactiveCommand.Create(CutSelectedParts)
            }
        ]);
        if (SelectedParts.Any(part => part is UVoicePart))
        {
            actions.Add(new FloatingToolbarAction
            {
                Text = L.S("Editor.Action.Split"),
                Icon = PackIconPhosphorIconsKind.SplitHorizontal,
                Command = ReactiveCommand.CreateFromTask(SplitSelectedParts)
            });
        }
        if (CanMergeSelectedParts)
        {
            actions.Add(new FloatingToolbarAction
            {
                Text = L.S("Editor.Action.Merge"),
                Icon = PackIconPhosphorIconsKind.Unite,
                Command = ReactiveCommand.Create(MergeSelectedParts)
            });
        }
        if (SelectedParts is [UWavePart wavePart])
        {
            actions.AddRange([
                new()
                {
                    Text = L.S("Editor.Action.ReplaceAudio"),
                    Icon = PackIconPhosphorIconsKind.ArrowsClockwise,
                    Command = ReactiveCommand.CreateFromTask(ReplaceSelectedAudioAsync)
                },
                new()
                {
                    Text = L.S("NoteExtraction.Title"),
                    Icon = PackIconPhosphorIconsKind.MusicNotes,
                    Command = ReactiveCommand.CreateFromTask(() => noteExtraction.RunAsync(wavePart))
                }
            ]);
        }
        PartToolbarActions = actions;
    }

    /// <summary>分片移动使用独立快照，不重排选区，因此集合末项始终是最后选中的分片。</summary>
    public Rect? GetPartToolbarAnchorRect()
    {
        if (PartToolbarActions.Count == 0 || SelectedParts.Count == 0 ||
            TrackEditMode == TrackEditMode.Normal && !IsPartToolbarExpanded) return null;
        UPart part = SelectedParts[^1];
        return new Rect(TickToCanvasX(part.position), TrackNoToCanvasY(part.trackNo), part.Duration * TickWidth, TrackHeight);
    }
}
