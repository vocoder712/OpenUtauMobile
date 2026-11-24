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
/// 统一弹窗预设尺寸，并按宿主可用空间约束布局。
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
        HeightProperty.OverrideMetadata<PopupDialogControl>(new StyledPropertyMetadata<double>(
            coerce: (control, value) =>
            {
                var popup = (PopupDialogControl)control;
                if (popup._host == null)
                {
                    return value;
                }
                if (popup.IsDesktopWindowHosted)
                {
                    return double.NaN;
                }
                // 固定预设使用有效上限，自适应预设保留自动测量。
                double limit = popup.MaxHeight;
                double height = double.IsNaN(value) && !popup.UsesAdaptiveHeight ? limit : value;
                return double.IsFinite(limit) ? Math.Min(height, limit) : value;
            }));
    }

    protected virtual PopupDialogWidthPreset WidthPreset => PopupDialogWidthPreset.Regular;
    public PopupDialogWidthPreset DialogWidthPreset => WidthPreset;

    /// <summary>自适应是默认高度预设，由弹窗内容决定外框高度。</summary>
    public bool UsesAdaptiveHeight => Classes.Contains("DialogHeightAdaptive") ||
        !(Classes.Contains("DialogHeightCompact") || Classes.Contains("DialogHeightRegular") ||
          Classes.Contains("DialogHeightList") || Classes.Contains("DialogHeightExpanded"));

    private bool IsDesktopWindowHosted => _host != null && _dialogHost == null && ServiceHub.DesktopWindowContext != null;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MaxHeightProperty)
        {
            // 最小高度不能突破有效上限；放大窗口后从原始预设自动恢复。
            CoerceValue(MinHeightProperty);
            CoerceValue(HeightProperty);
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
        CoerceValue(HeightProperty);
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
        CoerceValue(HeightProperty);
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
        if (IsDesktopWindowHosted)
        {
            // 桌面窗口约束测量空间，自适应内容可以超过上一轮的客户区高度。
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
