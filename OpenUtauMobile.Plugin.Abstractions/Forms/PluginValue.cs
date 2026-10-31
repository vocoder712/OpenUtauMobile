using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace OpenUtauMobile.Plugin.Abstractions
{
    /// <summary>安装表单与设置共享的类型化值，不接受任意 CLR 对象或界面类型。</summary>
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
    [JsonDerivedType(typeof(PluginValue.Text), "text")]
    [JsonDerivedType(typeof(PluginValue.Boolean), "boolean")]
    [JsonDerivedType(typeof(PluginValue.Number), "number")]
    [JsonDerivedType(typeof(PluginValue.Selection), "selection")]
    [JsonDerivedType(typeof(PluginValue.MultipleSelection), "multipleSelection")]
    [JsonDerivedType(typeof(PluginValue.Path), "path")]
    public abstract record PluginValue
    {
        /// <summary>普通文本。</summary>
        public sealed record Text(string Value) : PluginValue;
        /// <summary>布尔选择。</summary>
        public sealed record Boolean(bool Value) : PluginValue;
        /// <summary>有限精度数值。</summary>
        public sealed record Number(decimal Value) : PluginValue;
        /// <summary>选项稳定 ID。</summary>
        public sealed record Selection(string Value) : PluginValue;
        /// <summary>多个选项稳定 ID。</summary>
        public sealed record MultipleSelection(ImmutableArray<string> Values) : PluginValue;
        /// <summary>经宿主选择器解析的本地文件或目录。</summary>
        public sealed record Path(string Value, bool IsDirectory) : PluginValue;
    }
}
