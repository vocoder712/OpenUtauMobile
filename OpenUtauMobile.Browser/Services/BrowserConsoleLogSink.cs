using System;
using System.Globalization;
using Serilog.Core;
using Serilog.Events;

namespace OpenUtauMobile.Browser.Services;

/// <summary>输出应用日志及异常堆栈，便于定位浏览器启动失败。</summary>
internal sealed class BrowserConsoleLogSink : ILogEventSink
{
    public void Emit(LogEvent logEvent)
    {
        string message = $"[{logEvent.Level}] {logEvent.RenderMessage(CultureInfo.InvariantCulture)}";
        if (logEvent.Exception != null)
        {
            message += Environment.NewLine + logEvent.Exception;
        }

        if (logEvent.Level >= LogEventLevel.Error)
        {
            Console.Error.WriteLine(message);
        }
        else
        {
            Console.WriteLine(message);
        }
    }
}
