using System.Threading.Tasks;

namespace OpenUtauMobile.Services.Platform;

/// <summary>由平台宿主提供的异常退出记录收集与导出能力。</summary>
public interface IPlatformCrashLogService
{
    /// <summary>收集尚未处理的历史异常退出记录。</summary>
    Task CollectPreviousExitAsync();

    /// <summary>将已收集的退出记录和当前设备信息导出为压缩包。</summary>
    /// <returns>压缩包内的异常退出记录数量。</returns>
    Task<int> ExportAsync(string destination);
}
