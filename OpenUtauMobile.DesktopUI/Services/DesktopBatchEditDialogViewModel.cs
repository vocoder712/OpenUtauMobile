using System;
using System.Linq;
using System.Reactive;
using OpenUtau.Core.Ustx;
using OpenUtau.Core;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.ViewModels;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtauMobile.DesktopUI.Services;

internal sealed class DesktopBatchEditDialogViewModel : PopupViewModelBase
{
    private readonly BatchEditDescriptor _descriptor;
    private readonly UProject _project;
    private readonly UVoicePart _part;
    private readonly UNote[] _targets;

    public string Title => L.S(_descriptor.TitleKey);
    public string ParameterLabel => L.S(_descriptor.ParameterLabelKey);
    public string RunLabel => L.S("BatchEdit.Run");
    [Reactive] public string ParameterValue { get; set; }
    [Reactive] public string ValidationMessage { get; private set; } = string.Empty;
    public ReactiveCommand<Unit, Unit> RunCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    public DesktopBatchEditDialogViewModel(BatchEditDescriptor descriptor, UProject project, UVoicePart part, UNote[] targets)
    {
        _descriptor = descriptor;
        _project = project;
        _part = part;
        _targets = targets;
        ParameterValue = descriptor.DefaultValueFactory(project);
        RunCommand = ReactiveCommand.Create(Run);
        CancelCommand = ReactiveCommand.Create(RequestBack);
    }

    private void Run()
    {
        if (!_descriptor.TryCreate(ParameterValue, out OpenUtau.Core.Editing.BatchEdit? operation, out string message) || operation == null)
        {
            ValidationMessage = message;
            return;
        }
        if (!ReferenceEquals(_project, OpenUtau.Core.DocManager.Inst.Project) || !_project.parts.Contains(_part) ||
            _descriptor.RequiresNotes && _targets.Length == 0 || _targets.Any(note => !_part.notes.Contains(note)) ||
            PianoRollViewModel.IsBatchEditRunning || DocManager.Inst.HasOpenUndoGroup)
        {
            ValidationMessage = L.S("BatchEdit.Validation.SelectionUnavailable");
            return;
        }
        RaiseClose(new BatchEditExecutionRequest(operation, Title, _targets, _descriptor.SupportsCancellation, _descriptor.RequiresNotes));
    }

    public override void RequestBack() => RaiseClose(null);
}
