using OpenUtau.Core.Ustx;

namespace OpenUtauMobile.ViewModels;

/// <summary>
/// 参数选项项（用于绑定到参数选择器列表与菜单）。
/// </summary>
public sealed class ExpressionOption
{
    /// <summary>
    /// 参数缩写标识键。
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// 显示名称。
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// 对应的工程参数描述符，无参数项时为 null。
    /// </summary>
    public UExpressionDescriptor? Descriptor { get; }

    public ExpressionOption(string key, string displayName, UExpressionDescriptor? descriptor)
    {
        Key = key;
        DisplayName = displayName;
        Descriptor = descriptor;
    }
}
