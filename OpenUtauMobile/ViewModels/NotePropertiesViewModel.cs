using System;
using System.Collections.Generic;
using System.Reactive;
using OpenUtau.Core.Ustx;
using ReactiveUI;

namespace OpenUtauMobile.ViewModels;

/// <summary>移动弹窗保留整份草稿的确认与取消；桌面可独立提交同一编辑模型。</summary>
public sealed class NotePropertiesViewModel : PopupViewModelBase, IDisposable
{
    public NotePropertyEditor Editor { get; }
    public List<NotePropertyGroup> Groups => Editor.Groups;
    public string Summary => Editor.Summary;
    public string Error => Editor.Error;
    public int SelectedTabIndex { get => Editor.SelectedTabIndex; set => Editor.SelectedTabIndex = value; }
    public ReactiveCommand<Unit, Unit> ApplyCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    public NotePropertiesViewModel(UVoicePart part, IReadOnlyCollection<UNote> selectedNotes)
    {
        Editor = new NotePropertyEditor(part, selectedNotes);
        Editor.PropertyChanged += OnEditorChanged;
        ApplyCommand = ReactiveCommand.Create(() => { if (Editor.Commit()) RaiseClose(null); });
        CancelCommand = ReactiveCommand.Create(() => RaiseClose(null));
    }
    private void OnEditorChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => this.RaisePropertyChanged(e.PropertyName);
    public void Dispose()
    {
        Editor.PropertyChanged -= OnEditorChanged;
        Editor.Dispose();
        ApplyCommand.Dispose();
        CancelCommand.Dispose();
    }
}