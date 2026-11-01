using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using OpenUtau.Core;
using OpenUtauMobile.Plugin.Abstractions;
using Serilog;

namespace OpenUtauMobile.Services.SingerPlugins
{
    /// <summary>发现和安装共享的宿主上下文，不依赖插件目录结构。</summary>
    internal static class SingerPluginRuntime
    {
        /// <summary>对本次操作固定根目录顺序，避免设置变化影响正在执行的任务。</summary>
        internal static ImmutableArray<SingerRoot> Roots() => PathManager.Inst.SingersPaths
            .Select((path, index) => new SingerRoot($"root-{index}", Path.GetFullPath(path),
                string.Equals(Path.GetFullPath(path), Path.GetFullPath(PathManager.Inst.SingersInstallPath),
                    Installation.InstallPaths.Comparison))).ToImmutableArray();

        /// <summary>向发现和安装提供本进程已生效的只读设置。</summary>
        internal static IPluginSettingsReader Settings(string pluginId) =>
            PluginSettingsService.Effective(pluginId);

        /// <summary>将契约诊断写入宿主日志。</summary>
        internal sealed class PluginLog(string pluginId) : IPluginLog
        {
            public void Write(PluginDiagnostic diagnostic, Exception? exception = null)
            {
                Serilog.Events.LogEventLevel level = diagnostic.Severity switch
                {
                    PluginDiagnosticSeverity.Error => Serilog.Events.LogEventLevel.Error,
                    PluginDiagnosticSeverity.Warning => Serilog.Events.LogEventLevel.Warning,
                    _ => Serilog.Events.LogEventLevel.Information,
                };
                Log.Write(level, exception, "Plugin {PluginId}: {Code}: {Message} ({Location})",
                    pluginId, diagnostic.Code, diagnostic.Message, diagnostic.Location);
            }
        }
    }
}
