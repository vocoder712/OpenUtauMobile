using System;
using System.Threading.Tasks;
using Foundation;
using OpenUtauMobile.Services;
using UIKit;

namespace OpenUtauMobile.iOS;

internal sealed class IosExternalUrlLauncher : IExternalUrlLauncher
{
    public Task<ExternalUrlLaunchResult> LaunchAsync(Uri uri)
    {
        TaskCompletionSource<ExternalUrlLaunchResult> completionSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        UIApplication.SharedApplication.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                NSUrl? nativeUrl = NSUrl.FromString(uri.AbsoluteUri);
                if (nativeUrl == null)
                {
                    completionSource.SetResult(ExternalUrlLaunchResult.Failed("iOS could not create the URL."));
                    return;
                }

                bool opened = await UIApplication.SharedApplication.OpenUrlAsync(
                    nativeUrl,
                    new UIApplicationOpenUrlOptions());
                completionSource.SetResult(opened
                    ? ExternalUrlLaunchResult.Success
                    : ExternalUrlLaunchResult.Failed("iOS did not accept the URL."));
            }
            catch (Exception exception)
            {
                completionSource.SetResult(ExternalUrlLaunchResult.Failed(exception.Message));
            }
        });
        return completionSource.Task;
    }
}
