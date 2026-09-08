using System;
using System.Runtime.InteropServices.JavaScript;
using System.Threading.Tasks;
using OpenUtauMobile.Services;

namespace OpenUtauMobile.Browser;

internal sealed partial class BrowserExternalUrlLauncher : IExternalUrlLauncher
{
    private const string ModuleName = "OpenUtauMobileExternalUrl";
    private const string ModulePath = "./external-url.js";

    public static async Task InitializeAsync()
    {
        await JSHost.ImportAsync(ModuleName, ModulePath);
    }

    public Task<ExternalUrlLaunchResult> LaunchAsync(Uri uri)
    {
        try
        {
            bool opened = OpenExternalUrl(uri.AbsoluteUri);
            return Task.FromResult(opened
                ? ExternalUrlLaunchResult.Success
                : ExternalUrlLaunchResult.Failed("The browser blocked the new tab."));
        }
        catch (Exception exception)
        {
            return Task.FromResult(ExternalUrlLaunchResult.Failed(exception.Message));
        }
    }

    [JSImport("openExternalUrl", ModuleName)]
    private static partial bool OpenExternalUrl(string url);
}