using System;
using System.Collections.Generic;
using System.Runtime.InteropServices.JavaScript;
using System.Threading.Tasks;
using Avalonia.Threading;
using OpenUtau.Audio;
using OpenUtau.Core;
using OpenUtauMobile.Tools;
using Serilog;

namespace OpenUtauMobile.Browser.Services.Audio;

/// <summary>在浏览器主线程连接 Web Audio，并为后台音频对象提供能力快照。</summary>
internal static partial class BrowserAudioInterop
{
    private const string ModuleName = "OpenUtauMobileAudio";
    private static readonly object deviceLock = new();
    private static List<AudioOutputDevice> currentDevices = [];

    /// <summary>初始化成功后提供给浏览器宿主的输出采样率。</summary>
    internal static int SampleRate { get; private set; }
    /// <summary>是否可以通过浏览器的系统设备选择器切换输出。</summary>
    internal static bool CanSelectDevice { get; private set; }
    internal static event Action? DevicesChanged;

    /// <summary>预先加载音频线程，在用户首次操作之前准备好暂停的上下文。</summary>
    internal static async Task InitializeAsync()
    {
        try
        {
            await JSHost.ImportAsync(ModuleName, "/audio-output.js");
            SampleRate = await Initialize(JSHost.DotnetInstance);
            CanSelectDevice = CanSelectOutput();
        }
        catch (Exception exception)
        {
            SampleRate = 0;
            Log.Error(exception, "浏览器音频初始化失败，将使用静音后端");
        }
    }

    /// <summary>返回当前已选路由的快照，不从后台线程调用浏览器 API。</summary>
    internal static List<AudioOutputDevice> GetCurrentDevices()
    {
        lock (deviceLock)
        {
            return [.. currentDevices];
        }
    }

    /// <summary>浏览器已不再读取旧队列，允许回收其固定内存。</summary>
    [JSExport]
    public static Task ReleaseBuffer(int generation)
    {
        BrowserAudioBuffer.Release(generation);
        return Task.CompletedTask;
    }

    /// <summary>将消费时钟与上下文状态同步到托管快照。</summary>
    [JSExport]
    public static async Task UpdateStatus(int generation, int state, double latency)
    {
        await Dispatcher.UIThread.InvokeAsync(() => BrowserAudioOutput.Active?.UpdateStatus(generation, state, latency));
    }

    /// <summary>异步接收浏览器错误，避免从 JS 主线程同步进入托管运行时。</summary>
    [JSExport]
    public static async Task ReceiveError(int generation, string message)
    {
        await Dispatcher.UIThread.InvokeAsync(() => ReportError(generation, message));
    }

    /// <summary>将浏览器错误送入现有通知系统，保留用户重新授权的机会。</summary>
    internal static void ReportError(int generation, string message)
    {
        Log.Error("浏览器音频错误: {Message}", message);
        DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(new InvalidOperationException(message)));
    }

    /// <summary>设备切换成功后更新实际路由，设置页仅展示这个结果。</summary>
    [JSExport]
    public static async Task UpdateDevice(string id, string name)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            lock (deviceLock)
            {
                currentDevices = string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name)
                    ? []
                    : [new AudioOutputDevice
                    {
                        api = "WebAudio",
                        name = name,
                        deviceNumber = 0,
                        guid = GuidTools.CreateGuidFromStrings("WebAudio", id),
                    }];
            }
            DevicesChanged?.Invoke();
        });
    }

    [JSImport("initialize", ModuleName)]
    private static partial Task<int> Initialize(JSObject runtime);

    [JSImport("canSelectOutput", ModuleName)]
    private static partial bool CanSelectOutput();

    [JSImport("setPlayback", ModuleName)]
    internal static partial void SetPlayback(int generation, int state, int address);

    [JSImport("selectOutput", ModuleName)]
    internal static partial Task SelectOutputAsync();
}
