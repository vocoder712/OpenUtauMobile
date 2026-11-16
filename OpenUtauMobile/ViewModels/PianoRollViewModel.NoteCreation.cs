using System;
using System.Windows.Input;
using Avalonia;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services.Dialogs;
using ReactiveUI;

namespace OpenUtauMobile.ViewModels;

public partial class PianoRollViewModel
{
    private UNote? _pendingNoteCreation;

    /// <summary>点击空白区域后显示待确认的新音符。</summary>
    public bool IsNoteCreationPreviewVisible => _pendingNoteCreation != null;

    /// <summary>占位框使用音符主体的画布位置和尺寸，随视口变换更新。</summary>
    public Rect NoteCreationPreviewRect
    {
        get
        {
            if (_pendingNoteCreation is not { } note || EditingVoicePart == null) return default;
            Point origin = TickPitchToPoint(EditingVoicePart.position + note.position, note.AdjustedTone);
            Size size = TickToneToSize(note.duration, 1);
            return new Rect(origin.X + 1, origin.Y + 4, Math.Max(1, size.Width - 2), Math.Max(1, size.Height - 8));
        }
    }

    /// <summary>确认占位框，在同一位置创建音符。</summary>
    public ICommand ConfirmNoteCreationCommand { get; private set; } = null!;

    /// <summary>建立占位框的命令及视口坐标更新订阅。</summary>
    private void InitializeNoteCreationPreview()
    {
        ConfirmNoteCreationCommand = ReactiveCommand.Create(ConfirmNoteCreation);
        this.WhenAnyValue(model => model.TickWidth, model => model.KeyHeight, model => model.TickOffset, model => model.KeyOffset)
            .Subscribe(_ =>
            {
                if (IsNoteCreationPreviewVisible) this.RaisePropertyChanged(nameof(NoteCreationPreviewRect));
            })
            .DisposeWith(_disposables);
    }

    /// <summary>每次点击空白区域都直接更新占位框，无需先关闭已有占位框。</summary>
    private void ShowNoteCreationPreview(Point point)
    {
        SelectedNotes.Clear();
        SelectedAnchors.Clear();
        if (EditingVoicePart == null) return;
        int tick = SnapToFloor(PointXToTick(point.X));
        if (tick < EditingVoicePart.position) return;
        int duration = ResolveSnapUnit();
        if (duration <= 0) duration = DocManager.Inst.Project.resolution;
        _pendingNoteCreation = DocManager.Inst.Project.CreateNote(PointYToToneInt(point.Y), tick - EditingVoicePart.position, duration);
        this.RaisePropertyChanged(nameof(NoteCreationPreviewRect));
        this.RaisePropertyChanged(nameof(IsNoteCreationPreviewVisible));
        RebuildNoteToolbarActions();
        RequestInvalidateVisual?.Invoke();
    }

    /// <summary>关闭占位框及其粘贴工具栏，不修改工程或撤销记录。</summary>
    public void DismissNoteCreationPreview()
    {
        if (_pendingNoteCreation == null) return;
        _pendingNoteCreation = null;
        this.RaisePropertyChanged(nameof(IsNoteCreationPreviewVisible));
        this.RaisePropertyChanged(nameof(NoteCreationPreviewRect));
        RebuildNoteToolbarActions();
        RequestInvalidateVisual?.Invoke();
    }

    /// <summary>确认后才执行原有的新建命令、重叠处理和试听。</summary>
    private void ConfirmNoteCreation()
    {
        if (_pendingNoteCreation is not { } note || EditingVoicePart == null || EditMode != PianoRollEditMode.Note) return;
        UVoicePart part = EditingVoicePart;
        DismissNoteCreationPreview();
        AddCreatedNote(part, note);
        PlayCreatedNotePreview(note);
        SelectedNotes.Add(note);
    }

    /// <summary>以占位框起点对齐剪贴板最早的音符，保留音高及相对时间。</summary>
    private void PasteNotesAtCreationPreview()
    {
        if (_pendingNoteCreation is not { } note || EditingVoicePart == null) return;
        int tick = EditingVoicePart.position + note.position;
        DismissNoteCreationPreview();
        PasteNotesAtTick(tick);
    }

    /// <summary>长按粘贴入口清空应用内音符剪贴板，保留占位框并收起粘贴工具栏。</summary>
    private void ClearNotesClipboard()
    {
        DocManager.Inst.NotesClipboard = null;
        RebuildNoteToolbarActions();
        ToastService.Enqueue(L.S("PianoRoll.Clipboard.Cleared"));
    }
}
