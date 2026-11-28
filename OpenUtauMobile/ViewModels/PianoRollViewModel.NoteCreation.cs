using System;
using System.Windows.Input;
using Avalonia;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using OpenUtauMobile.Controls;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services.Dialogs;
using ReactiveUI;

namespace OpenUtauMobile.ViewModels;

public partial class PianoRollViewModel
{
    private UNote? _pendingNoteCreation;

    /// <summary>当前界面是否使用占位框创建音符；未设置时移动界面开启、桌面界面关闭。</summary>
    public bool UseNoteCreationPreview => Preferences.Default.PianoRollNoteCreationPreview ?? !UseDesktopMouseInput;

    /// <summary>音符圆角半径设置的上限（像素）。</summary>
    public const int NoteCornerRadiusMaximum = 12;

    /// <summary>音符间距设置的上限（像素）。</summary>
    public const int NoteVerticalInsetMaximum = 8;

    /// <summary>未设置音符间距时的界面默认值。</summary>
    public static int DefaultNoteVerticalInset(bool desktop) => desktop ? 0 : 4;

    /// <summary>音符主体及新建占位框上下各内缩的像素。</summary>
    public double NoteVerticalInset => Math.Clamp(Preferences.Default.PianoRollNoteVerticalInset ?? DefaultNoteVerticalInset(UseDesktopMouseInput), 0, NoteVerticalInsetMaximum);

    /// <summary>未设置音符圆角时的界面默认值。</summary>
    public static int DefaultNoteCornerRadius(bool desktop) => desktop ? 4 : 8;

    /// <summary>音符及新建占位框的圆角半径（像素）。</summary>
    public double NoteCornerRadiusValue => Math.Clamp(Preferences.Default.PianoRollNoteCornerRadius ?? DefaultNoteCornerRadius(UseDesktopMouseInput), 0, NoteCornerRadiusMaximum);

    /// <summary>音符及新建占位框的圆角。</summary>
    public CornerRadius NoteCornerRadius => new(NoteCornerRadiusValue);

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
            double inset = NoteVerticalInset;
            return new Rect(origin.X + 1, origin.Y + inset, Math.Max(1, size.Width - 2), Math.Max(1, size.Height - inset * 2));
        }
    }

    /// <summary>确认占位框，在同一位置创建音符。</summary>
    public ICommand ConfirmNoteCreationCommand { get; private set; } = null!;

    /// <summary>建立占位框的命令及视口坐标更新订阅。</summary>
    private void InitializeNoteCreationPreview()
    {
        ConfirmNoteCreationCommand = ReactiveCommand.Create(ConfirmNoteCreation);
        MessageBus.Current.Listen<NoteAppearanceChangedEvent>()
            .Subscribe(_ =>
            {
                this.RaisePropertyChanged(nameof(NoteCornerRadius));
                if (IsNoteCreationPreviewVisible) this.RaisePropertyChanged(nameof(NoteCreationPreviewRect));
                RequestInvalidateVisual?.Invoke();
            })
            .DisposeWith(_disposables);
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

    /// <summary>关闭占位框时的旧行为：在点击位置直接创建音符并选中、试听。</summary>
    private void CreateNoteAt(Point point)
    {
        ShowNoteCreationPreview(point);
        ConfirmNoteCreation();
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
