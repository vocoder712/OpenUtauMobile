using System.Collections.Immutable;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Plugin.Abstractions;

namespace OpenUtauMobile.Services.SingerPlugins
{
    /// <summary>归属明确的歌手类型；内置类型的 PluginId 固定为 opum.builtin。</summary>
    public sealed record RegisteredSingerType(string PluginId, SingerTypeDescriptor Descriptor);

    /// <summary>登记时固定身份与类型范围的发现提供方。</summary>
    public sealed record RegisteredSingerProvider(string PluginId, string Id,
        ImmutableArray<string> SingerTypeIds, ISingerProvider Instance);

    /// <summary>登记时固定描述的文件安装器。</summary>
    public sealed record RegisteredSingerInstaller(string PluginId, SingerInstallerDescriptor Descriptor,
        ISingerPackageInstaller Instance);

    /// <summary>登记时固定描述的插件设置处理器。</summary>
    public sealed record RegisteredPluginSettings(string PluginId, PluginSettingsDescriptor Descriptor,
        IPluginSettingsHandler Instance);

    /// <summary>一次性发布的能力目录；消费者应持有同一快照完成查询。</summary>
    public sealed record SingerPluginSnapshot(
        ImmutableDictionary<string, SingerPluginDescriptor> Plugins,
        ImmutableDictionary<string, RegisteredSingerType> Types,
        ImmutableDictionary<USingerType, RegisteredSingerType> TypesByBridge,
        ImmutableDictionary<string, RegisteredSingerProvider> Providers,
        ImmutableDictionary<string, RegisteredSingerInstaller> Installers,
        ImmutableDictionary<string, RegisteredPluginSettings> Settings);
}
