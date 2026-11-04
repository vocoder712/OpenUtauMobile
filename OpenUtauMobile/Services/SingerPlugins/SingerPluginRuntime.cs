using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Avalonia.Threading;
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

        /// <summary>发现和安装提供方使用插件独占目录，自行读取持久化配置。</summary>
        internal static PluginStorageContext Storage(string pluginId) =>
            PluginWebSettingsService.Storage(pluginId);

        /// <summary>复用已绑定插件身份的宿主服务，分别实现日志和用户通知接口。</summary>
        internal sealed class PluginLog(string pluginId) : IPluginLog, IPluginNotifications
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

            /// <summary>同插件同代码更新原警告，后台调用经界面队列发布。</summary>
            public void PublishWarning(PluginWarning warning)
            {
                ArgumentNullException.ThrowIfNull(warning);
                ArgumentException.ThrowIfNullOrWhiteSpace(warning.Code);
                ArgumentException.ThrowIfNullOrWhiteSpace(warning.Message);
                Write(new(warning.Code, warning.Message, PluginDiagnosticSeverity.Warning));
                Dispatcher.UIThread.Post(() =>
                {
                    string name = SingerPluginService.Registry.Snapshot.Plugins.TryGetValue(pluginId, out SingerPluginDescriptor? plugin)
                        ? plugin.DisplayName : pluginId;
                    DocManager.Inst.ExecuteCmd(new WarningNotification(Key(warning.Code), "Warning.Plugin.Title",
                        string.IsNullOrWhiteSpace(warning.Details) ? null : "Warning.Plugin.Detail",
                        name, warning.Message, warning.Details ?? string.Empty));
                });
            }

            /// <summary>问题恢复后清除当前插件的指定警告，不能清除其他插件通知。</summary>
            public void ClearWarning(string code)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(code);
                Dispatcher.UIThread.Post(() => DocManager.Inst.ExecuteCmd(new ClearWarningsNotification(Key(code))));
            }

            /// <summary>插件 ID 不含冒号，固定前缀确保调用方不能跨插件清除通知。</summary>
            private string Key(string code) => $"plugin:{pluginId}:{code}";
        }
    }
}
