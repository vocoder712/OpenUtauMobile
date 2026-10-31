using System;
using System.Globalization;
using System.Collections.Generic;
using Avalonia.Media;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Plugin.Abstractions;
using OpenUtauMobile.Themes.OpenUtauMobile.Runtime.Resources;
using OpenUtauMobile.Themes.OpenUtauMobile.Runtime;
using ReactiveUI;

namespace OpenUtauMobile.Services.SingerPlugins
{
    /// <summary>统一查询类型名称与明暗主题颜色。</summary>
    public static class SingerTypePresentation
    {
        private static readonly Dictionary<USingerType, SolidColorBrush> brushes = [];

        static SingerTypePresentation()
        {
            MessageBus.Current.Listen<ThemeChangedEvent>().Subscribe(_ =>
            {
                foreach (KeyValuePair<USingerType, SolidColorBrush> item in brushes) item.Value.Color = ResolveColor(item.Key);
            });
        }
        /// <summary>依次使用当前语言、父语言和默认名称。</summary>
        public static string Name(USingerType bridge)
        {
            if (!SingerPluginService.Registry.Snapshot.TypesByBridge.TryGetValue(bridge, out RegisteredSingerType? type))
                return bridge.ToString();
            if (type.Descriptor.LocalizedNames is { } names)
                for (CultureInfo culture = CultureInfo.CurrentUICulture; culture != CultureInfo.InvariantCulture; culture = culture.Parent)
                    if (names.TryGetValue(culture.Name, out string? name)) return name;
            return type.Descriptor.DisplayName;
        }

        /// <summary>外部类型颜色作为数据颜色使用，缺省沿用未知类型语义色。</summary>
        public static IBrush Brush(USingerType bridge)
        {
            // 内置类型继续使用原有主题画刷，不复制颜色或改动标签前景。
            string? builtinRole = bridge switch
            {
                USingerType.Classic => "Classic",
                USingerType.Enunu => "Enunu",
                USingerType.DiffSinger => "DiffSinger",
                USingerType.Vogen => "Vogen",
                USingerType.Voicevox => "Voicevox",
                USingerType.Neutrino => "Neutrino",
                _ => null,
            };
            if (builtinRole != null) return ThemeResources.GetBrush($"Sem.Color.Singer.{builtinRole}");
            if (!SingerPluginService.Registry.Snapshot.TypesByBridge.TryGetValue(bridge, out RegisteredSingerType? type) ||
                type.Descriptor.Color == null) return ThemeResources.GetBrush("Sem.Color.Singer.Unknown");
            if (!brushes.TryGetValue(bridge, out SolidColorBrush? brush)) brushes.Add(bridge, brush = new(ResolveColor(bridge)));
            return brush;
        }

        private static Color ResolveColor(USingerType bridge)
        {
            if (SingerPluginService.Registry.Snapshot.TypesByBridge.TryGetValue(bridge, out RegisteredSingerType? type) &&
                type.Descriptor.Color is SingerTypeColor color)
            {
                uint argb = ThemeResources.IsDarkMode ? color.DarkArgb : color.LightArgb;
                return Color.FromUInt32(argb | 0xff000000);
            }
            return ThemeResources.GetColor("Sem.Color.Singer.Unknown");
        }
    }
}
