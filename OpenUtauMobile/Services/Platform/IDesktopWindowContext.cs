using System.Threading.Tasks;
using Avalonia.Controls;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Services.Platform
{
    /// <summary>同一桌面窗口的弹窗、焦点和状态栏共用一个生命周期。</summary>
    public interface IDesktopWindowContext
    {
        TopLevel? ActiveTopLevel { get; }
        string? DialogHostIdentifier { get; }
        bool IsModalOpen { get; }
        Task<object?> ShowPopupAsync(Control view, PopupViewModelBase model);
        void ShowMessage(string message, double durationMilliseconds);
    }
}
