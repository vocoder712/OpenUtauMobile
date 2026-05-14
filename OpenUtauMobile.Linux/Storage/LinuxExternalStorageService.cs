using OpenUtauMobile.Storage;

namespace OpenUtauMobile.Linux.Storage;

public class LinuxExternalStorageService : IExternalStorageService
{
    public bool HasManageExternalStoragePermissionAsync()
    {
        return true;
    }

    public void RequestManageExternalStoragePermission()
    {
        // No action needed on desktop.
    }
}

