using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace OpenUtauMobile.Services;

/// <summary>
/// 全局 Toast 服务，任意线程均可调用 <see cref="Enqueue"/>。
/// </summary>
public static class ToastService
{
    private static readonly ConcurrentQueue<(string message, double durationMs)> _queue = new();
    private static int _isShowing; // 0 = idle, 1 = consuming；用 Interlocked 操作
    private static Func<Task>? _consume;

    /// <summary>
    /// 注册消费回调（由 ToastOverlay 在 OnAttachedToVisualTree 中调用）。
    /// 注册后若队列非空则立即触发消费。
    /// </summary>
    public static void Register(Func<Task> consume)
    {
        _consume = consume;
        // 若注册前已有消息排队，立即触发
        if (!_queue.IsEmpty)
            TryStartConsuming();
    }

    /// <summary>
    /// 注销消费回调（由 ToastOverlay 在 OnDetachedFromVisualTree 中调用）。
    /// </summary>
    public static void Unregister()
    {
        _consume = null;
        Interlocked.Exchange(ref _isShowing, 0);
    }

    /// <summary>
    /// 将消息加入队列，可在任意线程调用。
    /// </summary>
    public static void Enqueue(string message, double durationMs = 2000)
    {
        _queue.Enqueue((message, durationMs));
        TryStartConsuming();
    }

    /// <summary>
    /// 从队列取出下一条消息，供 ToastOverlay 消费循环调用。
    /// 若队列已空则返回 null 并将 _isShowing 重置为 idle。
    /// </summary>
    public static (string message, double durationMs)? Dequeue()
    {
        if (_queue.TryDequeue(out (string message, double durationMs) item))
            return item;

        // 队列空，重置标志
        Interlocked.Exchange(ref _isShowing, 0);
        return null;
    }

    // 尝试启动消费循环，保证只有一个在跑
    private static void TryStartConsuming()
    {
        if (_consume == null) return;
        // CAS：只有从 0 → 1 成功的线程才触发
        if (Interlocked.CompareExchange(ref _isShowing, 1, 0) == 0)
        {
            Dispatcher.UIThread.Post(() => _ = _consume.Invoke());
        }
    }
}