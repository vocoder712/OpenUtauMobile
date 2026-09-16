namespace OpenUtauMobile.Services.Platform;

/// <summary>平台窗口显示行为，由平台宿主负责实现。</summary>
public interface IPlatformDisplayService
{
    /// <summary>通知平台当前页面是否为编辑页。</summary>
    void SetEditorActive(bool isEditorActive);

    /// <summary>重新应用当前显示偏好。</summary>
    void Refresh();
}
