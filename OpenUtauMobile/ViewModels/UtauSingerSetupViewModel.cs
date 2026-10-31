using System.Linq;
using OpenUtauMobile.Services;

namespace OpenUtauMobile.ViewModels
{
    /// <summary>用户明确选择的 UTAU 安装向导，不执行歌手类型推断。</summary>
    public sealed class UtauSingerSetupViewModel(MainViewModel navigator, string sourcePath)
        : BuiltinSingerSetupViewModel(navigator, BuiltinSingerInstallers.All.Single(item => item.Id == "builtin:utau"), sourcePath);
}
