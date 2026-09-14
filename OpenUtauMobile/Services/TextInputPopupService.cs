using System;
using System.Threading.Tasks;
using OpenUtauMobile.Controls;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Services;

public static class TextInputPopupService
{
    /// <summary>返回输入字符串；取消时返回 null。校验函数返回错误提示或 null。</summary>
    public static Task<string?> ShowAsync(string title, string hint, string watermark, string initialText = "", Func<string, string?>? validate = null)
        => PopupService.Show<string>(new TextInputPopup(), new TextInputPopupViewModel(title, hint, watermark, initialText, validate));
}
