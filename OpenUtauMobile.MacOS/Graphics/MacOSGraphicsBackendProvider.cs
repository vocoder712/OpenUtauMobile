using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using OpenUtauMobile.Services.Graphics;

namespace OpenUtauMobile.MacOS.Graphics
{
    public sealed class MacOSGraphicsBackendProvider : IPlatformGraphicsBackendProvider
    {
        private static readonly IReadOnlyDictionary<string, AvaloniaNativeRenderingMode> Modes =
            new Dictionary<string, AvaloniaNativeRenderingMode>
            {
                ["Metal"] = AvaloniaNativeRenderingMode.Metal,
                ["OpenGl"] = AvaloniaNativeRenderingMode.OpenGl,
                ["Software"] = AvaloniaNativeRenderingMode.Software,
            };

        public string PlatformId => "macOS";
        public IReadOnlyCollection<string> SupportedBackendIds { get; } = Array.AsReadOnly(Modes.Keys.ToArray());
        public IReadOnlyList<GraphicsBackendPreset> Presets { get; } = Array.AsReadOnly<GraphicsBackendPreset>(
        [
            new GraphicsBackendPreset("Auto", "Settings.Graphics.Auto"),
            new GraphicsBackendPreset("Metal", "Settings.Graphics.Metal", "Metal", "OpenGl", "Software"),
            new GraphicsBackendPreset("OpenGL", "Settings.Graphics.OpenGL", "OpenGl", "Metal", "Software"),
            new GraphicsBackendPreset("Software", "Settings.Graphics.Software", "Software"),
        ]);

        public static AvaloniaNativePlatformOptions CreatePlatformOptions(IReadOnlyList<string> order)
        {
            AvaloniaNativePlatformOptions options = new();
            // 空列表保留框架默认值，不能把空数组传给 RenderingMode。
            if (order.Count > 0)
            {
                options.RenderingMode = order.Select(id => Modes[id]).ToArray();
            }
            return options;
        }
    }
}
