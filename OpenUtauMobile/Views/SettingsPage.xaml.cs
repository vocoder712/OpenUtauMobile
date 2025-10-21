using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using OpenUtauMobile.ViewModels;
using OpenUtauMobile.Resources.Strings;
using Serilog;

namespace OpenUtauMobile.Views;

public partial class SettingsPage : ContentPage
{
    private int _currentTabIndex = 0;
    private SettingsViewModel Viewmodel {  get; set; }
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
    public SettingsPage()
	{
		InitializeComponent();
        Viewmodel = (SettingsViewModel)BindingContext;
    }

    protected override bool OnBackButtonPressed()
    {
        return true;
    }

    private void ButtonTab_Clicked(object sender, EventArgs e)
    {
        if (sender == ButtonTabEditAndBehavior)
        {
            CurrentTabIndex = 0;
        }
        else if (sender == ButtonTabRenderAndPerformance)
        {
            CurrentTabIndex = 1;
        }
        else if (sender == ButtonTabFileAndStorage)
        {
            CurrentTabIndex = 2;
        }
        else if (sender == ButtonTabAppearanceAndLanguage)
        {
            CurrentTabIndex = 3;
        }
    }

    private void UpdateTab()
    {
        switch (CurrentTabIndex)
        {
            case 0:
                GridEditAndBehavior.IsVisible = true;
                GridRenderAndPerformance.IsVisible = false;
                GridFileAndStorage.IsVisible = false;
                GridAppearanceAndLanguage.IsVisible = false;
                break;
            case 1:
                GridEditAndBehavior.IsVisible = false;
                GridRenderAndPerformance.IsVisible = true;
                GridFileAndStorage.IsVisible = false;
                GridAppearanceAndLanguage.IsVisible = false;
                break;
            case 2:
                GridEditAndBehavior.IsVisible = false;
                GridRenderAndPerformance.IsVisible = false;
                GridFileAndStorage.IsVisible = true;
                GridAppearanceAndLanguage.IsVisible = false;
                break;
            case 3:
                GridEditAndBehavior.IsVisible = false;
                GridRenderAndPerformance.IsVisible = false;
                GridFileAndStorage.IsVisible = false;
                GridAppearanceAndLanguage.IsVisible = true;
                break;
        }
    }

    private void ButtonCancel_Clicked(object sender, EventArgs e)
    {
        Navigation.PopModalAsync();
    }

    private void ButtonSave_Clicked(object sender, EventArgs e)
    {
        Save();
    }

    private void Save()
    {
        try
        {
            Viewmodel.Save();
            Toast.Make(AppResources.SettingsSavedToast, ToastDuration.Short).Show();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "保存设置时出现未处理异常");
            Toast.Make(AppResources.SettingsSaveErrorToast, ToastDuration.Short).Show();
        }
    }

    private void ButtonConfirm_Clicked(object sender, EventArgs e)
    {
        Save();
        Navigation.PopModalAsync();
    }
}