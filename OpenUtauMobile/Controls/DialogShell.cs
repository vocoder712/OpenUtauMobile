using System;
using System.Collections.Generic;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace OpenUtauMobile.Controls;

/// <summary>统一弹窗外框；业务视图只提供标题、内容、操作和关闭命令。</summary>
public class DialogShell : HeaderedContentControl
{
    public static readonly StyledProperty<ICommand?> CloseCommandProperty =
        AvaloniaProperty.Register<DialogShell, ICommand?>(nameof(CloseCommand));

    public static readonly StyledProperty<Control?> FooterProperty =
        AvaloniaProperty.Register<DialogShell, Control?>(nameof(Footer));

    public ICommand? CloseCommand
    {
        get => GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }

    public Control? Footer
    {
        get => GetValue(FooterProperty);
        set => SetValue(FooterProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FooterProperty)
        {
            if (change.OldValue is Control oldFooter)
            {
                LogicalChildren.Remove(oldFooter);
            }
            if (change.NewValue is Control newFooter)
            {
                LogicalChildren.Add(newFooter);
            }
        }
    }
}

/// <summary>显式定义的一行操作，可见按钮等宽铺满，不自动分行。</summary>
public class DialogActionRow : Panel
{
    private readonly List<Control> visibleActions = [];
    private double preferredWidth;

    protected override Size MeasureOverride(Size availableSize)
    {
        visibleActions.Clear();
        preferredWidth = 0;
        foreach (Control child in Children)
        {
            if (!child.IsVisible)
            {
                continue;
            }

            // 先读取标签的自然宽度；动态选项的隐藏按钮可能仍有可见的外层容器。
            child.Measure(Size.Infinity);
            if (child.DesiredSize.Width <= 0 && child.DesiredSize.Height <= 0)
            {
                continue;
            }
            visibleActions.Add(child);
            preferredWidth = Math.Max(preferredWidth, child.DesiredSize.Width);
        }

        return LayoutRow(availableSize.Width, false);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        LayoutRow(finalSize.Width, true);
        return finalSize;
    }

    private Size LayoutRow(double availableWidth, bool arrange)
    {
        if (visibleActions.Count == 0)
        {
            return default;
        }

        double width = double.IsPositiveInfinity(availableWidth)
            ? preferredWidth * visibleActions.Count
            : Math.Max(0, availableWidth);
        double cellWidth = width / visibleActions.Count;
        double rowHeight = 0;
        foreach (Control child in visibleActions)
        {
            // 只让文字在分配的单元格中换行，保留业务视图指定的按钮分组。
            child.Measure(new Size(cellWidth, double.PositiveInfinity));
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
        }
        if (arrange)
        {
            for (int index = 0; index < visibleActions.Count; index++)
            {
                visibleActions[index].Arrange(
                    new Rect(index * cellWidth, 0, cellWidth, rowHeight));
            }
        }

        return new Size(width, rowHeight);
    }
}

/// <summary>纵向排列业务视图指定的操作行，也可用作二维列表的外层项目面板。</summary>
public class DialogActionRows : StackPanel
{
}

/// <summary>兼容旧的单行操作区；新视图使用 DialogActionRow 明确表达行分组。</summary>
public class DialogActions : DialogActionRow
{
}
