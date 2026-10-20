using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Threading;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Services.Dialogs
{
    /// <summary>一条提示的不可变快照；带错误详情时使用可点击变体。</summary>
    /// <param name="Message">用户可读的摘要。</param>
    /// <param name="DurationMs">可见驻留时长，非正数表示等待用户处理。</param>
    /// <param name="Error">点击后打开的错误详情；普通提示为空。</param>
    public sealed record ToastMessage(string Message, double DurationMs, ErrorDialogViewModel? Error = null);

    /// <summary>线程安全的应用内提示队列，同一时刻只允许当前宿主启动一个消费者。</summary>
    public static class ToastService
    {
        private static readonly object Gate = new();
        private static readonly LinkedList<ToastMessage> Queue = new();
        private static Func<Task>? _consume;
        private static int _registration;
        private static bool _running;

        /// <summary>注册当前浮层；普通桌面提示仍由状态栏显示，可点击错误由浮层承载。</summary>
        public static void Register(Func<Task> consume)
        {
            lock (Gate)
            {
                _consume = consume;
                _registration++;
                _running = false;
                TryStartConsuming();
            }
        }

        /// <summary>注销宿主，使已经排队的旧消费回调失效。</summary>
        public static void Unregister()
        {
            lock (Gate)
            {
                _consume = null;
                _registration++;
                _running = false;
            }
        }

        /// <summary>从任意线程加入普通提示，保留既有调用方式。</summary>
        public static void Enqueue(string message, double durationMs = 2000)
        {
            if (ServiceHub.DesktopWindowContext is { } context)
            {
                context.ShowMessage(message, durationMs);
                return;
            }
            Add(new ToastMessage(message, durationMs));
        }

        /// <summary>加入可点击的错误提示；较长驻留给触屏和键盘用户留出查看时间。</summary>
        public static void EnqueueError(string message, ErrorDialogViewModel error, double durationMs = 10000)
        {
            Add(new ToastMessage(message, durationMs, error));
        }

        /// <summary>取出一条消息；消费者退出时统一检查其间新加入的消息，避免丢失唤醒。</summary>
        public static ToastMessage? Dequeue()
        {
            lock (Gate)
            {
                if (Queue.First == null) return null;
                ToastMessage message = Queue.First.Value;
                Queue.RemoveFirst();
                return message;
            }
        }

        /// <summary>宿主卸载时将尚未处理的消息放回队首，不丢失详情入口。</summary>
        internal static void Return(ToastMessage message)
        {
            lock (Gate)
            {
                Queue.AddFirst(message);
                TryStartConsuming();
            }
        }

        private static void Add(ToastMessage message)
        {
            lock (Gate)
            {
                Queue.AddLast(message);
                TryStartConsuming();
            }
        }

        /// <summary>调用方持有队列锁；登记消费状态后才投递，避免后台入队启动并行消费者。</summary>
        private static void TryStartConsuming()
        {
            if (_consume == null || _running || Queue.Count == 0) return;
            _running = true;
            Func<Task> consume = _consume;
            int registration = _registration;
            Dispatcher.UIThread.Post(async () =>
            {
                lock (Gate)
                {
                    if (registration != _registration) return;
                }
                try
                {
                    await consume();
                }
                finally
                {
                    lock (Gate)
                    {
                        if (registration == _registration)
                        {
                            _running = false;
                            TryStartConsuming();
                        }
                    }
                }
            });
        }
    }
}