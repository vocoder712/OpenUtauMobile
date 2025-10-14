using OpenUtauMobile.Utils.Permission;

namespace OpenUtauMobile.Platforms.iOS.Utils.Permission
{
    /// <summary>
    /// iOS空实现
    /// </summary>
    public class ExternalStorageService : IExternalStorageService
    {
        public Task<bool> HasManageExternalStoragePermissionAsync()
        {
            return Task.FromResult(true);
        }

        public void RequestManageExternalStoragePermission()
        {
        }
    }
}
