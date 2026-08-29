namespace OpenUtauMobile.Services;

/// <summary>
/// 外部网页启动结果
/// </summary>
/// <param name="Succeeded">是否已交给平台处理</param>
/// <param name="ErrorMessage">失败原因</param>
public sealed record ExternalUrlLaunchResult(bool Succeeded, string? ErrorMessage)
{
    public static ExternalUrlLaunchResult Success { get; } = new(true, null);

    /// <summary>
    /// 创建失败结果
    /// </summary>
    /// <param name="errorMessage">失败原因</param>
    /// <returns>失败结果</returns>
    public static ExternalUrlLaunchResult Failed(string errorMessage)
    {
        return new ExternalUrlLaunchResult(false, errorMessage);
    }
}
