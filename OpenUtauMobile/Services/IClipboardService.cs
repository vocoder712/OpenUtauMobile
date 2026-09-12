using System.Threading.Tasks;

namespace OpenUtauMobile.Services;

/// <summary>
/// 跨平台剪贴板写入服务
/// </summary>
public interface IClipboardService
{
    /// <summary>
    /// 读取系统剪贴板中的纯文本
    /// </summary>
    /// <returns>剪贴板文本；没有文本或读取失败时为 null</returns>
    Task<string?> GetTextAsync();

    /// <summary>
    /// 将纯文本写入系统剪贴板
    /// </summary>
    /// <param name="text">待写入的文本</param>
    /// <returns>写入是否成功</returns>
    Task<bool> SetTextAsync(string text);
}
