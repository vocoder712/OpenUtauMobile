using System.Reactive;
using OpenUtau.Core;
using OpenUtauMobile.Helpers;
using ReactiveUI;

namespace OpenUtauMobile.ViewModels;

/// <summary>
/// 全局错误弹窗的 ViewModel。
/// 由 <see cref="OpenUtau.Core.ErrorMessageNotification"/> 的内容构造。
/// </summary>
public class ErrorDialogViewModel : PopupViewModelBase
{
    public string Title { get; }
    public string Message { get; }
    public string Detail { get; }
    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);

    public ReactiveCommand<Unit, Unit> CloseCommand { get; }

    public ErrorDialogViewModel(ErrorMessageNotification notification)
    {
        // 提取友好摘要
        if (notification.e is MessageCustomizableException mce)
        {
            Title = L.S("ErrorDialog.Title");
            Message = string.IsNullOrWhiteSpace(mce.Message)
                ? mce.SubstanceException.Message
                : mce.Message;
            Detail = mce.SubstanceException.ToString();
        }
        else if (notification.e != null)
        {
            Title = L.S("ErrorDialog.Title");
            Message = string.IsNullOrWhiteSpace(notification.message)
                ? notification.e.Message
                : notification.message;
            Detail = notification.e.ToString();
        }
        else
        {
            Title = L.S("ErrorDialog.Title");
            Message = string.IsNullOrWhiteSpace(notification.message)
                ? L.S("ErrorDialog.UnknownError")
                : notification.message;
            Detail = string.Empty;
        }

        CloseCommand = ReactiveCommand.Create(RequestBack);
    }

    public override void RequestBack()
    {
        RaiseClose(null);
    }
}