using OpenUtauMobile.Services.Dialogs;
using System.Threading.Tasks;
using System.Linq;
using Avalonia.Controls;
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

    public async Task<USinger?> PickSingerAsync(USinger? currentSinger = null)
    {
        if (ServiceHub.DesktopSingerPicker != null) return await ServiceHub.DesktopSingerPicker(currentSinger);
        return await PopupService.Show<USinger?>(new SingerPickerPopup(), new SingerPickerViewModel());
    }

    public Task<Phonemizer?> PickPhonemizerAsync() => PickPhonemizerAsync(null);

    public async Task<Phonemizer?> PickPhonemizerAsync(string? currentName, Control? anchor = null)
    {
        try
        {
            PhonemizerPickerResult? result = await PhonemizerPickerService.PickAsync(new(CurrentName: currentName, Anchor: anchor));
            return result?.Factory?.Create();
        }
        catch (System.Exception exception)
        {
            Serilog.Log.Error(exception, "Failed to create selected phonemizer");
            ErrorDialogService.Show(new ErrorDialogViewModel(new OpenUtau.Core.ErrorMessageNotification(exception)));
            return null;
        }
    }

    public async Task<RendererSettingsSelection?> PickRendererAsync(UProject project, UTrack track)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            using RendererPickerViewModel vm = new(project, track);
            return await PopupService.Show<RendererSettingsSelection?>(new RendererPickerPopup(), vm);
        });
    }

    public async Task<string?> PickRendererNameAsync(string[] supportedRenderers)
    {
        if (supportedRenderers.Length == 0) return null;
        if (ServiceHub.DesktopRendererPicker != null) return await ServiceHub.DesktopRendererPicker(supportedRenderers);
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            OptionConfirmPopupViewModel vm = new(L.S("TrackSettings.Renderer"), string.Empty,
                supportedRenderers.Select(renderer => new[] { new OptionConfirmOption(renderer, renderer) }));
            return PopupService.Show<string?>(new OptionConfirmPopup(), vm);
        });
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

    public Task ShowTrackSettingsAsync(UTrack track) => TrackSettingsService.Inst.ShowAsync(track);
}
