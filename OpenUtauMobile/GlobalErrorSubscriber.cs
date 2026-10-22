using System;
using Avalonia.Threading;
using OpenUtau.Core;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services.Dialogs;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile
{
    /// <summary>将内核错误接入既有弹窗，将内核提示接入支持点击查看异常的 Toast。</summary>
    public class GlobalErrorSubscriber : ICmdSubscriber
    {
        public static readonly GlobalErrorSubscriber Instance = new();

        private GlobalErrorSubscriber() { }

        /// <summary>应用初始化后订阅一次，生命周期与应用相同。</summary>
        public void Register()
        {
            DocManager.Inst.AddSubscriber(this);
        }

        /// <summary>只接收通知，不改变内核任务或播放状态；所有呈现对象都在界面线程构造。</summary>
        public void OnNext(UCommand cmd, bool isUndo)
        {
            if (cmd is not ErrorMessageNotification and not ToastNotification) return;
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => OnNext(cmd, isUndo));
                return;
            }

            if (cmd is ErrorMessageNotification error)
            {
                ErrorDialogService.Show(new ErrorDialogViewModel(error));
            }
            else if (cmd is ToastNotification toast)
            {
                string message = ResolveToastMessage(toast);
                if (toast.e != null)
                {
                    ErrorDialogViewModel model = new(new ErrorMessageNotification(message, toast.e));
                    double duration = toast.durationSec <= 0 ? 0 : Math.Max(10000, toast.durationSec * 1000.0);
                    ToastService.EnqueueError(message, model, duration);
                }
                else
                {
                    ToastService.Enqueue(message, toast.durationSec > 0 ? toast.durationSec * 1000.0 : 4000);
                }
            }
        }

        /// <summary>优先使用应用本地化，未知资源键回退摘要，避免把键名显示给用户。</summary>
        private static string ResolveToastMessage(ToastNotification toast)
        {
            if (!string.IsNullOrWhiteSpace(toast.translationKey))
            {
                try
                {
                    string translated = L.S(toast.translationKey);
                    if (translated != toast.translationKey && !string.IsNullOrWhiteSpace(translated)) return translated;
                }
                catch (InvalidOperationException) { }
            }
            return !string.IsNullOrWhiteSpace(toast.message) ? toast.message
                : !string.IsNullOrWhiteSpace(toast.e?.Message) ? toast.e.Message
                : L.S("ErrorDialog.UnknownError");
        }
    }
}