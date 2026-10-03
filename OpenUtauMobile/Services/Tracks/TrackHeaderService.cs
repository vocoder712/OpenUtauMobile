using OpenUtauMobile.Services.Dialogs;
using System.Threading.Tasks;
using Avalonia.Threading;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Controls;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Services.Tracks;

public class TrackHeaderService : ITrackHeaderService
{
    private static TrackHeaderService? _inst;

    public static TrackHeaderService Inst
    {
        get
        {
            if (_inst == null)
            {
                _inst = new TrackHeaderService();
            }

            return _inst;
        }
    }

    public async Task<USinger?> PickSingerAsync()
    {
        if (ServiceHub.DesktopSingerPicker != null) return await ServiceHub.DesktopSingerPicker();
        return await PopupService.Show<USinger?>(new SingerPickerPopup(), new SingerPickerViewModel());
    }

    public async Task<Phonemizer?> PickPhonemizerAsync()
    {
        if (ServiceHub.DesktopPhonemizerPicker != null) return await ServiceHub.DesktopPhonemizerPicker();
        return await Dispatcher.UIThread.InvokeAsync(static () =>
            PopupService.Show<Phonemizer?>(new PhonemizerPickerPopup(), new PhonemizerPickerViewModel())
        );
    }

    public async Task<string?> PickRendererAsync(string[] supportedRenderers)
    {
        if (ServiceHub.DesktopRendererPicker != null) return await ServiceHub.DesktopRendererPicker(supportedRenderers);
        return await Dispatcher.UIThread.InvokeAsync(() =>
            PopupService.Show<string?>(new RendererPickerPopup(), new RendererPickerViewModel(supportedRenderers)));
    }

    public async Task<string?> PickTrackNameAsync(string currentName)
    {
        if (ServiceHub.DesktopTrackNamePicker != null) return await ServiceHub.DesktopTrackNamePicker(currentName);
        return await Dispatcher.UIThread.InvokeAsync(() =>
            TextInputPopupService.ShowAsync(L.S("Picker.TrackRename.Title"), string.Empty,
                L.S("Picker.TrackRename.Placeholder"), currentName,
                name => string.IsNullOrWhiteSpace(name) ? L.S("TrackRename.Toast.Empty") : null));
    }

    public async Task<string?> PickTrackColorAsync(string currentColorName)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
            PopupService.Show<string?>(new TrackColorPickerPopup(),
                new TrackColorPickerPopupViewModel(currentColorName)));
    }
}