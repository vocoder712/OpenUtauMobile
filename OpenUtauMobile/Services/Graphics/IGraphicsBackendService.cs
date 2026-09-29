using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenUtauMobile.Services.Graphics
{
    public interface IGraphicsBackendService
    {
        string PlatformId { get; }
        IReadOnlyList<GraphicsBackendPreset> Presets { get; }
        IReadOnlyList<string> StartupFallbackOrder { get; }
        IReadOnlyList<string> SavedFallbackOrder { get; }
        bool HasInvalidConfiguration { get; }
        bool CanSave { get; }
        bool RequiresRestart { get; }
        Task SavePresetAsync(GraphicsBackendPreset preset);
    }
}
