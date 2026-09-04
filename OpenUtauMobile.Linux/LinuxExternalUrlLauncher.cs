using System;
using System.Diagnostics;
using System.Threading.Tasks;
using OpenUtauMobile.Services;

namespace OpenUtauMobile.Linux;

internal sealed class LinuxExternalUrlLauncher : IExternalUrlLauncher
{
    private static readonly string[] LauncherCommands = ["xdg-open", "gio"];

    public Task<ExternalUrlLaunchResult> LaunchAsync(Uri uri)
    {
        Exception? lastException = null;
        foreach (string launcherCommand in LauncherCommands)
        {
            try
            {
                ProcessStartInfo startInfo = new()
                {
                    FileName = launcherCommand,
                    UseShellExecute = false
                };
                if (launcherCommand == "gio")
                {
                    startInfo.ArgumentList.Add("open");
                }
                startInfo.ArgumentList.Add(uri.AbsoluteUri);

                Process? process = Process.Start(startInfo);
                return Task.FromResult(process == null
                    ? ExternalUrlLaunchResult.Failed($"{launcherCommand} did not start.")
                    : ExternalUrlLaunchResult.Success);
            }
            catch (Exception exception)
            {
                lastException = exception;
            }
        }

        string errorMessage = lastException?.Message ?? "No desktop URL launcher is available.";
        return Task.FromResult(ExternalUrlLaunchResult.Failed(errorMessage));
    }
}
