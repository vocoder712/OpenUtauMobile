using Android.Content;
using Android.OS;
using Android.Provider;
using OpenUtauMobile.Utils.Permission;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Environment = Android.OS.Environment;
using Application = Android.App.Application;

namespace OpenUtauMobile.Platforms.Android.Utils.Permission
{
    /// <summary>
    /// 在Android上检查和请求管理外部存储权限（MANAGE_EXTERNAL_STORAGE）
    /// </summary>
    public class ExternalStorageService : IExternalStorageService
    {
        public async Task<bool> HasManageExternalStoragePermissionAsync()
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.R) // Android 11及以上
            {
                return Environment.IsExternalStorageManager;
            }
            else // Android 10及以下
            {
                var storageReadStatus = await Permissions.CheckStatusAsync<Permissions.StorageRead>(); // 读
                if (storageReadStatus != PermissionStatus.Granted)
                {
                    storageReadStatus = await Permissions.RequestAsync<Permissions.StorageRead>();
                }
                var storageWriteStatus = await Permissions.CheckStatusAsync<Permissions.StorageWrite>(); // 写
                if (storageWriteStatus != PermissionStatus.Granted)
                {
                    storageWriteStatus = await Permissions.RequestAsync<Permissions.StorageWrite>();
                }
                return storageReadStatus == PermissionStatus.Granted && storageWriteStatus == PermissionStatus.Granted; // 返回是否被授予读写权限
            }
        }

        public void RequestManageExternalStoragePermission()
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.R && !Environment.IsExternalStorageManager)
            {
                var intent = new Intent(Settings.ActionManageAllFilesAccessPermission);
                intent.AddFlags(ActivityFlags.NewTask);
                Application.Context.StartActivity(intent);
            }
        }
    }
}
