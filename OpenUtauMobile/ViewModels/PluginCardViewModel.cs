using OpenUtauMobile.Services;
using OpenUtauMobile.Services.SingerPlugins;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtauMobile.ViewModels;

/// <summary>文件信息与动态设置能力分离，不因状态变化重建整张卡片。</summary>
public sealed class PluginCardViewModel(PluginFileEntry entry) : ReactiveObject
{
    public PluginFileEntry Entry { get; } = entry;
    public string FileName => Entry.FileName;
    public string RelativePath => Entry.RelativePath;
    public string SizeText => Entry.SizeText;
    public bool IsPendingDeletion => Entry.IsPendingDeletion;
    internal RegisteredSingerPlugin? Plugin { get; } = PluginWebSettingsService.FindPlugin(entry.RelativePath);
    [Reactive] public bool HasWebSettings { get; internal set; }
    [Reactive] public string SettingsTitle { get; internal set; } = "";
}
