using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using DialogHostAvalonia;
using OpenUtauMobile.Services;
using OpenUtauMobile.Themes.OpenUtauMobile.Tokens.Components;

namespace OpenUtauMobile.Controls;

public enum PopupDialogWidthPreset
{
    Compact,
    Regular,
    Wide,
}

/// <summary>
/// 统一弹窗宽度预设与视口限高，保留各弹窗声明的尺寸值。
/// </summary>
public abstract class PopupDialogControl : UserControl
{
    private TopLevel? _host;
    private DialogHost? _dialogHost;
    private double _availableHeight = double.PositiveInfinity;

    static PopupDialogControl()
    {
        // 强制值仅限制当前布局，保留样式、本地值或绑定中的原始上限。
        MaxHeightProperty.OverrideMetadata<PopupDialogControl>(new StyledPropertyMetadata<double>(
            coerce: (control, value) => Math.Min(value, ((PopupDialogControl)control)._availableHeight)));
        MinHeightProperty.OverrideMetadata<PopupDialogControl>(new StyledPropertyMetadata<double>(
            coerce: (control, value) => Math.Min(value, ((PopupDialogControl)control).MaxHeight)));
    }

    protected virtual PopupDialogWidthPreset WidthPreset => PopupDialogWidthPreset.Regular;
    public PopupDialogWidthPreset DialogWidthPreset => WidthPreset;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MaxHeightProperty)
        {
            // 最小高度不能突破有效上限；放大窗口后从原始预设自动恢复。
            CoerceValue(MinHeightProperty);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _host = TopLevel.GetTopLevel(this);
        _dialogHost = this.FindAncestorOfType<DialogHost>();
        if (_dialogHost != null)
        {
            _dialogHost.SizeChanged += OnHostSizeChanged;
        }
        if (_host != null)
        {
            _host.SizeChanged += OnHostSizeChanged;
            UpdateResponsiveSize(_host);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_dialogHost != null)
        {
            _dialogHost.SizeChanged -= OnHostSizeChanged;
            _dialogHost = null;
        }
        if (_host != null)
        {
            _host.SizeChanged -= OnHostSizeChanged;
            _host = null;
        }
        base.OnDetachedFromVisualTree(e);
        _availableHeight = double.PositiveInfinity;
        CoerceValue(MaxHeightProperty);
    }

    private void OnHostSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_host != null)
        {
            UpdateResponsiveSize(_host);
        }
    }

    /// <summary>更新响应式宽度与有效限高，不覆盖原始高度配置。</summary>
    protected virtual void UpdateResponsiveSize(TopLevel host)
    {
        if (ServiceHub.DesktopWindowContext != null && _dialogHost == null)
        {
            // 独立桌面窗口由宿主限制尺寸，内容铺满客户区。
            _availableHeight = host is Window { SizeToContent: SizeToContent.Height or SizeToContent.WidthAndHeight } window
                ? window.MaxHeight : host.ClientSize.Height;
            CoerceValue(MaxHeightProperty);
            Width = double.NaN;
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            return;
        }
        double viewportHeight = _dialogHost?.Bounds.Height ?? host.ClientSize.Height;
        Thickness inset = _dialogHost?.DialogMargin ?? DialogTokens.ViewportMargin;
        _availableHeight = double.IsFinite(viewportHeight)
            ? Math.Max(0d, viewportHeight - inset.Top - inset.Bottom)
            : double.PositiveInfinity;
        if (ServiceHub.DesktopPopupSizeProvider is { } desktopSize)
            _availableHeight = desktopSize(WidthPreset, new Size(host.ClientSize.Width, _availableHeight)).Height;
        CoerceValue(MaxHeightProperty);

        double viewportWidth = host.ClientSize.Width;
        if (!double.IsFinite(viewportWidth) || viewportWidth <= 0)
        {
            return;
        }

        double horizontalMargin = viewportWidth >= DialogTokens.ExpandedViewportBreakpoint
            ? DialogTokens.ExpandedHorizontalInset
            : DialogTokens.ViewportMargin.Left;
        double maxWidth = WidthPreset switch
        {
            PopupDialogWidthPreset.Compact => DialogTokens.CompactMaxWidth,
            PopupDialogWidthPreset.Regular => DialogTokens.RegularMaxWidth,
            PopupDialogWidthPreset.Wide => DialogTokens.WideMaxWidth,
            _ => DialogTokens.RegularMaxWidth,
        };
        // 窄窗口优先保留视口留白，不用最小宽度反向撑破宿主约束。
        double width = Math.Clamp(viewportWidth - horizontalMargin * 2d, 0d, maxWidth);
        if (ServiceHub.DesktopPopupSizeProvider is { } desktopWidth)
            width = desktopWidth(WidthPreset, new Size(Math.Max(0, viewportWidth - horizontalMargin * 2d), _availableHeight)).Width;
        Width = width;
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
    }
}