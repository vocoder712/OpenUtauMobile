namespace OpenUtauMobile.Controls;

public partial class FilePickerPopup : PopupDialogControl
{
    protected override PopupDialogWidthPreset WidthPreset => PopupDialogWidthPreset.Wide;

    public FilePickerPopup()
    {
        InitializeComponent();
    }
}