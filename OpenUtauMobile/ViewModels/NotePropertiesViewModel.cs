using System;
using System.Collections.Generic;
using System.Reactive;
using OpenUtau.Core.Ustx;
using ReactiveUI;

namespace OpenUtauMobile.ViewModels;

/// <summary>移动弹窗保留整份草稿的确认与取消；桌面可独立提交同一编辑模型。</summary>
public sealed class NotePropertiesViewModel : PopupViewModelBase, IDisposable
{
    private readonly List<(NotePropertyField Field, decimal? Number, string? Text, bool Edited)> pickerInputs = [];
    private bool pickerRequested;
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
    public void RequestPhonemizerPicker()
    {
        if (pickerRequested || Editor.PhonemizerField is not { IsEnabled: true } field) return;
        pickerRequested = true;
        // 切换弹窗只同步输入草稿，不执行工程命令。
        foreach (NotePropertyGroup group in Groups)
        {
            foreach (NotePropertyField input in group.Fields)
            {
                if (!input.HasInvalidInput) input.CommitInput();
            }
            if (group.Vibrato?.Duration is { HasInvalidInput: false } duration) duration.CommitInput();
        }
        pickerInputs.Clear();
        foreach (NotePropertyGroup group in Groups)
        {
            foreach (NotePropertyField input in group.Fields)
                if (input.IsNumber) pickerInputs.Add((input, input.Number, input.NumberText, input.IsEdited));
            if (group.Vibrato?.Duration is { } duration)
                pickerInputs.Add((duration, duration.Number, duration.NumberText, duration.IsEdited));
        }
        RaiseClose(field);
    }

    public void RestorePickerInputs()
    {
        // 数值控件重新进入可视树后可能格式化文本，恢复切换前的完整输入状态。
        foreach ((NotePropertyField field, decimal? number, string? text, bool edited) in pickerInputs)
            field.RestoreNumberInput(number, text, edited);
        pickerInputs.Clear();
        pickerRequested = false;
    }
    public void Dispose()
    {
        pickerInputs.Clear();
        Editor.PropertyChanged -= OnEditorChanged;
        Editor.Dispose();
        ApplyCommand.Dispose();
        CancelCommand.Dispose();
    }
}
