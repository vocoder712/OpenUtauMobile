using OpenUtauMobile.Storage;

namespace OpenUtauMobile.MacOS.Storage;

public class MacOSExternalStorageService : IExternalStorageService
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

