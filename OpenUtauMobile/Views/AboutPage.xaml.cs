using CommunityToolkit.Maui.Alerts;
using OpenUtau.Core.Util;

namespace OpenUtauMobile.Views;

public partial class AboutPage : ContentPage
{
	public AboutPage()
	{
		InitializeComponent();
		LabelVersion.Text = $"Version {AppInfo.VersionString} ({AppInfo.BuildString})";
		LabelCoreVersion.Text = BuildInfo.Version;
        LabelLyrics.Text = ViewConstants.ReleaseVersionLyrics;
    }
    private void ButtonBack_Clicked(object sender, EventArgs e)
    {
        Navigation.PopModalAsync();
    }

    private async void ButtonToGitHub_Clicked(object sender, EventArgs e)
    {
        const string url = "https://github.com/vocoder712/OpenUtauMobile";
        try
        {
            await Launcher.OpenAsync(new Uri(url));
        }
        catch
        {
            await Toast.Make(url, CommunityToolkit.Maui.Core.ToastDuration.Long).Show();
        }
    }
}