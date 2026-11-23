using System.Collections.Immutable;
using System.Globalization;

namespace OpenUtauMobile.Plugin.Abstractions
{
    /// <summary>会话内稳定的包来源；原生读取器只接收物化后的本机路径。</summary>
    public interface IInstallSource
    {
        /// <summary>展示名称，不用于推导歌手身份。</summary>
        string DisplayName { get; }
        /// <summary>已知长度，无法取得时为空。</summary>
        long? Length { get; }
        /// <summary>返回独立读取流，调用方负责释放。</summary>
        ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken);
        /// <summary>按需物化一次本地文件，不将 content URI 传入原生代码。</summary>
        ValueTask<string> GetLocalPathAsync(CancellationToken cancellationToken);
    }

    /// <summary>宿主创建并在 finally 释放的会话缓存。</summary>
    public interface IInstallWorkspace
    {
        /// <summary>CachePath/SingerInstall/SessionId 的绝对路径。</summary>
        string RootPath { get; }
        /// <summary>解析并验证相对路径，不自动创建文件。</summary>
        string GetPath(string relativePath);
        /// <summary>创建会话内子目录并返回绝对路径。</summary>
        string CreateDirectory(string relativePath);
    }

    /// <summary>已发现歌手的位置，不代替最终目标文件冲突检查。</summary>
    public sealed record InstalledSingerLocation(SingerReference Singer, string Location, SingerAvailability Availability);

    /// <summary>用于安装前身份提示的只读目录查询。</summary>
    public interface IInstalledSingerLookup
    {
        /// <summary>查询全部同身份位置，重复身份按既有 warning 处理。</summary>
        ValueTask<ImmutableArray<InstalledSingerLocation>> FindAsync(SingerReference singer,
            CancellationToken cancellationToken);
    }

    /// <summary>发布源的种类，不强制插件采用目录型声库。</summary>
    public enum InstallArtifactKind
    {
        File,
        Directory,
    }

    /// <summary>移动本会话内已校验内容到配置目标；首版不允许覆盖。</summary>
    public sealed record PublishRequest(string SourceRelativePath, string TargetId,
        string DestinationRelativePath, InstallArtifactKind Kind);

    /// <summary>已完成的正式发布回执，不属于 finally 的临时清理范围。</summary>
    public sealed record PublishedLocation(string Path, InstallArtifactKind Kind);

    /// <summary>宿主文件帮助器，不解释专有包格式或声库结构。</summary>
    public interface IInstallFileOperations
    {
        /// <summary>验证解压项只能写入指定会话子目录。</summary>
        string ResolveEntryPath(string workspaceDirectory, string entryPath);
        /// <summary>同卷移动，跨卷复制校验后移动，记录最终提交回执。</summary>
        ValueTask<PublishedLocation> PublishAsync(PublishRequest request, IProgress<InstallProgress> progress,
            CancellationToken cancellationToken);
        /// <summary>登记插件额外产生的会话临时路径，由宿主验证范围并清理。</summary>
        void RegisterTemporaryPath(string path);
        /// <summary>登记插件自行完成的正式发布，用于异常后的真实结果展示。</summary>
        void RecordPublishedLocation(PublishedLocation location);
    }

    /// <summary>安装器使用的宿主能力；不公开页面或服务容器。</summary>
    public sealed record SingerInstallContext(Guid SessionId, CultureInfo Culture, IInstallSource Source,
        IInstallWorkspace Workspace, ImmutableArray<SingerRoot> SingerRoots,
        ImmutableArray<InstallTarget> InstallTargets, string DefaultTargetId,
        IInstalledSingerLookup InstalledSingers, IInstallFileOperations Files,
        PluginStorageContext Storage, IPluginLog Log)
    {
        /// <summary>当前插件的用户通知入口。</summary>
        public IPluginNotifications Notifications => (IPluginNotifications)Log;
    }
}
