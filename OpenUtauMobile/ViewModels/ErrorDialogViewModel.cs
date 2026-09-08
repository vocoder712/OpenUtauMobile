using System.Reactive;
using System.Text;
using System.Threading.Tasks;
using OpenUtau.Core;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services;
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

    public ReactiveCommand<Unit, Unit> CloseCommand { get; }
    public ReactiveCommand<Unit, Unit> CopyCommand { get; }

    public ErrorDialogViewModel(ErrorMessageNotification notification)
    {
        // 提取友好摘要
        if (notification.e is MessageCustomizableException mce)
        {
            Message = string.IsNullOrWhiteSpace(mce.Message)
                ? mce.SubstanceException.Message
                : mce.Message;
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
