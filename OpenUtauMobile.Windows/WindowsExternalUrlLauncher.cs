using System;
using System.Diagnostics;
using System.Threading.Tasks;
using OpenUtauMobile.Services;

namespace OpenUtauMobile.Windows;

internal sealed class WindowsExternalUrlLauncher : IExternalUrlLauncher
{
    public Task<ExternalUrlLaunchResult> LaunchAsync(Uri uri)
    {
        try
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true
            };
            Process? process = Process.Start(startInfo);
            return Task.FromResult(process == null
                ? ExternalUrlLaunchResult.Failed("The Windows shell did not start a URL handler.")
                : ExternalUrlLaunchResult.Success);
        }
        catch (Exception exception)
        {
            return Task.FromResult(ExternalUrlLaunchResult.Failed(exception.Message));
        }
    }
}
