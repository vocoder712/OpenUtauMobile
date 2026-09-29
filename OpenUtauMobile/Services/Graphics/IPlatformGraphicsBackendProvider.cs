using System.Collections.Generic;

namespace OpenUtauMobile.Services.Graphics
{
    /// <summary>声明框架支持的后端；设备是否可用由 Avalonia 初始化时判断。</summary>
    public interface IPlatformGraphicsBackendProvider
    {
        string PlatformId { get; }
        IReadOnlyCollection<string> SupportedBackendIds { get; }
        IReadOnlyList<GraphicsBackendPreset> Presets { get; }
    }
}
