using System;
using System.Threading.Tasks;

namespace OpenUtauMobile.Services;

/// <summary>
/// 外部网页链接服务
/// </summary>
public static class ExternalUrlService
{
    /// <summary>
    /// 验证并在平台默认浏览器中打开链接
    /// </summary>
    /// <param name="url">HTTP 或 HTTPS 链接</param>
    /// <returns>启动结果</returns>
    public static Task<ExternalUrlLaunchResult> OpenAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return Task.FromResult(ExternalUrlLaunchResult.Failed("Only HTTP and HTTPS URLs are supported."));
        }

        IExternalUrlLauncher? launcher = ServiceHub.ExternalUrlLauncher;
        if (launcher == null)
        {
            return Task.FromResult(ExternalUrlLaunchResult.Failed("External URL launching is unavailable on this platform."));
        }

        return launcher.LaunchAsync(uri);
    }
}
