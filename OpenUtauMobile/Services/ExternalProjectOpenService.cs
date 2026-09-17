using System;
using System.Collections.Concurrent;

namespace OpenUtauMobile.Services;

/// <summary>
/// 平台交给应用处理的外部 USTX 打开请求。
/// </summary>
public abstract record ExternalProjectOpenItem;

public sealed record ExternalProjectOpenRequest(string LocalPath, bool DeleteSourceAfterRead)
    : ExternalProjectOpenItem;

/// <summary>
/// 外部 USTX 请求进入编辑器前发生的错误。
/// </summary>
public sealed record ExternalProjectOpenFailure(Exception Exception) : ExternalProjectOpenItem;

/// <summary>
/// 缓冲外部工程打开请求，避免平台请求早于应用初始化完成时丢失。
/// </summary>
public static class ExternalProjectOpenService
{
    private static readonly ConcurrentQueue<ExternalProjectOpenItem> Queue = new();
    private static readonly object ConsumerLock = new();
    private static Action? _consumer;

    public static void RegisterConsumer(Action consumer)
    {
        lock (ConsumerLock)
        {
            _consumer = consumer;
        }

        if (!Queue.IsEmpty)
        {
            consumer();
        }
    }

    public static void Enqueue(ExternalProjectOpenRequest request)
    {
        Queue.Enqueue(request);
        NotifyConsumer();
    }

    public static void ReportFailure(Exception exception)
    {
        Queue.Enqueue(new ExternalProjectOpenFailure(exception));
        NotifyConsumer();
    }

    public static bool TryDequeue(out ExternalProjectOpenItem? item) => Queue.TryDequeue(out item);

    public static bool HasPendingItems => !Queue.IsEmpty;

    private static void NotifyConsumer()
    {
        Action? consumer;
        lock (ConsumerLock)
        {
            consumer = _consumer;
        }
        consumer?.Invoke();
    }
}
