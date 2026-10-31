namespace OpenUtauMobile.Plugin.Abstractions
{
    /// <summary>宿主串行驱动的安装会话；临时目录最终由宿主统一清理。</summary>
    public interface ISingerInstallSession : IAsyncDisposable
    {
        /// <summary>初始化分析或返回首个输入步骤，不写正式目录。</summary>
        ValueTask<InstallView> AnalyzeAsync(IProgress<InstallProgress> progress, CancellationToken cancellationToken);
        /// <summary>校验当前步骤并前进，错误返回到同一步骤。</summary>
        ValueTask<InstallView> NextAsync(InstallSubmission submission, IProgress<InstallProgress> progress,
            CancellationToken cancellationToken);
        /// <summary>恢复上一步；过期状态版本必须被拒绝。</summary>
        ValueTask<InstallView> BackAsync(long expectedRevision, CancellationToken cancellationToken);
        /// <summary>验证最终身份、目标和输入，生成可消费一次的安装计划。</summary>
        ValueTask<PreparedInstall> PrepareAsync(InstallTargetSelection target, long expectedRevision,
            IProgress<InstallProgress> progress, CancellationToken cancellationToken);
        /// <summary>只提交已经校验的当前计划，不能重新解包替换预检内容。</summary>
        ValueTask<InstallResult> InstallAsync(Guid planId, IProgress<InstallProgress> progress,
            CancellationToken cancellationToken);
    }
}
