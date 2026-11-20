using System.Collections.Immutable;

namespace OpenUtauMobile.Plugin.Abstractions
{
    /// <summary>由宿主现有控件承载的输入种类。</summary>
    public enum PluginFieldKind
    {
        Text,
        Boolean,
        Number,
        Selection,
        MultipleSelection,
        File,
        Directory,
    }

    /// <summary>字段选项，身份不随显示语言变化。</summary>
    public sealed record PluginFieldOption(string Id, string Label);

    /// <summary>声明式字段；可见状态由插件计算，不解释任意脚本。</summary>
    public sealed record PluginFieldDescriptor(
        string Id,
        string Label,
        PluginFieldKind Kind,
        PluginValue? DefaultValue = null,
        string? Description = null,
        bool IsRequired = false,
        bool IsVisible = true,
        bool IsEnabled = true,
        ImmutableArray<PluginFieldOption> Options = default,
        decimal? Minimum = null,
        decimal? Maximum = null,
        ImmutableArray<string> FileExtensions = default);
}
