using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reactive;
using IconPacks.Avalonia.PhosphorIcons;
using OpenUtau.Core.Editing;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Helpers;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtauMobile.ViewModels;

public sealed class BatchEditItemViewModel : ViewModelBase
{
    private readonly BatchEditDescriptor _descriptor;
    private readonly Action<BatchEditItemViewModel> _execute;

    public string Title => L.S(_descriptor.TitleKey);
    public BatchEditCategory Category => _descriptor.Category;
    public PackIconPhosphorIconsKind Icon => _descriptor.Icon;
    public bool HasParameter => _descriptor.ParameterKind != BatchEditParameterKind.None;
    public string ParameterLabel => HasParameter ? L.S(_descriptor.ParameterLabelKey) : string.Empty;
    public bool RequiresConfirmation => _descriptor.RequiresConfirmation;
    public bool SupportsCancellation => _descriptor.SupportsCancellation;

    [Reactive] public string ParameterValue { get; set; }
    [Reactive] public string ValidationMessage { get; set; } = string.Empty;
    [Reactive] public bool IsConfirmationPending { get; set; }
    [Reactive] public string ExecuteLabel { get; set; } = L.S("BatchEdit.Run");

    public ReactiveCommand<Unit, Unit> ExecuteCommand { get; }

    public BatchEditItemViewModel(
        BatchEditDescriptor descriptor,
        UProject project,
        Action<BatchEditItemViewModel> execute)
    {
        _descriptor = descriptor;
        _execute = execute;
        ParameterValue = descriptor.DefaultValueFactory(project);
        ExecuteCommand = ReactiveCommand.Create(() => _execute(this));
    }

    public bool TryCreate(out BatchEdit? batchEdit)
    {
        ValidationMessage = string.Empty;
        string value = ParameterValue.Trim();

        if (_descriptor.ParameterKind == BatchEditParameterKind.Text && string.IsNullOrWhiteSpace(value))
        {
            ValidationMessage = L.S("BatchEdit.Validation.Required");
            batchEdit = null;
            return false;
        }

        if (_descriptor.ParameterKind == BatchEditParameterKind.Integer)
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int integerValue) ||
                !IsWithinRange(integerValue))
            {
                ValidationMessage = L.S("BatchEdit.Validation.Number");
                batchEdit = null;
                return false;
            }
        }

        if (_descriptor.ParameterKind == BatchEditParameterKind.Decimal)
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double decimalValue) ||
                !IsWithinRange(decimalValue))
            {
                ValidationMessage = L.S("BatchEdit.Validation.Number");
                batchEdit = null;
                return false;
            }
        }

        batchEdit = _descriptor.Factory(value);
        return true;
    }

    private bool IsWithinRange(double value)
    {
        return (!_descriptor.Minimum.HasValue || value >= _descriptor.Minimum.Value) &&
               (!_descriptor.Maximum.HasValue || value <= _descriptor.Maximum.Value);
    }
}

public sealed record BatchEditExecutionRequest(
    BatchEdit Operation,
    string Title,
    IReadOnlyList<UNote> TargetNotes,
    bool SupportsCancellation);

public sealed class BatchEditViewModel : PopupViewModelBase
{
    private readonly UVoicePart _part;
    private readonly List<UNote> _selectedNotes;
    private readonly bool _usesSelection;

    public IReadOnlyList<BatchEditItemViewModel> LyricActions { get; }
    public IReadOnlyList<BatchEditItemViewModel> NoteActions { get; }
    public IReadOnlyList<BatchEditItemViewModel> ResetActions { get; }
    public string ScopeText { get; }

    public ReactiveCommand<Unit, Unit> CloseCommand { get; }

    public BatchEditViewModel(UProject project, UVoicePart part, IReadOnlyCollection<UNote> selectedNotes)
    {
        _part = part;

        List<UNote> selectedSnapshot = selectedNotes
            .Where(part.notes.Contains)
            .OrderBy(note => note.position)
            .ToList();
        _selectedNotes = selectedSnapshot;
        _usesSelection = selectedSnapshot.Count > 0;

        string scopeKey = _usesSelection
            ? "BatchEdit.Scope.Selected"
            : "BatchEdit.Scope.Part";
        int targetCount = _usesSelection ? _selectedNotes.Count : part.notes.Count;
        ScopeText = string.Format(CultureInfo.CurrentCulture, L.S(scopeKey), targetCount);

        List<BatchEditItemViewModel> items = BatchEditCatalog.Items
            .Select(descriptor => new BatchEditItemViewModel(descriptor, project, Execute))
            .ToList();
        LyricActions = items.Where(item => item.Category == BatchEditCategory.Lyrics).ToList();
        NoteActions = items.Where(item => item.Category == BatchEditCategory.Notes).ToList();
        ResetActions = items.Where(item => item.Category == BatchEditCategory.Reset).ToList();

        CloseCommand = ReactiveCommand.Create(RequestBack);
    }

    public override void RequestBack()
    {
        RaiseClose(null);
    }

    private void Execute(BatchEditItemViewModel item)
    {
        if (item.RequiresConfirmation && !item.IsConfirmationPending)
        {
            item.IsConfirmationPending = true;
            item.ExecuteLabel = L.S("BatchEdit.Confirm");
            item.ValidationMessage = L.S("BatchEdit.Validation.Confirm");
            return;
        }

        if (!item.TryCreate(out BatchEdit? batchEdit) || batchEdit == null)
        {
            return;
        }

        item.IsConfirmationPending = false;
        item.ExecuteLabel = L.S("BatchEdit.Run");
        List<UNote> targetNotes = GetCurrentTargetNotes();
        if (_usesSelection && targetNotes.Count == 0)
        {
            item.ValidationMessage = L.S("BatchEdit.Validation.SelectionUnavailable");
            return;
        }

        RaiseClose(new BatchEditExecutionRequest(
            batchEdit,
            item.Title,
            targetNotes,
            item.SupportsCancellation));
    }

    private List<UNote> GetCurrentTargetNotes()
    {
        IEnumerable<UNote> notes = _usesSelection
            ? _selectedNotes.Where(_part.notes.Contains)
            : _part.notes;
        return notes.OrderBy(note => note.position).ToList();
    }
}
