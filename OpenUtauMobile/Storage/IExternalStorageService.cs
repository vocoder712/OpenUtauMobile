namespace OpenUtauMobile.Storage;

/// <summary>
/// 外部存储权限接口，主要面向 Android
/// </summary>
public interface IExternalStorageService
{
    public bool HasManageExternalStoragePermissionAsync();
    void RequestManageExternalStoragePermission();
}