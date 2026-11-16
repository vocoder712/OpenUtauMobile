namespace OpenUtauMobile.Plugin.Abstractions
{
    /// <summary>需要用户关注的插件警告；文本由插件本地化，代码在插件内保持稳定。</summary>
    public sealed record PluginWarning(string Code, string Message, string? Details = null);

    /// <summary>宿主提供的用户通知入口，与仅写日志的 IPluginLog 分开。</summary>
    public interface IPluginNotifications
    {
        /// <summary>发布或更新同代码警告；宿主自动按当前插件隔离身份并调度到界面线程。</summary>
        void PublishWarning(PluginWarning warning);

        /// <summary>问题恢复后清除当前插件的指定警告，不影响其他插件或宿主通知。</summary>
        void ClearWarning(string code);
    }
}
