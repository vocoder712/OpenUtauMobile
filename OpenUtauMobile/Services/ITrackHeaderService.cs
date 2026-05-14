using System.Threading.Tasks;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;

namespace OpenUtauMobile.Services;

public interface ITrackHeaderService
{
    Task<USinger?> PickSingerAsync();
    Task<Phonemizer?> PickPhonemizerAsync();
    Task<string?> PickRendererAsync(string[] supportedRenderers);
    Task<string?> PickTrackNameAsync(string currentName);
    Task<string?> PickTrackColorAsync(string currentColorName);
}