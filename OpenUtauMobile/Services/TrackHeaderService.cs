using System.Threading.Tasks;
using Avalonia.Threading;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Controls;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Services;

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
        return await PopupService.Show<USinger?>(new SingerPickerPopup(), new SingerPickerViewModel());
    }

    public async Task<Phonemizer?> PickPhonemizerAsync()
    {
        return await Dispatcher.UIThread.InvokeAsync(static () =>
            PopupService.Show<Phonemizer?>(new PhonemizerPickerPopup(), new PhonemizerPickerViewModel())
        );
    }

    public async Task<string?> PickRendererAsync(string[] supportedRenderers)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
            PopupService.Show<string?>(new RendererPickerPopup(), new RendererPickerViewModel(supportedRenderers)));
    }

    public async Task<string?> PickTrackNameAsync(string currentName)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
            PopupService.Show<string?>(new TrackRenamePopup(), new TrackRenamePopupViewModel(currentName)));
    }

    public async Task<string?> PickTrackColorAsync(string currentColorName)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
            PopupService.Show<string?>(new TrackColorPickerPopup(),
                new TrackColorPickerPopupViewModel(currentColorName)));
    }
}