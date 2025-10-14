using Microsoft.Maui;
using OpenUtauMobile.Utils.Permission;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.Serialization;

namespace OpenUtauMobile.Platforms.Windows.Utils.Permission
{
    /// <summary>
    /// 在Android上检查和请求管理外部存储权限（MANAGE_EXTERNAL_STORAGE）
    /// </summary>
    public class ExternalStorageService : IExternalStorageService
    {
        public Task<bool> HasManageExternalStoragePermissionAsync()
        {
            return Task.FromResult(true); // Windows上不需要权限
        }

        public void RequestManageExternalStoragePermission()
        {
            
        }
    }
}
