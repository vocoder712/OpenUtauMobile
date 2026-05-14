using System.Reactive;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtauMobile.ViewModels;

public class TrackRenamePopupViewModel : PopupViewModelBase
{
    [Reactive] public string TrackName { get; set; } = string.Empty;

    public ReactiveCommand<Unit, Unit> ConfirmCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    public TrackRenamePopupViewModel(string currentName)
    {
        TrackName = currentName;
        ConfirmCommand = ReactiveCommand.Create(Confirm);
        CancelCommand = ReactiveCommand.Create(Cancel);
    }

    private void Confirm()
    {
        string name = (TrackName ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(name))
        {
            ToastService.Enqueue(L.S("TrackRename.Toast.Empty"));
            return;
        }

        RaiseClose(name);
    }

    private void Cancel()
    {
        RaiseClose(null);
    }

    public override void RequestBack()
    {
        Cancel();
    }
}
