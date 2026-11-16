using OpenUtauMobile.Services;

namespace OpenUtauMobile.ViewModels
{
    /// <summary>Vogen 文件的内置安装确认，不进入编码或类型选择向导。</summary>
    public sealed class SingleFileSingerSetupViewModel(MainViewModel navigator, BuiltinSingerInstaller installer, string sourcePath)
        : BuiltinSingerSetupViewModel(navigator, installer, sourcePath);
}
