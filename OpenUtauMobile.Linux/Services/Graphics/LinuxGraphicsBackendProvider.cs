using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using OpenUtauMobile.Services.Graphics;

namespace OpenUtauMobile.Linux.Services.Graphics
{
    public sealed class LinuxGraphicsBackendProvider : IPlatformGraphicsBackendProvider
    {
        private static readonly IReadOnlyDictionary<string, X11RenderingMode> Modes =
            new Dictionary<string, X11RenderingMode>
            {
                ["Glx"] = X11RenderingMode.Glx,
                ["Egl"] = X11RenderingMode.Egl,
                ["Vulkan"] = X11RenderingMode.Vulkan,
                ["Software"] = X11RenderingMode.Software,
            };

        public string PlatformId => "Linux";
        public IReadOnlyCollection<string> SupportedBackendIds { get; } = Array.AsReadOnly([.. Modes.Keys]);
        public IReadOnlyList<GraphicsBackendPreset> Presets { get; } = Array.AsReadOnly<GraphicsBackendPreset>(
        [
            new GraphicsBackendPreset("Auto", "Settings.Graphics.Auto"),
            new GraphicsBackendPreset("GLX", "Settings.Graphics.GLX", "Glx", "Egl", "Vulkan", "Software"),
            new GraphicsBackendPreset("EGL", "Settings.Graphics.EGL", "Egl", "Glx", "Vulkan", "Software"),
            new GraphicsBackendPreset("Vulkan", "Settings.Graphics.Vulkan", "Vulkan", "Glx", "Egl", "Software"),
            new GraphicsBackendPreset("Software", "Settings.Graphics.Software", "Software"),
        ]);

        public static X11PlatformOptions CreatePlatformOptions(IReadOnlyList<string> order)
        {
            X11PlatformOptions options = new();
            // 空列表保留框架默认值，不能把空数组传给 RenderingMode。
            if (order.Count > 0)
            {
                options.RenderingMode = order.Select(id => Modes[id]).ToArray();
            }
            return options;
        }
    }
}
