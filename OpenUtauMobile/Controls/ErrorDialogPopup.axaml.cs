using Avalonia.Interactivity;

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
        DetailChevron.Data = _detailExpanded
            ? (Avalonia.Media.Geometry?)Resources["IconChevronUp"]
            : (Avalonia.Media.Geometry?)Resources["IconChevronDown"];
    }
}