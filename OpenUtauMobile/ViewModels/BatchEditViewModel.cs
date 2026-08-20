using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Reactive;
using IconPacks.Avalonia.PhosphorIcons;
using OpenUtau.Core.Editing;
using OpenUtau.Core.Util;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Helpers;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtauMobile.ViewModels;

public sealed class BatchEditItemViewModel : ViewModelBase
{
    private readonly BatchEditDescriptor _descriptor;
    private readonly Action<BatchEditItemViewModel> _execute;
    private readonly Action<BatchEditItemViewModel> _togglePin;
    private bool _isPinned;

    public string Id => _descriptor.Id;
    public string Title => L.S(_descriptor.TitleKey);
    public BatchEditCategory Category => _descriptor.Category;
    public PackIconPhosphorIconsKind Icon => _descriptor.Icon;
    public bool HasParameter => _descriptor.ParameterKind != BatchEditParameterKind.None;
    public string ParameterLabel => HasParameter ? L.S(_descriptor.ParameterLabelKey) : string.Empty;
    public bool RequiresConfirmation => _descriptor.RequiresConfirmation;
    public bool SupportsCancellation => _descriptor.SupportsCancellation;
    public bool IsPinned
    {
        get => _isPinned;
        private set
        {
            if (_isPinned == value)
            {
                return;
            }
            this.RaiseAndSetIfChanged(ref _isPinned, value);
            this.RaisePropertyChanged(nameof(PinIcon));
            this.RaisePropertyChanged(nameof(PinLabel));
        }
    }
    public PackIconPhosphorIconsKind PinIcon => IsPinned
        ? PackIconPhosphorIconsKind.PushPinFill
        : PackIconPhosphorIconsKind.PushPin;
    public string PinLabel => L.S(IsPinned ? "BatchEdit.Unpin" : "BatchEdit.Pin");

    [Reactive] public string ParameterValue { get; set; }
    [Reactive] public string ValidationMessage { get; set; } = string.Empty;
    [Reactive] public bool IsConfirmationPending { get; set; }
    [Reactive] public string ExecuteLabel { get; set; } = L.S("BatchEdit.Run");

    public ReactiveCommand<Unit, Unit> ExecuteCommand { get; }
    public ReactiveCommand<Unit, Unit> TogglePinCommand { get; }

    public BatchEditItemViewModel(
        BatchEditDescriptor descriptor,
        UProject project,
        Action<BatchEditItemViewModel> execute,
        Action<BatchEditItemViewModel> togglePin,
        bool isPinned)
    {
        _descriptor = descriptor;
        _execute = execute;
        _togglePin = togglePin;
        _isPinned = isPinned;
        ParameterValue = descriptor.DefaultValueFactory(project);
        ExecuteCommand = ReactiveCommand.Create(() => _execute(this));
        TogglePinCommand = ReactiveCommand.Create(() => _togglePin(this));
    }

    public void SetPinned(bool isPinned)
    {
        IsPinned = isPinned;
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
    private readonly List<BatchEditItemViewModel> _items;

    public ObservableCollection<BatchEditItemViewModel> PinnedLyricActions { get; } = [];
    public ObservableCollection<BatchEditItemViewModel> PinnedNoteActions { get; } = [];
    public ObservableCollection<BatchEditItemViewModel> PinnedResetActions { get; } = [];
    public ObservableCollection<BatchEditItemViewModel> LyricActions { get; } = [];
    public ObservableCollection<BatchEditItemViewModel> NoteActions { get; } = [];
    public ObservableCollection<BatchEditItemViewModel> ResetActions { get; } = [];
    [Reactive] public bool HasPinnedLyrics { get; private set; }
    [Reactive] public bool HasPinnedNotes { get; private set; }
    [Reactive] public bool HasPinnedReset { get; private set; }
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

        Preferences.Default.PinnedBatchEdits ??= new Dictionary<string, List<string>>();
        _items = BatchEditCatalog.Items
            .Select(descriptor => new BatchEditItemViewModel(
                descriptor,
                project,
                Execute,
                TogglePin,
                GetPinnedIds(descriptor.Category).Contains(descriptor.Id)))
            .ToList();
        RefreshCollections();

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

    private void TogglePin(BatchEditItemViewModel item)
    {
        List<string> pinnedIds = GetPinnedIds(item.Category);
        pinnedIds.RemoveAll(id => string.Equals(id, item.Id, StringComparison.Ordinal));
        if (!item.IsPinned)
        {
            pinnedIds.Insert(0, item.Id);
        }
        Preferences.Save();
        RefreshCollections();
    }

    private void RefreshCollections()
    {
        RefreshCategory(BatchEditCategory.Lyrics, PinnedLyricActions, LyricActions);
        RefreshCategory(BatchEditCategory.Notes, PinnedNoteActions, NoteActions);
        RefreshCategory(BatchEditCategory.Reset, PinnedResetActions, ResetActions);
        HasPinnedLyrics = PinnedLyricActions.Count > 0;
        HasPinnedNotes = PinnedNoteActions.Count > 0;
        HasPinnedReset = PinnedResetActions.Count > 0;
    }

    private void RefreshCategory(
        BatchEditCategory category,
        ObservableCollection<BatchEditItemViewModel> pinnedItems,
        ObservableCollection<BatchEditItemViewModel> regularItems)
    {
        List<string> pinnedIds = GetPinnedIds(category);
        Dictionary<string, BatchEditItemViewModel> categoryItems = _items
            .Where(item => item.Category == category)
            .ToDictionary(item => item.Id, StringComparer.Ordinal);
        List<string> validPinnedIds = pinnedIds
            .Where(categoryItems.ContainsKey)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (!pinnedIds.SequenceEqual(validPinnedIds, StringComparer.Ordinal))
        {
            pinnedIds.Clear();
            pinnedIds.AddRange(validPinnedIds);
            Preferences.Save();
        }

        pinnedItems.Clear();
        foreach (string id in validPinnedIds)
        {
            BatchEditItemViewModel item = categoryItems[id];
            item.SetPinned(true);
            pinnedItems.Add(item);
        }

        regularItems.Clear();
        foreach (BatchEditItemViewModel item in _items.Where(item => item.Category == category))
        {
            if (!validPinnedIds.Contains(item.Id, StringComparer.Ordinal))
            {
                item.SetPinned(false);
                regularItems.Add(item);
            }
        }
    }

    private static List<string> GetPinnedIds(BatchEditCategory category)
    {
        string categoryKey = category.ToString();
        if (!Preferences.Default.PinnedBatchEdits.TryGetValue(categoryKey, out List<string>? pinnedIds) ||
            pinnedIds == null)
        {
            pinnedIds = [];
            Preferences.Default.PinnedBatchEdits[categoryKey] = pinnedIds;
        }
        return pinnedIds;
    }

    private List<UNote> GetCurrentTargetNotes()
    {
        IEnumerable<UNote> notes = _usesSelection
            ? _selectedNotes.Where(_part.notes.Contains)
            : _part.notes;
        return notes.OrderBy(note => note.position).ToList();
    }
}
