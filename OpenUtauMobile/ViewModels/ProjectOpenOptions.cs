namespace OpenUtauMobile.ViewModels;

/// <summary>
/// 工程打开方式。
/// </summary>
public enum ProjectOpenKind
{
    Normal,
    Template,
    ExternalCopy,
}

/// <summary>
/// 编辑器载入工程时使用的参数。
/// </summary>
public sealed record ProjectOpenOptions(
    string Path = "",
    ProjectOpenKind Kind = ProjectOpenKind.Normal,
    bool DeleteSourceAfterRead = false);
