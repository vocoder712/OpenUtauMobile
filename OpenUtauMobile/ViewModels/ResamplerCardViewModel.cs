using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services;
using ReactiveUI;

namespace OpenUtauMobile.ViewModels;

/// <summary>列表仅展示文件和引擎识别状态，移除操作由页面命令负责。</summary>
public sealed class ResamplerCardViewModel(ResamplerFileEntry entry) : ReactiveObject
{
    public string FileName => entry.FileName;
    public string RelativePath => entry.RelativePath;
    /// <summary>位于子目录时才单独显示相对路径，避免与文件名重复。</summary>
    public bool HasFolder => entry.RelativePath != entry.FileName;
    public string SizeText => entry.SizeText;
    /// <summary>引擎已识别该文件；不代表程序一定能在当前设备上运行。</summary>
    public bool IsAvailable => entry.IsAvailable;
    public string StatusText => L.S(IsAvailable ? "ResamplerManager.Available" : "ResamplerManager.Unavailable");
}
