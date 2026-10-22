using System;
using OpenUtau.Core;
using OpenUtau.Core.ExpressionGraph;
using OpenUtau.Core.Ustx;

namespace OpenUtauMobile.Services.ExpressionGraph;

/// <summary>同一工程的编辑窗口共享节点快照；工程重新加载时立即丢弃，不写系统剪贴板。</summary>
internal sealed class GraphNodeClipboard : ICmdSubscriber
{
    private static readonly GraphNodeClipboard instance = new();
    private UProject? project;
    private UExpressionGraph? snapshot;

    private GraphNodeClipboard() => DocManager.Inst.AddSubscriber(this);

    /// <summary>通知当前编辑器刷新粘贴可用状态。</summary>
    internal static event Action? Changed;

    /// <summary>查询当前工程是否有可用快照。</summary>
    internal static bool HasContent(UProject project) => ReferenceEquals(instance.project, project) && instance.snapshot != null;

    /// <summary>保存独立副本，不保留源图的可写引用。</summary>
    internal static void Set(UProject project, UExpressionGraph snapshot)
    {
        if (!ReferenceEquals(DocManager.Inst.Project, project)) return;
        instance.project = project;
        instance.snapshot = snapshot.Clone();
        Changed?.Invoke();
    }

    /// <summary>每次读取再克隆，预检与粘贴不能更改已复制快照。</summary>
    internal static UExpressionGraph? Read(UProject project) => HasContent(project) ? instance.snapshot!.Clone() : null;

    /// <summary>即使重新加载同一工程实例，也不能沿用旧剪贴板。</summary>
    public void OnNext(UCommand cmd, bool isUndo)
    {
        if (cmd is not LoadProjectNotification) return;
        project = null; snapshot = null;
        Changed?.Invoke();
    }
}
