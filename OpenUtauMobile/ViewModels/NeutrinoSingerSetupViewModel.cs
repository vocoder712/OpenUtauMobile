using System.Linq;
using OpenUtauMobile.Services;

namespace OpenUtauMobile.ViewModels
{
    /// <summary>用户明确选择的 NEUTRINO 安装向导，复用编码及安装生命周期。</summary>
    public sealed class NeutrinoSingerSetupViewModel(MainViewModel navigator, string sourcePath)
        : BuiltinSingerSetupViewModel(navigator, BuiltinSingerInstallers.All.Single(item => item.Id == "builtin:neutrino"), sourcePath);
}
