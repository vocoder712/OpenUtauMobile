using Avalonia.Controls;
using OpenUtauMobile.ViewModels;
using OpenUtauMobile.Views;

namespace OpenUtauMobile.DesktopUI.Views
{
    /// <summary>桌面窗口分别承载内置向导与插件声明式页面。</summary>
    internal sealed class DesktopSingerSetupPage : UserControl
    {
        public DesktopSingerSetupPage(BuiltinSingerSetupViewModel model)
        {
            DataContext = model;
            BuiltinSingerSetupView view = model switch
            {
                UtauSingerSetupViewModel => new UtauSingerSetupView(),
                DiffSingerSetupViewModel => new DiffSingerSetupView(),
                EnunuSingerSetupViewModel => new EnunuSingerSetupView(),
                NeutrinoSingerSetupViewModel => new NeutrinoSingerSetupView(),
                _ => new SingleFileSingerSetupView(),
            };
            view.DataContext = model;
            Content = view;
        }

        public DesktopSingerSetupPage(PluginSingerSetupViewModel model)
        {
            DataContext = model;
            Content = new PluginSingerSetupView { DataContext = model };
        }
    }
}
