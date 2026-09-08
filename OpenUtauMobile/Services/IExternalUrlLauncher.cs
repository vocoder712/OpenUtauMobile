using System;
using System.Threading.Tasks;

namespace OpenUtauMobile.Services;

/// <summary>
/// 打开外部网页链接
/// </summary>
public interface IExternalUrlLauncher
{
    /// <summary>
    /// 打开指定的网页链接
    /// </summary>
    /// <param name="uri">已验证的 HTTP 或 HTTPS 链接</param>
    /// <returns>启动结果</returns>
    Task<ExternalUrlLaunchResult> LaunchAsync(Uri uri);
}
