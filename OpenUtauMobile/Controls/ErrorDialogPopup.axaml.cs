using Avalonia.Interactivity;
using IconPacks.Avalonia.PhosphorIcons;

namespace OpenUtauMobile.Controls;

public partial class ErrorDialogPopup : PopupDialogControl
{
    private bool _detailExpanded;

    protected override PopupDialogWidthPreset WidthPreset => PopupDialogWidthPreset.Regular;

    public ErrorDialogPopup()
    {
        InitializeComponent();
    }

    private void OnDetailToggleClicked(object? sender, RoutedEventArgs e)
    {
        _detailExpanded = !_detailExpanded;
        DetailPanel.IsVisible = _detailExpanded;
        DetailChevron.Kind = _detailExpanded
            ? PackIconPhosphorIconsKind.CaretUp
            : PackIconPhosphorIconsKind.CaretDown;
    }
}
