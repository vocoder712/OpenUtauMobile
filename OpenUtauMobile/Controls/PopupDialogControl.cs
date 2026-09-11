using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using DialogHostAvalonia;
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
        double viewportHeight = _dialogHost?.Bounds.Height ?? host.ClientSize.Height;
        Thickness inset = _dialogHost?.DialogMargin ?? DialogTokens.ViewportMargin;
        _availableHeight = double.IsFinite(viewportHeight)
            ? Math.Max(0d, viewportHeight - inset.Top - inset.Bottom)
            : double.PositiveInfinity;
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
        Width = width;
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
    }
}
