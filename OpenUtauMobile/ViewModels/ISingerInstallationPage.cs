using System.Threading.Tasks;

namespace OpenUtauMobile.ViewModels
{
    /// <summary>导航只协调安装页面的关闭，不规定其业务流程和界面。</summary>
    public interface ISingerInstallationPage
    {
        /// <summary>正在执行不能随导航丢弃的安装操作。</summary>
        bool IsBusy { get; }
        /// <summary>页面持有的后台操作和资源已经释放。</summary>
        bool IsSessionClosed { get; }
        /// <summary>返回前由页面拦截尚未取消的安装；默认保留插件会话既有关闭行为。</summary>
        bool TryLeave() => true;
        /// <summary>等待后台操作结束，再允许导航移除页面。</summary>
        Task CloseSessionAsync();
    }
}
