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
    private TopLevel? _host;

    protected virtual PopupDialogWidthPreset WidthPreset => PopupDialogWidthPreset.Regular;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _host = TopLevel.GetTopLevel(this);
        if (_host != null)
        {
            _host.SizeChanged += OnHostSizeChanged;
            UpdateResponsiveSize(_host);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_host != null)
        {
            _host.SizeChanged -= OnHostSizeChanged;
            _host = null;
        }
        base.OnDetachedFromVisualTree(e);
    }

    private void OnHostSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_host != null)
        {
            UpdateResponsiveSize(_host);
        }
    }

    /// <summary>统一各宽度预设的首次布局与窗口尺寸变化处理。</summary>
    protected virtual void UpdateResponsiveSize(TopLevel host)
    {
        double viewportWidth = host.ClientSize.Width;
        if (!double.IsFinite(viewportWidth) || viewportWidth <= 0)
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
        // 窄窗口优先保留视口留白，不用最小宽度反向撑破宿主约束。
        double width = Math.Clamp(viewportWidth - horizontalMargin * 2d, 0d, maxWidth);
        Width = width;
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
    }
}
