namespace OpenUtauMobile.Plugin.Abstractions
{
    /// <summary>插件独占的持久化目录，配置格式和迁移由插件自己管理。</summary>
    public sealed record PluginStorageContext(string DataDirectory)
    {
        /// <summary>插件资源目录，与旧版资源位置保持一致。</summary>
        public string ResourceDirectory => Path.Combine(DataDirectory, "Resources");
    }

    /// <summary>每次查询和打开设置时提供的宿主上下文。</summary>
    public sealed record PluginWebSettingsContext(PluginStorageContext Storage, IPluginLog Log)
    {
        /// <summary>宿主服务同时实现日志与通知接口，保持既有构造参数不变。</summary>
        public IPluginNotifications Notifications => (IPluginNotifications)Log;
    }

    /// <summary>当前可用的 Web 设置声明；查询返回 null 表示此刻没有设置入口。</summary>
    public sealed record PluginWebSettingsDescriptor(string Title);

    /// <summary>插件创建的网页会话；宿主离开页面时等待释放本地服务等资源。</summary>
    public interface IPluginWebSettingsSession : IAsyncDisposable
    {
        /// <summary>可由系统 WebView 或外部浏览器打开的 HTTP/HTTPS 地址。</summary>
        Uri Address { get; }
        /// <summary>关闭设置后是否重新发现歌手，默认刷新。</summary>
        bool ReloadResourcesOnClose => true;
    }
}
