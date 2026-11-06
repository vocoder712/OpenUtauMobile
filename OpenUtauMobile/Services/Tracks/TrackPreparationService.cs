using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia.Threading;
using OpenUtau.Api;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services.Dialogs;
using OpenUtauMobile.ViewModels;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;

namespace OpenUtauMobile.Services.Tracks;

/// <summary>先展示模态，再准备独立的歌手资源和音素器；活动轨道只在 UI 提交。</summary>
public sealed class TrackPreparationService : ReactiveObject
{
    private static readonly ConditionalWeakTable<USinger, object> SingerLocks = new();
    /// <summary>所有轨道准备共享一个模态事务，避免重复打开同一弹窗宿主。</summary>
    public static TrackPreparationService Inst { get; } = new();

    /// <summary>准备及提交期间禁止编辑，后台音素化不占用此状态。</summary>
    [Reactive] public bool IsBusy { get; private set; }

    /// <summary>拒绝重复的准备请求，异常在弹窗关闭和编辑解锁后传递。</summary>
    public async Task<bool> RunAsync(Func<LoadingPopupViewModel, Task> operation)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (IsBusy || DocManager.Inst.HasOpenUndoGroup) return false;
        IsBusy = true;
        try
        {
            await LoadingPopupService.RunAsync(L.S("TrackPreparation.Singer"), operation);
            return true;
        }
        finally { IsBusy = false; }
    }

    /// <summary>仅装载歌手资源，不在这里调用音素器 SetSinger；后者始终由原有后台队列串行执行。</summary>
    internal static async Task EnsureSingerAsync(USinger? singer)
    {
        if (singer is not { Found: true }) return;
        Stopwatch watch = Stopwatch.StartNew();
        await Task.Run(() =>
        {
            lock (SingerLocks.GetValue(singer, _ => new object()))
            {
                singer.EnsureLoaded();
                if (!singer.Loaded) throw new InvalidOperationException($"Failed to load singer: {singer.Name}");
            }
        });
        Log.Information("轨道准备 Singer：{Milliseconds:F2} ms，{Singer}", watch.Elapsed.TotalMilliseconds, singer.Id);
    }

    /// <summary>在 UI 读取偏好；耗时构造仅作用于尚未提交的音素器实例。</summary>
    internal static async Task<Phonemizer?> PrepareSingerAsync(USinger singer, LoadingPopupViewModel loading)
    {
        Preferences.Default.SingerPhonemizers.TryGetValue(singer.Id ?? string.Empty, out string? remembered);
        await EnsureSingerAsync(singer);
        loading.SetIndeterminate(L.S("TrackPreparation.Phonemizer"));
        Stopwatch watch = Stopwatch.StartNew();
        Phonemizer? result = await Task.Run(() =>
        {
            Phonemizer? prepared = TryCreate(remembered) ?? TryCreate(singer.DefaultPhonemizer);
            if (singer.Found)
            {
                string renderer = ExternalSingerPluginService.GetDefaultRenderer(singer);
                if (!string.IsNullOrEmpty(renderer)) OpenUtau.Core.Render.Renderers.GetOrCreate(renderer);
            }
            return prepared;
        });
        Log.Information("轨道准备 Phonemizer：{Milliseconds:F2} ms", watch.Elapsed.TotalMilliseconds);
        return result;
    }

    /// <summary>兼容原有默认音素器回退，但记录构造失败。</summary>
    private static Phonemizer? TryCreate(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier)) return null;
        try { return PhonemizerFactory.Get(identifier)?.Create(); }
        catch (Exception error)
        {
            Log.Error(error, "无法准备音素器 {Phonemizer}", identifier);
            return null;
        }
    }
}

/// <summary>捕获会话和轨道配置，防止等待期间重载同一个工程实例后误提交。</summary>
internal sealed class TrackPreparationOwner
{
    private readonly DocManager.SaveCheckpoint session = DocManager.Inst.CaptureSaveCheckpoint();
    private readonly UProject project;
    private readonly UTrack track;
    private readonly USinger? singer;
    private readonly Phonemizer phonemizer;
    private readonly URenderSettings settings;
    private readonly string? graph;

    internal TrackPreparationOwner(UProject project, UTrack track)
    {
        this.project = project;
        this.track = track;
        singer = track.Singer;
        phonemizer = track.Phonemizer;
        settings = track.RendererSettings.Clone();
        graph = track.ExpressionGraph;
    }

    /// <summary>提交后配置已变化，收尾只检查原文档会话。</summary>
    internal bool IsSessionCurrent => DocManager.Inst.IsSaveCheckpointCurrent(session);

    /// <summary>派生音素结果可更新；会话、轨道身份及用户选择的配置必须保持一致。</summary>
    internal bool IsCurrent => IsSessionCurrent &&
        ReferenceEquals(project, DocManager.Inst.Project) && project.tracks.Contains(track) &&
        ReferenceEquals(singer, track.Singer) && ReferenceEquals(phonemizer, track.Phonemizer) &&
        settings.renderer == track.RendererSettings.renderer && settings.resampler == track.RendererSettings.resampler &&
        settings.wavtool == track.RendererSettings.wavtool && graph == track.ExpressionGraph;

    /// <summary>目录扫描替换歌手实例后，旧准备结果不再可用。</summary>
    internal static bool IsAvailable(USinger singer) => !singer.Found ||
        SingerManager.Inst.Singers.TryGetValue(singer.Id, out USinger? registered) && ReferenceEquals(singer, registered);
}
