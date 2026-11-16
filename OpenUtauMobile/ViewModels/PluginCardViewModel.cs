using OpenUtauMobile.Services;
using OpenUtauMobile.Helpers;
using ReactiveUI;

namespace OpenUtauMobile.ViewModels;

/// <summary>列表仅展示文件和启用状态，完整操作由独立详情页负责。</summary>
public sealed class PluginCardViewModel(PluginFileEntry entry) : ReactiveObject
{
    public PluginFileEntry Entry { get; } = entry;
    public string FileName => Entry.FileName;
    public string RelativePath => Entry.RelativePath;
    public string SizeText => Entry.SizeText;
    public bool IsPendingDeletion => Entry.IsPendingDeletion;
    /// <summary>提供启用状态，供列表文字及其语义颜色保持一致。</summary>
    public bool IsEnabled => PluginActivationService.IsEnabled(RelativePath);
    public string EnabledText => L.S(IsEnabled
        ? "PluginDetails.Enabled" : "PluginDetails.Disabled");
}
