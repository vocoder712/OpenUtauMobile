using System.Collections.Immutable;

namespace OpenUtauMobile.Plugin.Abstractions
{
    /// <summary>操作进度的业务阶段，不将未知总量伪造成精确百分比。</summary>
    public enum InstallPhase
    {
        Analyzing,
        Extracting,
        Validating,
        Preparing,
        Publishing,
        Refreshing,
    }

    /// <summary>本会话的进度，总量可以未知。</summary>
    public sealed record InstallProgress(InstallPhase Phase, string Message, long Completed = 0, long? Total = null);

    /// <summary>用于刷新后定位歌手的稳定引用。</summary>
    public sealed record SingerReference(string ProviderId, string SingerId);

    /// <summary>用户选中的配置内目标，不接受任意绝对路径。</summary>
    public sealed record InstallTargetSelection(string TargetId);

    /// <summary>宿主提供的可写安装根目录。</summary>
    public sealed record InstallTarget(string Id, string Path, string DisplayName);

    /// <summary>可展示摘要和会话私有计划标识；不序列化插件原生对象。</summary>
    public sealed record PreparedInstall(Guid PlanId, long Revision, string InstallerId, string TargetId,
        string Summary, ImmutableArray<string> DestinationPaths, ImmutableArray<SingerReference> Singers,
        ImmutableArray<PluginDiagnostic> Diagnostics = default, long? RequiredBytes = null);

    /// <summary>提交后状态；未提交的校验失败通过异常返回。</summary>
    public enum InstallResultStatus
    {
        Committed,
        PartialFailure,
    }

    /// <summary>提交结果；刷新失败不能将已提交文件伪装成没有安装。</summary>
    public sealed record InstallResult(InstallResultStatus Status, ImmutableArray<PublishedLocation> Locations,
        ImmutableArray<SingerReference> Singers, ImmutableArray<PluginDiagnostic> Diagnostics = default);

    /// <summary>可诊断的安装失败，区别于正常取消。</summary>
    public sealed class SingerInstallException(string code, string message, Exception? innerException = null,
        string? fieldId = null) : Exception(message, innerException)
    {
        /// <summary>稳定错误代码，不通过本地化消息判断逻辑。</summary>
        public string Code { get; } = code;
        /// <summary>可以定位到具体输入字段。</summary>
        public string? FieldId { get; } = fieldId;
    }
}
