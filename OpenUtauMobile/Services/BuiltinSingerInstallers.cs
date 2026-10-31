using System;
using System.Collections.Immutable;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OpenUtauMobile.Services
{
    /// <summary>宿主明确支持的内置安装入口，不进入插件安装注册表。</summary>
    public sealed record BuiltinSingerInstaller(string Id, string DisplayName, string? SingerType,
        ImmutableArray<string> Extensions)
    {
        /// <summary>Vogen 使用单文件安装，其余入口使用编码向导。</summary>
        public bool IsArchive => SingerType != null;
    }

    /// <summary>内置归档复用上游安装器；单文件安装遵循桌面的复制与冲突规则。</summary>
    public static class BuiltinSingerInstallers
    {
        /// <summary>内置入口独立于插件注册、排序和生命周期。</summary>
        public static ImmutableArray<BuiltinSingerInstaller> All =>
        [
            new("builtin:utau", "UTAU", "utau", [".zip", ".rar", ".uar"]),
            new("builtin:diffsinger", "DiffSinger", "diffsinger", [".zip", ".rar", ".uar"]),
            new("builtin:enunu", "ENUNU", "enunu", [".zip", ".rar", ".uar"]),
            new("builtin:vogen", "Vogen", null, [".vogeon"]),
        ];

        /// <summary>沿用桌面解包规则，通过会话暂存与文件变更记录提供取消和回滚。</summary>
        public static async Task InstallAsync(BuiltinSingerInstaller installer, string source,
            string singerType, Encoding archiveEncoding, Encoding textEncoding,
            IProgress<(double Percent, string Message)> progress, CancellationToken cancellationToken = default)
        {
            BuiltinSingerInstallTransaction transaction = new(progress);
            await Task.Run(() => transaction.RunAsync(installer, source, singerType, archiveEncoding, textEncoding, cancellationToken), cancellationToken);
        }
    }
}
