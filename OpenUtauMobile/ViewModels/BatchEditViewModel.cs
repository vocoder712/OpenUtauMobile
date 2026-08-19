using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using IconPacks.Avalonia.PhosphorIcons;
using OpenUtau.Core;
using OpenUtau.Core.Editing;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Helpers;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtauMobile.ViewModels;

public sealed class BatchEditItemViewModel : ViewModelBase
{
    private readonly BatchEditDescriptor _descriptor;
    private readonly Func<BatchEditItemViewModel, Task> _executeAsync;

    public string Title => L.S(_descriptor.TitleKey);
    public BatchEditCategory Category => _descriptor.Category;
    public PackIconPhosphorIconsKind Icon => _descriptor.Icon;
    public bool HasParameter => _descriptor.ParameterKind != BatchEditParameterKind.None;
    public string ParameterLabel => HasParameter ? L.S(_descriptor.ParameterLabelKey) : string.Empty;
    public bool RequiresConfirmation => _descriptor.RequiresConfirmation;

    [Reactive] public string ParameterValue { get; set; }
    [Reactive] public bool IsRunning { get; set; }
    [Reactive] public string ProgressText { get; set; } = string.Empty;
    [Reactive] public string ValidationMessage { get; set; } = string.Empty;
    [Reactive] public bool IsConfirmationPending { get; set; }
    [Reactive] public string ExecuteLabel { get; set; } = L.S("BatchEdit.Run");

    public ReactiveCommand<Unit, Unit> ExecuteCommand { get; }

    public BatchEditItemViewModel(
        BatchEditDescriptor descriptor,
        UProject project,
        Func<BatchEditItemViewModel, Task> executeAsync)
    {
        _descriptor = descriptor;
        _executeAsync = executeAsync;
        ParameterValue = descriptor.DefaultValueFactory(project);
        ExecuteCommand = ReactiveCommand.CreateFromTask(ExecuteAsync);
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

    private async Task ExecuteAsync()
    {
        await _executeAsync(this);
    }
}

public sealed class BatchEditViewModel : PopupViewModelBase
{
    private readonly UProject _project;
    private readonly UVoicePart _part;
    private readonly List<UNote> _selectedNotes;
    private readonly bool _usesSelection;
    private CancellationTokenSource? _cancellationTokenSource;

    public IReadOnlyList<BatchEditItemViewModel> LyricActions { get; }
    public IReadOnlyList<BatchEditItemViewModel> NoteActions { get; }
    public IReadOnlyList<BatchEditItemViewModel> ResetActions { get; }
    public string ScopeText { get; }

    [Reactive] public bool IsBusy { get; private set; }
    [Reactive] public string StatusText { get; private set; } = string.Empty;

    public ReactiveCommand<Unit, Unit> CloseCommand { get; }

    public BatchEditViewModel(UProject project, UVoicePart part, IReadOnlyCollection<UNote> selectedNotes)
    {
        _project = project;
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
            .Select(descriptor => new BatchEditItemViewModel(descriptor, project, ExecuteAsync))
            .ToList();
        LyricActions = items.Where(item => item.Category == BatchEditCategory.Lyrics).ToList();
        NoteActions = items.Where(item => item.Category == BatchEditCategory.Notes).ToList();
        ResetActions = items.Where(item => item.Category == BatchEditCategory.Reset).ToList();

        CloseCommand = ReactiveCommand.Create(RequestBack);
    }

    public override void RequestBack()
    {
        _cancellationTokenSource?.Cancel();
        RaiseClose(null);
    }

    private async Task ExecuteAsync(BatchEditItemViewModel item)
    {
        if (IsBusy)
        {
            return;
        }

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

        IsBusy = true;
        item.IsRunning = true;
        item.ProgressText = string.Empty;
        StatusText = string.Format(CultureInfo.CurrentCulture, L.S("BatchEdit.Status.Running"), item.Title);
        _cancellationTokenSource = new CancellationTokenSource();

        try
        {
            List<UNote> targetNotes = GetCurrentTargetNotes();
            if (_usesSelection && targetNotes.Count == 0)
            {
                item.ValidationMessage = L.S("BatchEdit.Validation.SelectionUnavailable");
                StatusText = item.ValidationMessage;
                return;
            }

            if (batchEdit.IsAsync)
            {
                CancellationToken token = _cancellationTokenSource.Token;
                await Task.Run(() => batchEdit.RunAsync(
                    _project,
                    _part,
                    targetNotes,
                    DocManager.Inst,
                    (current, total) =>
                    {
                        string progress = total > 0
                            ? string.Format(CultureInfo.CurrentCulture, L.S("BatchEdit.Status.Progress"), current, total)
                            : string.Empty;
                        Dispatcher.UIThread.Post(() => item.ProgressText = progress);
                    },
                    token), token);
            }
            else
            {
                batchEdit.Run(_project, _part, targetNotes, DocManager.Inst);
            }

            StatusText = string.Format(CultureInfo.CurrentCulture, L.S("BatchEdit.Status.Completed"), item.Title);
        }
        catch (OperationCanceledException)
        {
            StatusText = L.S("BatchEdit.Status.Cancelled");
        }
        catch (Exception exception)
        {
            item.ValidationMessage = exception.GetBaseException().Message;
            StatusText = string.Format(CultureInfo.CurrentCulture, L.S("BatchEdit.Status.Failed"), item.Title);
        }
        finally
        {
            item.IsRunning = false;
            IsBusy = false;
            _cancellationTokenSource.Dispose();
            _cancellationTokenSource = null;
        }
    }

    private List<UNote> GetCurrentTargetNotes()
    {
        IEnumerable<UNote> notes = _usesSelection
            ? _selectedNotes.Where(_part.notes.Contains)
            : _part.notes;
        return notes.OrderBy(note => note.position).ToList();
    }
}
