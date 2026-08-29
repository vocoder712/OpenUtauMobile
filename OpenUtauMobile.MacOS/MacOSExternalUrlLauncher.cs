using System;
using System.Diagnostics;
using System.Threading.Tasks;
using OpenUtauMobile.Services;

namespace OpenUtauMobile.MacOS;

internal sealed class MacOSExternalUrlLauncher : IExternalUrlLauncher
{
    public Task<ExternalUrlLaunchResult> LaunchAsync(Uri uri)
    {
        try
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = "open",
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add(uri.AbsoluteUri);
            Process? process = Process.Start(startInfo);
            return Task.FromResult(process == null
                ? ExternalUrlLaunchResult.Failed("macOS did not start a URL handler.")
                : ExternalUrlLaunchResult.Success);
        }
        catch (Exception exception)
        {
            return Task.FromResult(ExternalUrlLaunchResult.Failed(exception.Message));
        }
    }
}
