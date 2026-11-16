using System.Linq;
using OpenUtauMobile.Services;

namespace OpenUtauMobile.ViewModels
{
    /// <summary>用户明确选择的 DiffSinger 安装向导，保留独立入口。</summary>
    public sealed class DiffSingerSetupViewModel(MainViewModel navigator, string sourcePath)
        : BuiltinSingerSetupViewModel(navigator, BuiltinSingerInstallers.All.Single(item => item.Id == "builtin:diffsinger"), sourcePath);
}
