using CommunityToolkit.Maui.Views;
using OpenUtauMobile.ViewModels.Controls;

namespace OpenUtauMobile.Views.Controls;

public partial class ExportAudioPopup : Popup
{
    private ExportAudioPopupViewModel ViewModel { get; }
    private string File { get; }
    public ExportAudioPopup(string file)
	{
        InitializeComponent();
        ViewModel = (ExportAudioPopupViewModel)BindingContext;
        File = file;
    }

    private void ButtonCancel_Clicked(object sender, EventArgs e)
    {
        CloseAsync();
    }

    private void ButtonExport_Clicked(object sender, EventArgs e)
    {
        if (ViewModel.Export(File))
        {
            CloseAsync();
        }
    }
}