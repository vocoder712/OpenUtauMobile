using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using OpenUtauMobile.Services.Graphics;

namespace OpenUtauMobile.Android.Graphics
{
    public sealed class AndroidGraphicsBackendProvider : IPlatformGraphicsBackendProvider
    {
        private static readonly IReadOnlyDictionary<string, AndroidRenderingMode> Modes =
            new Dictionary<string, AndroidRenderingMode>
            {
                ["Vulkan"] = AndroidRenderingMode.Vulkan,
                ["Egl"] = AndroidRenderingMode.Egl,
                ["Software"] = AndroidRenderingMode.Software,
            };

        public string PlatformId => "Android";
        public IReadOnlyCollection<string> SupportedBackendIds { get; } = Array.AsReadOnly([.. Modes.Keys]);
        public IReadOnlyList<GraphicsBackendPreset> Presets { get; } = Array.AsReadOnly<GraphicsBackendPreset>(
        [
            new GraphicsBackendPreset("Auto", "Settings.Graphics.Auto"),
            new GraphicsBackendPreset("Vulkan", "Settings.Graphics.Vulkan", "Vulkan", "Egl", "Software"),
            new GraphicsBackendPreset("OpenGLES", "Settings.Graphics.OpenGLES", "Egl", "Vulkan", "Software"),
            new GraphicsBackendPreset("Software", "Settings.Graphics.Software", "Software"),
        ]);

        public static AndroidPlatformOptions CreatePlatformOptions(IReadOnlyList<string> order)
        {
            AndroidPlatformOptions options = new();
            // 空列表保留框架默认值，不能把空数组传给 RenderingMode。
            if (order.Count > 0)
            {
                options.RenderingMode = order.Select(id => Modes[id]).ToArray();
            }
            return options;
        }
    }
}
