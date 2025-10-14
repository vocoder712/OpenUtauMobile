using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenUtauMobile.Utils.Permission
{
    public interface IExternalStorageService
    {
        public Task<bool> HasManageExternalStoragePermissionAsync();
        void RequestManageExternalStoragePermission();
    }
}
