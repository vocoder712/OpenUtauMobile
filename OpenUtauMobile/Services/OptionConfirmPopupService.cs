using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenUtauMobile.Controls;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Services;

/// <summary>
/// 显示通用选项确认弹窗。
/// </summary>
public static class OptionConfirmPopupService
{
    /// <summary>
    /// 使用相同的显示文本和返回值展示字符串选项。
    /// </summary>
    public static Task<string?> ShowAsync(
        string title,
        string content,
        IReadOnlyList<string> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        List<OptionConfirmOption> mappedOptions = options
            .Select(option => new OptionConfirmOption(option, option))
            .ToList();
        return ShowAsync(title, content, mappedOptions);
    }

    /// <returns>用户所选项的字符串值；关闭弹窗时返回 null。</returns>
    public static Task<string?> ShowAsync(
        string title,
        string content,
        IReadOnlyList<OptionConfirmOption> options)
    {
        OptionConfirmPopupViewModel viewModel = new(title, content, options);
        return PopupService.Show<string>(new OptionConfirmPopup(), viewModel);
    }
}
