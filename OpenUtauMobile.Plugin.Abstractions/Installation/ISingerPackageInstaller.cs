using System.Collections.Immutable;

namespace OpenUtauMobile.Plugin.Abstractions
{
    /// <summary>用户手选的安装器描述；扩展名仅用于其文件选择器，可与其他安装器重叠。</summary>
    public sealed record SingerInstallerDescriptor(
        string Id,
        string DisplayName,
        ImmutableArray<string> SingerTypeIds,
        ImmutableArray<string> Extensions,
        string? IconKey = null,
        ImmutableArray<string> MimeTypes = default);

    /// <summary>文件包安装器；先选安装器再选文件，不提供自动探测接口。</summary>
    public interface ISingerPackageInstaller
    {
        /// <summary>菜单信息和文件过滤器。</summary>
        SingerInstallerDescriptor Descriptor { get; }
        /// <summary>在宿主已创建的工作区内创建安装会话。</summary>
        ValueTask<ISingerInstallSession> CreateSessionAsync(SingerInstallContext context,
            CancellationToken cancellationToken);
    }
}
