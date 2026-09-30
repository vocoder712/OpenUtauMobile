using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Browser;
using OpenUtauMobile.Services.Graphics;

namespace OpenUtauMobile.Browser.Services.Graphics
{
    public sealed class BrowserGraphicsBackendProvider : IPlatformGraphicsBackendProvider
    {
        private static readonly IReadOnlyDictionary<string, BrowserRenderingMode> Modes =
            new Dictionary<string, BrowserRenderingMode>
            {
                ["WebGL2"] = BrowserRenderingMode.WebGL2,
                ["WebGL1"] = BrowserRenderingMode.WebGL1,
                ["Software2D"] = BrowserRenderingMode.Software2D,
            };

        public string PlatformId => "Browser";
        public IReadOnlyCollection<string> SupportedBackendIds { get; } = Array.AsReadOnly([.. Modes.Keys]);
        public IReadOnlyList<GraphicsBackendPreset> Presets { get; } = Array.AsReadOnly<GraphicsBackendPreset>(
        [
            new GraphicsBackendPreset("Auto", "Settings.Graphics.Auto"),
            new GraphicsBackendPreset("WebGL2", "Settings.Graphics.WebGL2", "WebGL2", "WebGL1", "Software2D"),
            new GraphicsBackendPreset("WebGL1", "Settings.Graphics.WebGL1", "WebGL1", "WebGL2", "Software2D"),
            new GraphicsBackendPreset("Software", "Settings.Graphics.Software", "Software2D"),
        ]);

        public static BrowserPlatformOptions CreatePlatformOptions(IReadOnlyList<string> order)
        {
            BrowserPlatformOptions options = new();
            // 空列表保留框架默认值，不能把空数组传给 RenderingMode。
            if (order.Count > 0)
            {
                options.RenderingMode = order.Select(id => Modes[id]).ToArray();
            }
            return options;
        }
    }
}
