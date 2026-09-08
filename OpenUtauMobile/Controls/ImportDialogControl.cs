using System;
using Avalonia;
using Avalonia.Controls;
using OpenUtauMobile.Themes.OpenUtauMobile.Runtime;

namespace OpenUtauMobile.Controls;

/// <summary>导入流程弹窗随可用窗口尺寸调整，横屏时保持底部按钮可见。</summary>
public abstract class ImportDialogControl : PopupDialogControl
{
    private TopLevel? host;
    protected override PopupDialogWidthPreset WidthPreset => PopupDialogWidthPreset.Wide;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        host = TopLevel.GetTopLevel(this);
        if (host == null) return;
        host.SizeChanged += HostSizeChanged;
        UpdateSize();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (host != null) host.SizeChanged -= HostSizeChanged;
        host = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void HostSizeChanged(object? sender, SizeChangedEventArgs e) => UpdateSize();

    private void UpdateSize()
    {
        if (host == null) return;
        double margin = ThemeSemImportTracksTokens.ViewportMargin;
        Width = Math.Clamp(host.ClientSize.Width - margin, 0, ThemeSemImportTracksTokens.DialogMaxWidth);
        MaxHeight = Math.Max(0, host.ClientSize.Height - margin);
    }
}
