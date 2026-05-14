using OpenUtau.Core;
using OpenUtauMobile.Services;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile;

/// <summary>
/// 全局 DocManager 订阅者，捕获 <see cref="ErrorMessageNotification"/> 并通过
/// <see cref="ErrorDialogService"/> 弹出错误对话框。
/// 在 DocManager 初始化完成后立即注册，生命周期与应用相同。
/// </summary>
public class GlobalErrorSubscriber : ICmdSubscriber
{
    public static readonly GlobalErrorSubscriber Instance = new();

    private GlobalErrorSubscriber()
    {
    }

    public void Register()
    {
        DocManager.Inst.AddSubscriber(this);
    }

    public void OnNext(UCommand cmd, bool isUndo)
    {
        if (cmd is ErrorMessageNotification notification)
        {
            ErrorDialogService.Show(new ErrorDialogViewModel(notification));
        }
    }
}