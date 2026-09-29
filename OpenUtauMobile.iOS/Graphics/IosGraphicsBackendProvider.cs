using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using OpenUtauMobile.Services.Graphics;

namespace OpenUtauMobile.iOS.Graphics
{
    public sealed class IosGraphicsBackendProvider : IPlatformGraphicsBackendProvider
    {
        private static readonly IReadOnlyDictionary<string, iOSRenderingMode> Modes =
            new Dictionary<string, iOSRenderingMode>
            {
                ["OpenGl"] = iOSRenderingMode.OpenGl,
                ["Metal"] = iOSRenderingMode.Metal,
            };

        public string PlatformId => "iOS";
        public IReadOnlyCollection<string> SupportedBackendIds { get; } = Array.AsReadOnly([.. Modes.Keys]);
        public IReadOnlyList<GraphicsBackendPreset> Presets { get; } = Array.AsReadOnly<GraphicsBackendPreset>(
        [
            new GraphicsBackendPreset("Auto", "Settings.Graphics.Auto"),
            new GraphicsBackendPreset("OpenGLES", "Settings.Graphics.OpenGLES", "OpenGl", "Metal"),
            new GraphicsBackendPreset("MetalExperimental", "Settings.Graphics.MetalExperimental", "Metal", "OpenGl"),
        ]);

        public static iOSPlatformOptions CreatePlatformOptions(IReadOnlyList<string> order)
        {
            iOSPlatformOptions options = new();
            // 空列表保留框架默认值，不能把空数组传给 RenderingMode。
            if (order.Count > 0)
            {
                options.RenderingMode = order.Select(id => Modes[id]).ToArray();
            }
            return options;
        }
    }
}
