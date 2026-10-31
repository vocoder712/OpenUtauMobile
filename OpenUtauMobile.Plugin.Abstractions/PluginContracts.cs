using System.Diagnostics.CodeAnalysis;

namespace OpenUtauMobile.Plugin.Abstractions
{
    /// <summary>宿主与歌手插件共同遵循的契约版本和数值边界。</summary>
    public static class SingerPluginContract
    {
        /// <summary>当前支持的契约版本。</summary>
        public const int Version = 1;
        /// <summary>外部类型数值的起点；数值只用于桥接，不作为位标志。</summary>
        public const int ExternalTypeMinimum = 0x10000000;
    }

    /// <summary>在程序集上声明歌手插件入口；宿主先检查版本，再创建入口实例。</summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
    public sealed class SingerPluginAttribute(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type entryPoint,
        int contractVersion = SingerPluginContract.Version) : Attribute
    {
        /// <summary>实现 ISingerPlugin 的公开无参入口。</summary>
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
        public Type EntryPoint { get; } = entryPoint;
        /// <summary>该入口要求的契约版本。</summary>
        public int ContractVersion { get; } = contractVersion;
    }

    /// <summary>插件的稳定身份与展示名称。</summary>
    public sealed record SingerPluginDescriptor(string Id, string DisplayName);

    /// <summary>只登记能力的插件入口；不得在登记阶段扫描目录或修改工程。</summary>
    public interface ISingerPlugin
    {
        /// <summary>插件身份，不因语言或显示名称改变。</summary>
        SingerPluginDescriptor Descriptor { get; }
        /// <summary>提交本插件的能力描述；宿主验证全部登记后统一发布。</summary>
        void Register(ISingerPluginRegistrar registrar);
    }

    /// <summary>宿主收集插件本轮登记，不向插件公开宿主服务容器。</summary>
    public interface ISingerPluginRegistrar
    {
        /// <summary>登记一个独立类型及 renderer 关联。</summary>
        void RegisterSingerType(SingerTypeDescriptor descriptor);
        /// <summary>登记目录发现提供方，不立即执行发现。</summary>
        void RegisterProvider(ISingerProvider provider);
        /// <summary>登记由用户手选的文件安装器，不自动探测包内容。</summary>
        void RegisterInstaller(ISingerPackageInstaller installer);
        /// <summary>登记设置描述和设置操作处理器。</summary>
        void RegisterSettings(IPluginSettingsHandler settings);
    }
}
