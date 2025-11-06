namespace OpenUtauMobile.Views;

public partial class OptionsPage : ContentPage
{
	public OptionsPage()
	{
		InitializeComponent();
	}

    private void ButtonBack_Clicked(object sender, EventArgs e)
    {
		Navigation.PopModalAsync();
    }

    private void ButtonSettings_Clicked(object sender, EventArgs e)
    {
        Navigation.PushModalAsync(new SettingsPage());
    }

    private void ButtonAbout_Clicked(object sender, EventArgs e)
    {
        Navigation.PushModalAsync(new AboutPage());
    }

    private void ButtonHelp_Clicked(object sender, EventArgs e)
    {

    }

    private void ButtonExportLog_Clicked(object sender, EventArgs e)
    {
        Navigation.PushModalAsync(new LogExportPage());
    }
}