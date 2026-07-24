using Android.App;
using Android.Content;
using Android.OS;
using Android;
using Android.Provider;
using OpenUtauMobile.Storage;

namespace OpenUtauMobile.Android.Storage;

public class AndroidExternalStorageService : IExternalStorageService
{
    private const int StoragePermissionRequestCode = 1001;
    private readonly System.Func<Activity?> _activityProvider;

    public AndroidExternalStorageService(System.Func<Activity?> activityProvider)
    {
        _activityProvider = activityProvider;
    }

    public bool HasManageExternalStoragePermissionAsync()
    {
        // Android 11 及以上：管理所有文件权限
        if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
        {
            return Environment.IsExternalStorageManager;
        }

        Activity? activity = _activityProvider();
        if (activity == null)
        {
            return false;
        }

        // Android 10 及以下：检查传统外部存储读写权限
        string readPermission = Manifest.Permission.ReadExternalStorage;
        string writePermission = Manifest.Permission.WriteExternalStorage;
        bool hasRead = activity.CheckSelfPermission(readPermission) ==
                       global::Android.Content.PM.Permission.Granted;
        bool hasWrite = activity.CheckSelfPermission(writePermission) ==
                        global::Android.Content.PM.Permission.Granted;
        return hasRead && hasWrite;
    }

    public void RequestManageExternalStoragePermission()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.R)
        {
            // Android 10 及以下，申请读写权限
            Activity? activity = _activityProvider();
            if (activity == null)
            {
                return;
            }

            string[] permissions =
            [
                Manifest.Permission.ReadExternalStorage,
                Manifest.Permission.WriteExternalStorage
            ];
            activity.RequestPermissions(permissions, StoragePermissionRequestCode);
            return;
        }

        if (!Environment.IsExternalStorageManager)
        {
            Intent intent = new Intent(Settings.ActionManageAllFilesAccessPermission);
            intent.AddFlags(ActivityFlags.NewTask);
            Application.Context.StartActivity(intent);
        }
    }
}
