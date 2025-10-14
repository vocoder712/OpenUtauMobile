using CommunityToolkit.Maui.Views;
using OpenUtauMobile.ViewModels;
using OpenUtauMobile.ViewModels.Controls;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtauMobile.Views.Controls;

public partial class EditMenuPopup : Popup
{
    private int _currentTabIndex = 0;
    private int CurrentTabIndex
    {
        get
        {
            return _currentTabIndex;
        }
        set
        {
            _currentTabIndex = value;
            UpdateTab();
        }
    }
    public EditMenuPopup()
	{
		InitializeComponent();
        UpdateTab();
    }

    private void ButtonImportAudio_Clicked(object sender, EventArgs e)
    {
        CloseAsync("import_audio");
    }

    private void ButtonTab_Clicked(object sender, EventArgs e)
    {
        if (sender == ButtonTabImport)
        {
            CurrentTabIndex = 0;
        }
        else if (sender == ButtonTabExport)
        {
            CurrentTabIndex = 1;
        }
        else if (sender == ButtonTabAuxiliary)
        {
            CurrentTabIndex = 2;
        }
        else if (sender == ButtonTabMore)
        {
            CurrentTabIndex = 3;
        }
    }

    private void UpdateTab()
    {
        switch (CurrentTabIndex) 
        {
            case 0:
                GridImport.IsVisible = true;
                GridExport.IsVisible = false;
                GridAuxiliary.IsVisible = false;
                GridMore.IsVisible = false;
                break;
            case 1:
                GridImport.IsVisible = false;
                GridExport.IsVisible = true;
                GridAuxiliary.IsVisible = false;
                GridMore.IsVisible = false;
                break;
            case 2:
                GridImport.IsVisible = false;
                GridExport.IsVisible = false;
                GridAuxiliary.IsVisible = true;
                GridMore.IsVisible = false;
                break;
            case 3:
                GridImport.IsVisible = false;
                GridExport.IsVisible = false;
                GridAuxiliary.IsVisible = false;
                GridMore.IsVisible = true;
                break;
        }
    }

    private void ButtonExportAudio_Clicked(object sender, EventArgs e)
    {
        CloseAsync("export_audio");
    }

    private void ButtonSaveAs_Clicked(object sender, EventArgs e)
    {
        CloseAsync("save_as");
    }

    private void ButtonSettings_Clicked(object sender, EventArgs e)
    {
        CloseAsync("settings");
    }

    private void ButtonImportMidi_Clicked(object sender, EventArgs e)
    {
        CloseAsync("import_midi");
    }
}