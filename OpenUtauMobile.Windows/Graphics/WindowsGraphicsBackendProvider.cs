using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using OpenUtauMobile.Services.Graphics;

namespace OpenUtauMobile.Windows.Graphics
{
    public sealed class WindowsGraphicsBackendProvider : IPlatformGraphicsBackendProvider
    {
        private static readonly IReadOnlyDictionary<string, Win32RenderingMode> Modes =
            new Dictionary<string, Win32RenderingMode>
            {
                ["AngleEgl"] = Win32RenderingMode.AngleEgl,
                ["Wgl"] = Win32RenderingMode.Wgl,
                ["Vulkan"] = Win32RenderingMode.Vulkan,
                ["Software"] = Win32RenderingMode.Software,
            };

        public string PlatformId => "Windows";
        public IReadOnlyCollection<string> SupportedBackendIds { get; } = Array.AsReadOnly([.. Modes.Keys]);
        public IReadOnlyList<GraphicsBackendPreset> Presets { get; } = Array.AsReadOnly<GraphicsBackendPreset>(
        [
            new GraphicsBackendPreset("Auto", "Settings.Graphics.Auto"),
            new GraphicsBackendPreset("ANGLE", "Settings.Graphics.ANGLE", "AngleEgl", "Wgl", "Vulkan", "Software"),
            new GraphicsBackendPreset("OpenGL", "Settings.Graphics.OpenGL", "Wgl", "AngleEgl", "Vulkan", "Software"),
            new GraphicsBackendPreset("Vulkan", "Settings.Graphics.Vulkan", "Vulkan", "AngleEgl", "Wgl", "Software"),
            new GraphicsBackendPreset("Software", "Settings.Graphics.Software", "Software"),
        ]);

        public static Win32PlatformOptions CreatePlatformOptions(IReadOnlyList<string> order)
        {
            Win32PlatformOptions options = new();
            // 空列表保留框架默认值，不能把空数组传给 RenderingMode。
            if (order.Count > 0)
            {
                options.RenderingMode = order.Select(id => Modes[id]).ToArray();
            }
            return options;
        }
    }
}
