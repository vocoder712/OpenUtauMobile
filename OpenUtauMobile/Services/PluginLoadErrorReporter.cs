using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenUtau.Core;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services.Dialogs;
using OpenUtauMobile.ViewModels;
using Serilog.Core;
using Serilog.Events;

namespace OpenUtauMobile.Services;

/// <summary>接收上游插件扫描的结构化错误日志，启动完成后统一显示错误弹窗。</summary>
public sealed class PluginLoadErrorReporter : ILogEventSink
{
    public static PluginLoadErrorReporter Instance { get; } = new();
    private readonly ConcurrentQueue<Exception> _pendingErrors = new();

    private PluginLoadErrorReporter()
    {
    }

    public void Emit(LogEvent logEvent)
    {
        if (logEvent.Level < LogEventLevel.Warning || logEvent.Exception == null)
        {
            return;
        }

        // 上游没有公开逐文件加载结果；按结构化模板识别，不解析日志文件或本地化文本。
        switch (logEvent.MessageTemplate.Text)
        {
            case "Failed to load {File}.":
                if (logEvent.Properties.TryGetValue("File", out LogEventPropertyValue? value)
                    && value is ScalarValue { Value: string file })
                {
                    _pendingErrors.Enqueue(new IOException(file, logEvent.Exception));
                }
                break;
            case "Failed to search plugins.":
                _pendingErrors.Enqueue(new IOException(PathManager.Inst.PluginsPath, logEvent.Exception));
                break;
        }
    }

    /// <summary>在主界面和错误弹窗服务就绪后调用，保留每个文件的原始异常详情。</summary>
    public void ShowPendingErrors()
    {
        List<Exception> errors = [];
        while (_pendingErrors.TryDequeue(out Exception? exception))
        {
            errors.Add(exception);
        }
        if (errors.Count == 0)
        {
            return;
        }

        string files = string.Join(Environment.NewLine, errors.Select(error => error.Message).Distinct());
        ErrorMessageNotification notification = new(
            string.Format(L.S("PluginManager.LoadFailed"), files),
            new AggregateException(L.S("PluginManager.LoadFailedTitle"), errors));
        ErrorDialogService.Show(new ErrorDialogViewModel(notification));
    }
}
