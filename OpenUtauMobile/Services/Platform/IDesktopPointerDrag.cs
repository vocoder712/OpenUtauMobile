using System;
using Avalonia;

namespace OpenUtauMobile.Services.Platform
{
    /// <summary>桌面旋钮的相对鼠标拖动；结束时释放原生资源并恢复光标。</summary>
    public interface IDesktopPointerDrag : IDisposable
    {
        Vector Move(Point position);
    }
}
