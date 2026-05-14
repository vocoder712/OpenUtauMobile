using System;
using OpenUtauMobile.Services;

namespace OpenUtauMobile.ViewModels;

public abstract class PopupViewModelBase : ViewModelBase, IPopupContext
{
    /// <summary>
    /// 弹窗外部可以关尝试关弹窗。但是到底关不关，弹窗说了算（默认直接关，可以重写）
    /// </summary>
    public virtual void RequestBack()
    {
        ClosingEvent?.Invoke(this, null);
    }

    /// <summary>
    /// 外部服务成全她
    /// </summary>
    public event EventHandler<object?>? ClosingEvent;

    /// <summary>
    /// 弹窗说我想关
    /// </summary>
    /// <param name="result"></param>
    protected void RaiseClose(object? result)
    {
        ClosingEvent?.Invoke(this, result);
    }
}