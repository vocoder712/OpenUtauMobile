using System;
using Avalonia;
using Avalonia.Controls;

namespace OpenUtauMobile.Controls;

public enum PopupDialogWidthPreset
{
    Compact,
    Regular,
    Wide,
}

/// <summary>
/// 所有弹窗控件的基类，提供自动适应屏幕宽度的功能。
/// </summary>
public abstract class PopupDialogControl : UserControl
{
    private bool _widthInitialized;

    protected virtual PopupDialogWidthPreset WidthPreset => PopupDialogWidthPreset.Regular;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (_widthInitialized)
        {
            return;
        }

        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        double viewportWidth = topLevel?.ClientSize.Width ?? double.NaN;
        if (double.IsNaN(viewportWidth) || viewportWidth <= 0)
        {
            return;
        }

        double horizontalMargin = viewportWidth >= 840 ? 56d : 24d;
        double maxWidth = WidthPreset switch
        {
            PopupDialogWidthPreset.Compact => 360d,
            PopupDialogWidthPreset.Regular => 420d,
            PopupDialogWidthPreset.Wide => 560d,
            _ => 420d,
        };
        double minWidth = WidthPreset == PopupDialogWidthPreset.Wide ? 320d : 280d;

        double width = Math.Clamp(viewportWidth - horizontalMargin * 2d, minWidth, maxWidth);
        Width = width;
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        _widthInitialized = true;
    }
}