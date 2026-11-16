using System.Collections.Immutable;
using OpenUtau.Core.Ustx;

namespace OpenUtauMobile.Plugin.Abstractions
{
    /// <summary>类型标识的明暗颜色；采用 ARGB 数值，不依赖界面画刷。</summary>
    public sealed record SingerTypeColor(uint LightArgb, uint DarkArgb);

    /// <summary>可动态登记的类型；不可用占位类型不得发布可合成歌手。</summary>
    public sealed record SingerTypeDescriptor(
        string Id,
        USingerType BridgeValue,
        string DisplayName,
        ImmutableArray<string> RendererIds,
        string? DefaultRendererId,
        SingerTypeColor? Color = null,
        bool IsAvailable = true,
        string? UnavailableReason = null,
        ImmutableDictionary<string, string>? LocalizedNames = null);
}
