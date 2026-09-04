using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenUtauMobile.Themes.OpenUtauMobile.Runtime;

namespace OpenUtauMobile.Controls;

public partial class ErrorDialogPopup : PopupDialogControl
{
    private bool _detailExpanded;
    private TopLevel? _topLevel;

    protected override PopupDialogWidthPreset WidthPreset => PopupDialogWidthPreset.Regular;

    public ErrorDialogPopup()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel != null)
        {
            _topLevel.SizeChanged += OnTopLevelSizeChanged;
            UpdateResponsiveHeight(_topLevel);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_topLevel != null)
        {
            _topLevel.SizeChanged -= OnTopLevelSizeChanged;
            _topLevel = null;
        }

        base.OnDetachedFromVisualTree(e);
    }

    private void OnTopLevelSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (sender is TopLevel topLevel)
        {
            UpdateResponsiveHeight(topLevel);
        }
    }

    private void UpdateResponsiveHeight(TopLevel topLevel)
    {
        MaxHeight = Math.Max(
            topLevel.ClientSize.Height - ThemeSemErrorDialogTokens.ViewportVerticalInset,
            ThemeSemErrorDialogTokens.ActionMinHeight);
    }

    private void OnDetailToggleClicked(object? sender, RoutedEventArgs e)
    {
        _detailExpanded = !_detailExpanded;
        DetailPanel.IsVisible = _detailExpanded;
        DetailChevron.Data = _detailExpanded
            ? (Avalonia.Media.Geometry?)Resources["IconChevronUp"]
            : (Avalonia.Media.Geometry?)Resources["IconChevronDown"];
    }
}
