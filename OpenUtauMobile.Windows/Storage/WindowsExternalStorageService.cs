using OpenUtauMobile.Storage;

namespace OpenUtauMobile.Windows.Storage;

public class WindowsExternalStorageService : IExternalStorageService
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

