using System.Threading.Tasks;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;

namespace OpenUtauMobile.Services.Tracks;

public interface ITrackHeaderService
{
    Task<USinger?> PickSingerAsync();
    Task<Phonemizer?> PickPhonemizerAsync();
    Task<RendererSettingsSelection?> PickRendererAsync(UProject project, UTrack track);
    Task<string?> PickTrackNameAsync(string currentName);
    Task<string?> PickTrackColorAsync(string currentColorName);
    Task ShowTrackSettingsAsync(UTrack track);
}
