using System;
using System.Linq;
using System.Reactive;
using System.Text;
using System.Threading.Tasks;
using OpenUtau.Core;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services;
using OpenUtauMobile.Services.Dialogs;
using ReactiveUI;

namespace OpenUtauMobile.ViewModels;

/// <summary>
/// 全局错误弹窗的 ViewModel。
/// 由 <see cref="OpenUtau.Core.ErrorMessageNotification"/> 的内容构造。
/// </summary>
public class ErrorDialogViewModel : PopupViewModelBase
{
    public string Title { get; } = L.S("ErrorDialog.Title");
    public string Message { get; }
    public string Detail { get; }
    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);
    public bool HasMissingPackage { get; }

    public ReactiveCommand<Unit, Unit> CloseCommand { get; }
    public ReactiveCommand<Unit, Unit> CopyCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenDependencyManagerCommand { get; }

    public ErrorDialogViewModel(ErrorMessageNotification notification)
    {
        // 渲染任务可能包裹单一异常；多项失败仍保留整体错误，避免隐藏其他原因。
        Exception? error = notification.e;
        while (error is AggregateException aggregate && aggregate.InnerExceptions.Count == 1)
        {
            error = aggregate.InnerExceptions[0];
        }
        // 提取友好摘要；只有上游明确的缺包元数据才显示依赖管理入口。
        if (error is MessageCustomizableException mce)
        {
            string[] packages = mce.TranslatableMessage == "<translate:packages.errors.missing>"
                ? mce.Replaces?.OfType<string>().Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToArray() ?? []
                : [];
            HasMissingPackage = packages.Length > 0;
            Message = HasMissingPackage
                ? string.Format(L.S("ErrorDialog.MissingPackage"), string.Join(", ", packages))
                : string.IsNullOrWhiteSpace(mce.Message) ? mce.SubstanceException.Message : mce.Message;
            Detail = mce.SubstanceException.ToString();
        }
        else if (notification.e != null)
        {
            Message = string.IsNullOrWhiteSpace(notification.message)
                ? notification.e.Message
                : notification.message;
            Detail = notification.e.ToString();
        }
        else
        {
            Message = string.IsNullOrWhiteSpace(notification.message)
                ? L.S("ErrorDialog.UnknownError")
                : notification.message;
            Detail = string.Empty;
        }

        CloseCommand = ReactiveCommand.Create(RequestBack);
        CopyCommand = ReactiveCommand.CreateFromTask(CopyErrorAsync);
        OpenDependencyManagerCommand = ReactiveCommand.Create(() =>
        {
            if (!HasMissingPackage) return;
            RequestBack();
            ErrorDialogService.OpenDependencyManager();
        });
    }

    private async Task CopyErrorAsync()
    {
        StringBuilder text = new();
        text.AppendLine(Title);
        text.AppendLine(Message);
        if (HasDetail)
        {
            text.AppendLine();
            text.Append(Detail);
        }

        bool copied = await ServiceHub.ClipboardService.SetTextAsync(text.ToString());
        ToastService.Enqueue(L.S(copied
            ? "ErrorDialog.CopySucceeded"
            : "ErrorDialog.CopyFailed"));
    }

    public override void RequestBack()
    {
        RaiseClose(null);
    }
}
