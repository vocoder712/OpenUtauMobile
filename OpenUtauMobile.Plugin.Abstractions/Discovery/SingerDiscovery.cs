using System.Collections.Immutable;
using System.Globalization;
using OpenUtau.Core.Ustx;

namespace OpenUtauMobile.Plugin.Abstractions
{
    /// <summary>宿主配置的歌手根目录；读写能力由宿主提供。</summary>
    public sealed record SingerRoot(string Id, string Path, bool CanWrite);

    /// <summary>发现项的状态，独立于 USinger.Found/Loaded。</summary>
    public enum SingerAvailability
    {
        Ready,
        Unsupported,
        Invalid,
        MissingResources,
        Unavailable,
    }

    /// <summary>一次发现的条目；未知稳定身份时 Singer 可为空，但位置与诊断仍可展示。</summary>
    public sealed record SingerDiscoveryEntry(
        string Location,
        string DisplayName,
        SingerAvailability Availability,
        USinger? Singer = null,
        ImmutableArray<PluginDiagnostic> Diagnostics = default);

    /// <summary>提供方的一次完整或部分发现结果，不要求声库具有统一磁盘清单。</summary>
    public sealed record SingerDiscoveryResult(
        ImmutableArray<SingerDiscoveryEntry> Entries,
        ImmutableArray<PluginDiagnostic> Diagnostics = default,
        bool IsComplete = true);

    /// <summary>发现阶段的进度；总量未知时 Total 为空。</summary>
    public sealed record SingerDiscoveryProgress(string Message, long Completed = 0, long? Total = null);

    /// <summary>发现上下文；具体扫描深度和目录结构由提供方决定。</summary>
    public sealed record SingerDiscoveryContext(
        ImmutableArray<SingerRoot> Roots,
        CultureInfo Culture,
        IPluginSettingsReader Settings,
        IPluginLog Log);

    /// <summary>一个由提供方明确允许的维护操作。</summary>
    public sealed record SingerMaintenanceAction(string Id, string DisplayName, bool IsDestructive = false);

    /// <summary>提供歌手发现和可选维护能力，不直接修改宿主歌手字典。</summary>
    public interface ISingerProvider
    {
        /// <summary>提供方稳定 ID，使用插件自己的命名空间。</summary>
        string Id { get; }
        /// <summary>该提供方可能返回的类型 ID。</summary>
        ImmutableArray<string> SingerTypeIds { get; }
        /// <summary>发现根目录中的条目，按项返回错误，响应取消。</summary>
        ValueTask<SingerDiscoveryResult> DiscoverAsync(SingerDiscoveryContext context,
            IProgress<SingerDiscoveryProgress> progress, CancellationToken cancellationToken);
        /// <summary>声明当前条目的维护能力；默认不允许删除外部目录。</summary>
        ImmutableArray<SingerMaintenanceAction> GetMaintenanceActions(SingerDiscoveryEntry entry) => [];
        /// <summary>执行已由宿主确认的维护操作，默认不提供任何操作。</summary>
        ValueTask ExecuteMaintenanceAsync(SingerDiscoveryEntry entry, string actionId,
            CancellationToken cancellationToken) => ValueTask.FromException(new NotSupportedException("此提供方没有维护操作。"));
    }
}
