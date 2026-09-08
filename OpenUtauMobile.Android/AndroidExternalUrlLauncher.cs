using System;
using System.Threading.Tasks;
using Android.Content;
using OpenUtauMobile.Services;

namespace OpenUtauMobile.Android;

internal sealed class AndroidExternalUrlLauncher : IExternalUrlLauncher
{
    private readonly Func<MainActivity?> _getActivity;

    public AndroidExternalUrlLauncher(Func<MainActivity?> getActivity)
    {
        _getActivity = getActivity;
    }

    public Task<ExternalUrlLaunchResult> LaunchAsync(Uri uri)
    {
        MainActivity? activity = _getActivity();
        if (activity == null)
        {
            return Task.FromResult(ExternalUrlLaunchResult.Failed("The Android activity is unavailable."));
        }

        TaskCompletionSource<ExternalUrlLaunchResult> completionSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        activity.RunOnUiThread(() =>
        {
            try
            {
                Intent intent = new(Intent.ActionView, global::Android.Net.Uri.Parse(uri.AbsoluteUri));
                activity.StartActivity(intent);
                completionSource.SetResult(ExternalUrlLaunchResult.Success);
            }
            catch (Exception exception)
            {
                completionSource.SetResult(ExternalUrlLaunchResult.Failed(exception.Message));
            }
        });
        return completionSource.Task;
    }
}
