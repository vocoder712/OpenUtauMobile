using System.Windows.Input;
using IconPacks.Avalonia.PhosphorIcons;

namespace OpenUtauMobile.ViewModels;

/// <summary>
/// 上下文操作面板中的一个按钮项。
/// </summary>
public class ContextActionItem
{
    /// <summary>按钮显示图标（强类型）。</summary>
    public PackIconPhosphorIconsKind Icon { get; init; } = PackIconPhosphorIconsKind.CaretUp;

    /// <summary>按钮的 ToolTip 说明文字</summary>
    public string Tip { get; init; } = string.Empty;

    /// <summary>按钮点击时执行的命令</summary>
    public ICommand Command { get; init; } = null!;

    /// <summary>
    /// 是否为危险操作（删除等），true 时显示红色样式。
    /// </summary>
    public bool IsDanger { get; init; }
}