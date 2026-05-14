using Android.App;
using Android.Content;
using Android.OS;
using Android;
using Android.Provider;
using OpenUtauMobile.Storage;

namespace OpenUtauMobile.Android.Storage;

public class AndroidExternalStorageService : IExternalStorageService
{
    public bool HasManageExternalStoragePermissionAsync()
    {
        // Android 11 及以上：管理所有文件权限
        if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
        {
            return Environment.IsExternalStorageManager;
        }

        Context context = Application.Context;
        if (context is Activity activity)
        {
            // Android 10 及以下
            string readPermission = Manifest.Permission.ReadExternalStorage;
            string writePermission = Manifest.Permission.WriteExternalStorage;

            bool hasRead = activity.CheckSelfPermission(readPermission) == global::Android.Content.PM.Permission.Granted;
            bool hasWrite = activity.CheckSelfPermission(writePermission) == global::Android.Content.PM.Permission.Granted;

            return hasRead && hasWrite;
        }
        return false;
    }

    public void RequestManageExternalStoragePermission()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.R)
        {
            // Android 10 及以下，申请读写权限
            Context context = Application.Context;
            if (context is Activity activity)
            {
                string[] permissions =
                [
                    Manifest.Permission.ReadExternalStorage,
                    Manifest.Permission.WriteExternalStorage
                ];
                activity.RequestPermissions(permissions, 0);
            }
        }
        if (Build.VERSION.SdkInt >= BuildVersionCodes.R && !Environment.IsExternalStorageManager)
        {
            var intent = new Intent(Settings.ActionManageAllFilesAccessPermission);
            intent.AddFlags(ActivityFlags.NewTask);
            Application.Context.StartActivity(intent);
        }
    }
}