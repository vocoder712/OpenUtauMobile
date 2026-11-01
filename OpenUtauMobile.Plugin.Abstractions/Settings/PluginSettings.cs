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

    /// <summary>插件设置描述，配置按插件隔离并携带数据版本。</summary>
    public sealed record PluginSettingsDescriptor(int SchemaVersion, string DisplayName,
        ImmutableArray<PluginSettingDescriptor> Fields);

    /// <summary>只读设置快照，安装及发现不能借此改动全局配置。</summary>
    public interface IPluginSettingsReader
    {
        /// <summary>该插件独占的资源目录，原生代码只接收这里或选择器返回的真实路径。</summary>
        string ResourceDirectory => "";
        /// <summary>已保存配置的数据版本。</summary>
        int SchemaVersion { get; }
        /// <summary>获取当前插件的类型化设置。</summary>
        bool TryGet(string key, out PluginValue? value);
    }

    /// <summary>设置保存的校验结果。</summary>
    public sealed record PluginSettingsValidation(ImmutableArray<PluginDiagnostic> Diagnostics)
    {
        /// <summary>存在错误时宿主不能提交草稿。</summary>
        public bool IsValid => Diagnostics.IsDefaultOrEmpty || Diagnostics.All(item => item.Severity != PluginDiagnosticSeverity.Error);
    }

    /// <summary>插件声明设置并负责校验，宿主负责呈现与持久化。</summary>
    public interface IPluginSettingsHandler
    {
        /// <summary>设置分组与字段。</summary>
        PluginSettingsDescriptor Descriptor { get; }
        /// <summary>接收本进程实际生效的快照；重启生效的字段在保存时不会提前应用。</summary>
        void Apply(IPluginSettingsReader settings) { }
        /// <summary>保存前校验完整草稿，实际持久化由宿主完成。</summary>
        ValueTask<PluginSettingsValidation> ValidateAsync(ImmutableDictionary<string, PluginValue> values,
            CancellationToken cancellationToken);
        /// <summary>将旧数据转换到当前版本，保留需要延续的未知字段。</summary>
        ImmutableDictionary<string, PluginValue> Upgrade(int previousVersion,
            ImmutableDictionary<string, PluginValue> values);
    }
}
