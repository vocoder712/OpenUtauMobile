using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using Avalonia.Threading;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Render;
using OpenUtau.Core.Vogen;
using OpenUtauMobile.Plugin.Abstractions;
using Serilog;

namespace OpenUtauMobile.Services.SingerPlugins
{
    /// <summary>保留提供方归属的管理候选，不伪造无法确认的歌手身份。</summary>
    public sealed record SingerCandidate(string ProviderId, SingerDiscoveryEntry Entry)
    {
        /// <summary>管理界面展示的定位和诊断信息。</summary>
        public string Details => string.Join("\n", new[] { Entry.Location }
            .Concat(Entry.Diagnostics.IsDefault ? [] : Entry.Diagnostics.Select(item => item.Code == "discovery.missing-reference"
                ? Helpers.L.S("SingerDiscovery.MissingReference") : item.Message)));
    }

    /// <summary>串行发现、合并和发布歌手；工程引用更新后才释放被替换实例。</summary>
    public sealed class SingerDiscoveryService : ICmdSubscriber
    {
        public static SingerDiscoveryService Instance { get; } = new();
        private readonly SemaphoreSlim gate = new(1, 1);
        private ImmutableArray<SingerCandidate> candidates = [];
        private bool subscribed;
        private long legacyGeneration;
        private readonly ConditionalWeakTable<USinger, object> providerInstances = new();
        /// <summary>最近一次完成的发现候选快照。</summary>
        public ImmutableArray<SingerCandidate> Candidates => candidates;
        /// <summary>仅在最终目录和工程引用都已更新后触发。</summary>
        public event Action? Changed;

        /// <summary>在 Core 初始化后接入旧通知；新调用方直接等待 RefreshAsync。</summary>
        public void Initialize()
        {
            if (subscribed) return;
            subscribed = true;
            DocManager.Inst.AddSubscriber(this);
        }

        /// <summary>目录维护与扫描互斥，无论维护成功与否都重新发布目录。</summary>
        internal async Task MaintainAsync(Func<Task> operation)
        {
            await gate.WaitAsync();
            try { await operation(); }
            finally
            {
                gate.Release();
                await RefreshAsync();
            }
        }

        /// <summary>提交期间阻止扫描看见尚未发布完整的目标卷暂存内容。</summary>
        internal async Task<T> WithCatalogLockAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
        {
            await gate.WaitAsync(cancellationToken);
            try { return await operation(); }
            finally { gate.Release(); }
        }

        /// <summary>扫描不修改公共目录，所有变更在 UI 线程一次性发布。</summary>
        public async Task RefreshAsync(CancellationToken cancellationToken = default, bool scanBuiltins = true)
        {
            await gate.WaitAsync(cancellationToken);
            List<USinger> owned = [];
            List<USinger> offered = [];
            try
            {
                long generation = Interlocked.Read(ref legacyGeneration);
                HashSet<USinger> previous = await Dispatcher.UIThread.InvokeAsync(() =>
                    SingerManager.Inst.Singers.Values.ToHashSet<USinger>(ReferenceEqualityComparer.Instance));
                SingerPluginSnapshot registry = SingerPluginService.Registry.Snapshot;
                ImmutableArray<SingerRoot> roots = SingerPluginRuntime.Roots();
                List<USinger> found = scanBuiltins
                    ? await Task.Run(() => ClassicSingerLoader.FindAllSingers().Concat(VogenSingerLoader.FindAllSingers()).ToList(), cancellationToken)
                    : await Dispatcher.UIThread.InvokeAsync(() => SingerManager.Inst.Singers.Values
                        .Where(singer => !providerInstances.TryGetValue(singer, out _)).ToList());
                if (scanBuiltins) owned.AddRange(found);
                List<SingerCandidate> entries = [];
                foreach (RegisteredSingerProvider provider in registry.Providers.Values.OrderBy(item => item.Id, StringComparer.Ordinal))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    SingerPluginRuntime.PluginLog log = new(provider.PluginId);
                    try
                    {
                        CultureInfo culture = CultureInfo.CurrentUICulture;
                        SingerDiscoveryResult result = await Task.Run(async () =>
                        {
                            SingerDiscoveryContext context = new(roots, culture, SingerPluginRuntime.Settings(provider.PluginId), log);
                            return await provider.Instance.DiscoverAsync(context, new Progress<SingerDiscoveryProgress>(), cancellationToken);
                        }, cancellationToken);
                        foreach (PluginDiagnostic diagnostic in result.Diagnostics.IsDefault ? [] : result.Diagnostics)
                        {
                            entries.Add(new(provider.Id, new(diagnostic.Location ?? provider.Id, provider.Id,
                                SingerAvailability.Unavailable, Diagnostics: [diagnostic])));
                            log.Write(diagnostic);
                        }
                        foreach (SingerDiscoveryEntry entry in result.Entries.IsDefault ? [] : result.Entries)
                        {
                            try
                            {
                                SingerDiscoveryEntry checkedEntry = entry;
                                if (entry.Singer is { } singer)
                                {
                                    bool seen = providerInstances.TryGetValue(singer, out _) || previous.Contains(singer);
                                    if (!seen) { owned.Add(singer); offered.Add(singer); }
                                    bool validType = registry.TypesByBridge.TryGetValue(singer.SingerType, out RegisteredSingerType? type)
                                        && provider.SingerTypeIds.Contains(type.Descriptor.Id);
                                    bool invalidReady = entry.Availability == SingerAvailability.Ready &&
                                        (type?.Descriptor.IsAvailable != true || !singer.Found || !RendererReady(singer));
                                    if (seen || !validType || string.IsNullOrWhiteSpace(singer.Id) ||
                                        invalidReady)
                                    {
                                        checkedEntry = entry with
                                        {
                                            Singer = null,
                                            Availability = SingerAvailability.Unavailable,
                                            Diagnostics = [new("discovery.invalid-singer", "歌手实例或渲染器不可用。", Location: entry.Location)]
                                        };
                                    }
                                    else if (entry.Availability == SingerAvailability.Ready) found.Add(singer);
                                }
                                else if (entry.Availability == SingerAvailability.Ready)
                                {
                                    checkedEntry = entry with
                                    {
                                        Availability = SingerAvailability.Invalid,
                                        Diagnostics = [new("discovery.missing-singer", "未返回歌手实例。", Location: entry.Location)]
                                    };
                                }
                                entries.Add(new(provider.Id, checkedEntry));
                            }
                            catch (Exception exception)
                            {
                                PluginDiagnostic diagnostic = new("discovery.invalid-entry", exception.Message, Location: entry.Location);
                                log.Write(diagnostic, exception);
                                entries.Add(new(provider.Id, entry with
                                {
                                    Singer = null,
                                    Availability = SingerAvailability.Invalid,
                                    Diagnostics = [diagnostic]
                                }));
                            }
                        }
                        if (!result.IsComplete) entries.Add(new(provider.Id, new(provider.Id, provider.Id,
                            SingerAvailability.Unavailable, Diagnostics: [new("discovery.incomplete", "部分目录未能完成扫描。")])));
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                    catch (Exception exception)
                    {
                        PluginDiagnostic diagnostic = new("discovery.failed", exception.Message, Location: provider.Id);
                        log.Write(diagnostic, exception);
                        entries.Add(new(provider.Id, new(provider.Id, provider.Id, SingerAvailability.Unavailable, Diagnostics: [diagnostic])));
                    }
                }
                cancellationToken.ThrowIfCancellationRequested();
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (generation != Interlocked.Read(ref legacyGeneration)) return;
                    Publish(found, entries, scanBuiltins);
                });
            }
            finally
            {
                // 取消或失败时也释放新实例，但不能触碰仍在公共目录里的旧实例。
                HashSet<USinger> retained = await Dispatcher.UIThread.InvokeAsync(() =>
                    SingerManager.Inst.Singers.Values.ToHashSet<USinger>(ReferenceEqualityComparer.Instance));
                foreach (USinger singer in offered)
                    providerInstances.GetValue(singer, _ => new object());
                foreach (USinger singer in owned.Distinct<USinger>(ReferenceEqualityComparer.Instance))
                {
                    if (!retained.Contains(singer)) DisposeSinger(singer);
                }
                gate.Release();
            }
        }

        private static bool RendererReady(USinger singer)
        {
            try
            {
                string[] renderers = SingerPluginService.Registry.GetSupportedRenderers(singer.SingerType);
                return renderers.Length > 0 && renderers.All(id => Renderers.GetOrCreate(id)?.SingerType == singer.SingerType);
            }
            catch { return false; }
        }

        /// <summary>合并按 ID 保留首个，重复项沿用现有警告通知。</summary>
        private void Publish(List<USinger> found, List<SingerCandidate> entries, bool refreshLegacy)
        {
            USinger[] old = SingerManager.Inst.Singers.Values.ToArray();
            Dictionary<string, USinger> next = found.GroupBy(item => item.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            SingerManager.Inst.Singers.Clear();
            foreach (KeyValuePair<string, USinger> item in next) SingerManager.Inst.Singers.Add(item.Key, item.Value);
            SingerManager.Inst.SingerGroups.Clear();
            foreach (IGrouping<USingerType, USinger> group in next.Values.GroupBy(item => item.SingerType))
                SingerManager.Inst.SingerGroups.Add(group.Key, group.OrderBy(item => item.LocalizedName, StringComparer.CurrentCulture).ToList());
            if (refreshLegacy)
            {
                ExternalSingerPluginService.RefreshSingers();
                next = SingerManager.Inst.Singers.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
            }
            foreach (PluginDiagnostic diagnostic in SingerPluginService.Diagnostics)
                entries.Add(new("host:plugin-errors", new(diagnostic.Location ?? "", diagnostic.Location ?? "",
                    SingerAvailability.Unavailable, Diagnostics: [diagnostic])));
            foreach (UTrack track in DocManager.Inst.Project.tracks)
            {
                string? id = track.Singer?.Id ?? track.singer;
                if (string.IsNullOrWhiteSpace(id)) continue;
                if (next.TryGetValue(id, out USinger? replacement)) track.Singer = replacement;
                else
                {
                    if (track.Singer?.Found == true) track.Singer = new UnavailableSinger(id, track.Singer.Name, track.Singer.SingerType);
                    if (!entries.Any(item => item.Entry.Singer?.Id == id || (item.ProviderId == "host:missing-singer" && item.Entry.Location == id)))
                        entries.Add(new("host:missing-singer", new(id, track.Singer?.Name ?? id, SingerAvailability.Unavailable,
                            Diagnostics: [new("discovery.missing-reference", "找不到歌手，请检查插件和歌手目录。")])));
                }
            }
            candidates = [.. entries];
            HashSet<USinger> retained = next.Values.ToHashSet<USinger>(ReferenceEqualityComparer.Instance);
            try { DocManager.Inst.ExecuteCmd(new SingersRefreshedNotification()); }
            finally { foreach (USinger singer in old) if (!retained.Contains(singer)) DisposeSinger(singer); }
            foreach (IGrouping<string, USinger> group in found.GroupBy(item => item.Id).Where(group => group.Count() > 1))
                DocManager.Inst.ExecuteCmd(new WarningNotification($"singers.duplicate:{group.Key}",
                    "warnings.singers.duplicate", "warnings.singers.duplicate.detail", group.Key,
                    group.First().Location, string.Join("\n", group.Skip(1).Select(item => item.Location))));
            foreach (Delegate handler in Changed?.GetInvocationList() ?? [])
                try { ((Action)handler)(); }
                catch (Exception exception) { Log.Warning(exception, "Failed to update singer discovery consumer"); }
        }

        private static void DisposeSinger(USinger singer)
        {
            try { (singer as IDisposable)?.Dispose(); }
            catch (Exception exception) { Log.Warning(exception, "Failed to dispose singer instance {SingerType}", singer.GetType().FullName); }
        }

        public void OnNext(UCommand cmd, bool isUndo)
        {
            if (cmd is SingersChangedNotification)
            {
                Interlocked.Increment(ref legacyGeneration);
                _ = AdaptLegacyRefreshAsync();
            }
        }

        private async Task AdaptLegacyRefreshAsync()
        {
            try { await RefreshAsync(scanBuiltins: false); }
            catch (Exception exception) { Log.Error(exception, "Failed to refresh singer providers"); }
        }
    }
}
