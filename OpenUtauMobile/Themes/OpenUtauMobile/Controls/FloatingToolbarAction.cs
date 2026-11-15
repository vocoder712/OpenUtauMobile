using System.Windows.Input;
using IconPacks.Avalonia.PhosphorIcons;

namespace OpenUtauMobile.Themes.OpenUtauMobile.Controls;

/// <summary>浮动工具栏的一项操作；修改操作集合时向工具栏赋予新的集合。</summary>
public sealed class FloatingToolbarAction
{
    /// <summary>操作名称，同时用于溢出菜单、提示和无障碍朗读。</summary>
    public required string Text { get; init; }

    /// <summary>现有图标库中的操作图标。</summary>
    public required PackIconPhosphorIconsKind Icon { get; init; }

    /// <summary>主面板与溢出面板共用的命令。</summary>
    public required ICommand Command { get; init; }

    /// <summary>传递给命令的业务参数。</summary>
    public object? CommandParameter { get; init; }

    /// <summary>始终收纳到溢出面板。</summary>
    public bool AlwaysInOverflow { get; init; }
}
