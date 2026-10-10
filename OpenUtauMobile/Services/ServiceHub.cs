using System;
using System.Threading.Tasks;
using Avalonia.Media;
using OpenUtauMobile.Services.Graphics;
using OpenUtauMobile.Services.Performance;
using OpenUtauMobile.Services.Platform;
using OpenUtauMobile.Storage;
using OpenUtauMobile.Themes.OpenUtauMobile.Runtime;
using OpenUtauMobile.Themes.OpenUtauMobile.Runtime.Platform;

namespace OpenUtauMobile.Services;

/// <summary>
/// 跨平台能力抽象层
/// </summary>
public static class ServiceHub
{
    public static IGraphicsBackendService? GraphicsBackendService { get; set; }
    public static Controls.Gestures.IViewportInputPlatform? ViewportInputPlatform { get; set; }
    public static Action? InitAudioOutput { get; set; }
    public static IClipboardService ClipboardService { get; set; } = new AvaloniaClipboardService();
    public static IExternalUrlLauncher? ExternalUrlLauncher { get; set; }
    public static IExternalStorageService? ExternalStorageService { get; set; }
    /// <summary>等待虚拟文件系统写入持久存储；原生平台无需额外同步。</summary>
    public static Func<Task> FlushFileSystemAsync { get; set; } = () => Task.CompletedTask;
    public static ISystemAccentColorProvider? SystemAccentColorProvider { get; set; }
    public static Func<(bool success, Color color, string source)>? TryGetPlatformAccentFallback { get; set; }
    public static IPlatformPerformanceProvider? PlatformPerformanceProvider { get; set; }
    public static IPlatformDisplayService? PlatformDisplayService { get; set; }
    public static IPlatformCrashLogService? PlatformCrashLogService { get; set; }
}
