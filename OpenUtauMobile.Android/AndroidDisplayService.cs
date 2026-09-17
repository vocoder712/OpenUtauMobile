using System;
using Android.Views;
using AndroidX.Core.View;
using OpenUtau.Core.Util;
using OpenUtauMobile.Services.Platform;

namespace OpenUtauMobile.Android;

/// <summary>应用 Android 系统栏与屏幕常亮偏好。</summary>
internal sealed class AndroidDisplayService : IPlatformDisplayService
{
    private readonly Func<MainActivity?> _getActivity;
    private bool _isEditorActive;

    public AndroidDisplayService(Func<MainActivity?> getActivity)
    {
        _getActivity = getActivity;
    }

    public void SetEditorActive(bool isEditorActive)
    {
        _isEditorActive = isEditorActive;
        Refresh();
    }

    public void Refresh()
    {
        MainActivity? activity = _getActivity();
        if (activity == null)
        {
            return;
        }

        activity.RunOnUiThread(() => Apply(activity));
    }

    private void Apply(MainActivity activity)
    {
        Window? window = activity.Window;
        View? decorView = window?.DecorView;
        if (window == null || decorView == null)
        {
            return;
        }

        int fullscreenMode = Preferences.Default.AndroidFullscreenMode;
        bool useFullscreen = fullscreenMode switch
        {
            1 => _isEditorActive,
            2 => false,
            _ => true,
        };

        WindowInsetsControllerCompat? controller = WindowCompat.GetInsetsController(window, decorView);
        if (controller != null)
        {
            if (useFullscreen)
            {
                controller.Hide(WindowInsetsCompat.Type.SystemBars());
                controller.SystemBarsBehavior =
                    WindowInsetsControllerCompat.BehaviorShowTransientBarsBySwipe;
            }
            else
            {
                controller.Show(WindowInsetsCompat.Type.SystemBars());
            }
        }

        bool keepScreenAwake =
            _isEditorActive && Preferences.Default.AndroidKeepScreenAwakeWhileEditing;
        if (keepScreenAwake)
        {
            window.AddFlags(WindowManagerFlags.KeepScreenOn);
        }
        else
        {
            window.ClearFlags(WindowManagerFlags.KeepScreenOn);
        }
    }
}
