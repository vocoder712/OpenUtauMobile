using System;
using System.Collections.Generic;
using System.Threading;
using Avalonia.Logging;
using Serilog;
using SerilogLevel = Serilog.Events.LogEventLevel;

namespace OpenUtauMobile.Services;

/// <summary>将 Avalonia 警告和错误限流后写入应用日志。</summary>
internal sealed class AvaloniaLogSink : ILogSink
{
    private readonly RateLimitWindow _warnings = new();
    private readonly RateLimitWindow _errors = new();
    private readonly RateLimitWindow _fatalErrors = new();

    public bool IsEnabled(LogEventLevel level, string area) =>
        level >= LogEventLevel.Warning && Serilog.Log.IsEnabled(ToSerilogLevel(level));

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate)
    {
        Log(level, area, source, messageTemplate, []);
    }

    /// <summary>
    /// 记录一个日志事件。
    /// </summary>
    /// <param name="level"></param>
    /// <param name="area"></param>
    /// <param name="source"></param>
    /// <param name="messageTemplate"></param>
    /// <param name="propertyValues"></param>
    public void Log(LogEventLevel level, string area, object? source, string messageTemplate,
        params object?[] propertyValues)
    {
        if (!IsEnabled(level, area))
        {
            return;
        }

        RateLimitWindow window = level switch
        {
            LogEventLevel.Warning => _warnings,
            LogEventLevel.Error => _errors,
            _ => _fatalErrors,
        };
        // 先去重、限流，再分配参数和格式化；不保存控件或异常对象。
        if (!window.TryAccept(area, messageTemplate, out long suppressed))
        {
            return;
        }

        ILogger logger = Serilog.Log.Logger;
        if (suppressed > 0)
        {
            logger.Warning("[Avalonia] Suppressed {Count} {Level} events in the previous logging window",
                suppressed, level);
        }

        object?[] values = new object?[propertyValues.Length + 2];
        values[0] = area;
        values[1] = source?.GetType().FullName ?? "unknown";
        Array.Copy(propertyValues, 0, values, 2, propertyValues.Length);
        Exception? exception = null;
        foreach (object? value in propertyValues)
        {
            if (value is Exception candidate)
            {
                exception = candidate;
                break;
            }
        }

        logger.Write(ToSerilogLevel(level), exception,
            "[Avalonia/{AvaloniaArea}] [{AvaloniaSource}] " + messageTemplate, values);
    }

    /// <summary>
    /// 转换为 Serilog 日志级别。
    /// </summary>
    /// <param name="level"></param>
    /// <returns></returns>
    private static SerilogLevel ToSerilogLevel(LogEventLevel level) => level switch
    {
        LogEventLevel.Warning => SerilogLevel.Warning,
        LogEventLevel.Error => SerilogLevel.Error,
        _ => SerilogLevel.Fatal,
    };

    /// <summary>
    /// 一个简单的限流窗口，记录最近一段时间内的日志事件数量，并在达到最大事件数时抑制后续事件。
    /// </summary>
    private sealed class RateLimitWindow
    {
        private const int MaxEvents = 20;
        private const long WindowMilliseconds = 30_000;
        private readonly Lock _gate = new();
        private readonly HashSet<(string Area, string Template)> _seen = [with(MaxEvents)];
        private long _startedAt = Environment.TickCount64;
        private long _suppressedCount;

        /// <summary>
        /// 尝试接受一个日志事件，如果在当前窗口内已经达到最大事件数，则返回 false 并增加抑制计数。
        /// </summary>
        /// <param name="area"></param>
        /// <param name="messageTemplate"></param>
        /// <param name="suppressed">抑制的事件数量</param>
        /// <returns></returns>
        public bool TryAccept(string area, string messageTemplate, out long suppressed)
        {
            lock (_gate)
            {
                suppressed = 0;
                long now = Environment.TickCount64;
                if (now - _startedAt >= WindowMilliseconds)
                {
                    // 下次有日志时才报告抑制数量，不为日志创建后台定时任务。
                    suppressed = _suppressedCount;
                    _suppressedCount = 0;
                    _seen.Clear();
                    _startedAt = now;
                }

                // 参数变化仍视为同类事件；容量到顶后不再保存新模板。
                if (_seen.Count >= MaxEvents || !_seen.Add((area, messageTemplate)))
                {
                    _suppressedCount++;
                    return false;
                }

                return true;
            }
        }
    }
}
