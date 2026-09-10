using System;
using Avalonia.Controls;

namespace OpenUtauMobile.Controls;

/// <summary>导入流程使用通用宽弹窗策略，仅额外限制内容的可用高度。</summary>
public abstract class ImportDialogControl : PopupDialogControl
{
    private const double VerticalViewportInset = 48d;

    protected override PopupDialogWidthPreset WidthPreset => PopupDialogWidthPreset.Wide;

    protected override void UpdateResponsiveSize(TopLevel host)
    {
        base.UpdateResponsiveSize(host);
        MaxHeight = Math.Max(0, host.ClientSize.Height - VerticalViewportInset);
    }
}
