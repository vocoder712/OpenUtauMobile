using OpenUtauMobile.Services.Platform;
using System;
using System.Threading.Tasks;
using OpenUtauMobile.Services.Graphics;
using Avalonia.Media;
using OpenUtauMobile.Storage;
using OpenUtauMobile.Services.Performance;
using OpenUtauMobile.Themes.OpenUtauMobile.Runtime;
using OpenUtauMobile.Themes.OpenUtauMobile.Runtime.Platform;

namespace OpenUtauMobile.Services;

/// <summary>
/// 跨平台能力抽象层
/// </summary>
public static class ServiceHub
{
    public static Func<ViewModels.MainViewModel, Avalonia.Controls.Window>? DesktopWindowFactory { get; set; }
    public static IDesktopWindowContext? DesktopWindowContext { get; set; }
    public static Func<Controls.PopupDialogWidthPreset, Avalonia.Size, Avalonia.Size>? DesktopPopupSizeProvider { get; set; }
    public static Func<OpenUtau.Core.Ustx.USinger?, System.Threading.Tasks.Task<OpenUtau.Core.Ustx.USinger?>>? DesktopSingerPicker { get; set; }
    public static Func<string, System.Threading.Tasks.Task<string?>>? DesktopTrackNamePicker { get; set; }
    public static Func<string[], System.Threading.Tasks.Task<string?>>? DesktopRendererPicker { get; set; }
    public static Func<PhonemizerPickerRequest, Task<PhonemizerPickerResult?>>? DesktopPhonemizerPicker { get; set; }
    public static Func<Avalonia.Controls.Control, Platform.IDesktopPointerDrag?>? DesktopPointerDragFactory { get; set; }
    public static bool UseDesktopFileWorkflows { get; set; }
    public static Func<System.Threading.Tasks.Task>? BeforeDesktopProjectOpenAsync { get; set; }
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
