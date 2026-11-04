using System.Diagnostics.CodeAnalysis;

namespace OpenUtauMobile.Plugin.Abstractions
{
    /// <summary>宿主与歌手插件共同遵循的契约版本和数值边界。</summary>
    public static class SingerPluginContract
    {
        /// <summary>当前支持的契约版本。</summary>
        public const int Version = 2;
        /// <summary>外部类型数值的起点；数值只用于桥接，不作为位标志。</summary>
        public const int ExternalTypeMinimum = 0x10000000;

        /// <summary>校验可直接作为跨平台单级目录名的插件 ID；能力 ID 另按命名空间规则校验。</summary>
        public static void ValidatePluginId(string id)
        {
            ArgumentException.ThrowIfNullOrEmpty(id);
            if (id.Length > 160 || !char.IsAsciiLetterLower(id[0]) || id.EndsWith('.')
                || id.StartsWith("opum.", StringComparison.Ordinal)
                || id.Any(character => !char.IsAsciiLetterLower(character) && !char.IsAsciiDigit(character)
                    && character is not '.' and not '-' and not '_'))
                throw new ArgumentException("插件 ID 须以小写字母开头，仅含小写字母、数字、点、下划线和短横线，不能以点结尾或使用 opum. 前缀。", nameof(id));

            string stem = id.Split('.')[0];
            if (stem is "con" or "prn" or "aux" or "nul"
                || (stem.Length == 4 && stem[3] is >= '1' and <= '9'
                    && (stem.StartsWith("com", StringComparison.Ordinal) || stem.StartsWith("lpt", StringComparison.Ordinal))))
                throw new ArgumentException("插件 ID 不能使用系统保留的设备名称。", nameof(id));
        }
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

        /// <summary>运行时动态查询 Web 设置能力；必须无副作用，不在查询时启动服务。</summary>
        ValueTask<PluginWebSettingsDescriptor?> GetWebSettingsAsync(PluginWebSettingsContext context,
            CancellationToken cancellationToken) => ValueTask.FromResult<PluginWebSettingsDescriptor?>(null);

        /// <summary>用户点击设置后创建网页会话；服务生命周期由返回对象管理。</summary>
        ValueTask<IPluginWebSettingsSession> OpenWebSettingsAsync(PluginWebSettingsContext context,
            CancellationToken cancellationToken) => ValueTask.FromException<IPluginWebSettingsSession>(
                new NotSupportedException("当前插件没有 Web 设置。"));
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
    }
}
