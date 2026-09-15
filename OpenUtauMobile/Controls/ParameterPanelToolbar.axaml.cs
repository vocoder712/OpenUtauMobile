using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Services.Dialogs;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Controls;

public partial class ParameterPanelToolbar : UserControl
{
    public ParameterPanelToolbar()
    {
        InitializeComponent();
    }

    private async void OpenExpressionsClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PianoRollViewModel vm) return;
        UProject project = DocManager.Inst.Project;
        int trackNo = vm.EditingVoicePart?.trackNo ?? -1;
        UTrack? track = trackNo >= 0 && trackNo < project.tracks.Count ? project.tracks[trackNo] : null;
        await PopupService.Show<bool>(new ExpressionsPopup(), new ExpressionsPopupViewModel(project, track));
    }
}