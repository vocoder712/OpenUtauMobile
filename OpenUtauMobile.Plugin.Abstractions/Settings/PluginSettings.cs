using System.Collections.Immutable;

namespace OpenUtauMobile.Plugin.Abstractions
{
    /// <summary>设置成功保存后的实际生效方式。</summary>
    public enum PluginSettingApplication
    {
        Immediate,
        ReloadResources,
        Restart,
    }

    /// <summary>设置字段及其生效策略，与临时安装输入分离。</summary>
    public sealed record PluginSettingDescriptor(PluginFieldDescriptor Field,
        PluginSettingApplication Application = PluginSettingApplication.Restart);

    /// <summary>明确执行的设置操作，不作为持久化字段值。</summary>
    public sealed record PluginSettingsAction(string Id, string DisplayName, bool IsDestructive = false,
        ImmutableArray<PluginFieldDescriptor> Fields = default);

    /// <summary>插件设置描述，配置按插件隔离并携带数据版本。</summary>
    public sealed record PluginSettingsDescriptor(int SchemaVersion, string DisplayName,
        ImmutableArray<PluginSettingDescriptor> Fields, ImmutableArray<PluginSettingsAction> Actions = default);

    /// <summary>只读设置快照，安装及发现不能借此改动全局配置。</summary>
    public interface IPluginSettingsReader
    {
        /// <summary>已保存配置的数据版本。</summary>
        int SchemaVersion { get; }
        /// <summary>获取当前插件的类型化设置。</summary>
        bool TryGet(string key, out PluginValue? value);
    }

    /// <summary>设置保存或操作的校验结果。</summary>
    public sealed record PluginSettingsValidation(ImmutableArray<PluginDiagnostic> Diagnostics)
    {
        /// <summary>存在错误时宿主不能提交草稿。</summary>
        public bool IsValid => Diagnostics.IsDefaultOrEmpty || Diagnostics.All(item => item.Severity != PluginDiagnosticSeverity.Error);
    }

    /// <summary>插件提供的设置校验及操作，不自行持久化宿主设置文件。</summary>
    public interface IPluginSettingsHandler
    {
        /// <summary>设置分组、字段与操作。</summary>
        PluginSettingsDescriptor Descriptor { get; }
        /// <summary>保存前校验完整草稿，实际持久化由宿主完成。</summary>
        ValueTask<PluginSettingsValidation> ValidateAsync(ImmutableDictionary<string, PluginValue> values,
            CancellationToken cancellationToken);
        /// <summary>将旧数据转换到当前版本，保留需要延续的未知字段。</summary>
        ImmutableDictionary<string, PluginValue> Upgrade(int previousVersion,
            ImmutableDictionary<string, PluginValue> values);
        /// <summary>执行用户明确触发的导入、检查等操作，资源上下文由宿主提供。</summary>
        ValueTask ExecuteAsync(string actionId, PluginSettingsOperationContext context,
            IProgress<InstallProgress> progress, CancellationToken cancellationToken);
    }

    /// <summary>设置操作的输入与资源目录；文件输入由宿主按声明字段选择。</summary>
    public sealed record PluginSettingsOperationContext(string ResourceDirectory, IInstallWorkspace Workspace,
        ImmutableDictionary<string, PluginValue> Values, IPluginLog Log);
}
