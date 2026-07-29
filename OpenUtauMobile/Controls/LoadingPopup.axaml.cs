namespace OpenUtauMobile.Controls;

public partial class LoadingPopup : PopupDialogControl
{
    protected override PopupDialogWidthPreset WidthPreset => PopupDialogWidthPreset.Compact;

    public LoadingPopup()
    {
        InitializeComponent();
    }
}
