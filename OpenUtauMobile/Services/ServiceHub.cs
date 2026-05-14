using System;
using Avalonia.Media;
using OpenUtauMobile.Storage;
using OpenUtauMobile.Themes.OpenUtauMobile.Runtime;

namespace OpenUtauMobile.Services;

/// <summary>
/// 跨平台能力抽象层
/// </summary>
public static class ServiceHub
{
    public static Action? InitAudioOutput { get; set; }
    public static IExternalStorageService? ExternalStorageService { get; set; }
    public static ISystemAccentColorProvider? SystemAccentColorProvider { get; set; }
    public static Func<(bool success, Color color, string source)>? TryGetPlatformAccentFallback { get; set; }
}