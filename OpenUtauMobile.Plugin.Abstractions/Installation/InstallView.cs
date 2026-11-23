using System.Collections.Immutable;

namespace OpenUtauMobile.Plugin.Abstractions
{
    /// <summary>步骤内容的宿主展示方式。</summary>
    public enum InstallContentKind
    {
        Information,
        Image,
        License,
        FilePreview,
    }

    /// <summary>只读内容；图片位置引用会话内相对路径。</summary>
    public sealed record InstallContent(InstallContentKind Kind, string Title, string Value);

    /// <summary>动态步骤，数量和分支由安装器决定。</summary>
    public sealed record InstallStep(string Id, string Title, ImmutableArray<InstallContent> Content,
        ImmutableArray<PluginFieldDescriptor> Fields, bool IsComplete = false);

    /// <summary>页面数据快照；修改输入会使旧 Revision 和 PlanId 失效。</summary>
    public sealed record InstallView(long Revision, ImmutableArray<InstallStep> Steps, string CurrentStepId,
        bool CanGoBack, bool CanGoNext, bool CanPrepare,
        ImmutableArray<PluginDiagnostic> Diagnostics = default);

    /// <summary>提交指定状态版本的字段值。</summary>
    public sealed record InstallSubmission(string StepId, long ExpectedRevision,
        ImmutableDictionary<string, PluginValue> Values);
}
