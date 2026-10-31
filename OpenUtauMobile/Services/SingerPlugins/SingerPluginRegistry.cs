using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Threading;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Plugin.Abstractions;

namespace OpenUtauMobile.Services.SingerPlugins
{
    /// <summary>持有歌手能力快照，单个插件通过全部校验后才发布登记。</summary>
    public sealed class SingerPluginRegistry
    {
        private readonly object gate = new();
        private SingerPluginSnapshot snapshot = CreateBuiltinSnapshot();

        /// <summary>不可变的当前能力目录；不会暴露尚未提交的插件。</summary>
        public SingerPluginSnapshot Snapshot => Volatile.Read(ref snapshot);

        /// <summary>收集并提交插件；插件代码在提交锁之外执行，避免锁内回调。</summary>
        internal void Register(ISingerPlugin plugin, Assembly assembly)
        {
            SingerPluginDescriptor descriptor = plugin.Descriptor
                ?? throw new InvalidOperationException("插件没有身份描述。");
            SingerPluginRegistration batch = new(descriptor, assembly);
            try
            {
                plugin.Register(batch);
            }
            finally
            {
                batch.Close();
            }
            batch.Validate();
            lock (gate)
            {
                SingerPluginSnapshot current = snapshot;
                RequireUnique(current.Plugins.ContainsKey(descriptor.Id), "插件 ID", descriptor.Id);
                foreach (RegisteredSingerType type in batch.Types)
                {
                    RequireUnique(current.Types.ContainsKey(type.Descriptor.Id), "类型 ID", type.Descriptor.Id);
                    RequireUnique(current.TypesByBridge.ContainsKey(type.Descriptor.BridgeValue),
                        "类型桥接值", ((int)type.Descriptor.BridgeValue).ToString());
                }
                foreach (RegisteredSingerProvider provider in batch.Providers)
                {
                    RequireUnique(current.Providers.ContainsKey(provider.Id), "提供方 ID", provider.Id);
                }
                foreach (RegisteredSingerInstaller installer in batch.Installers)
                {
                    RequireUnique(current.Installers.ContainsKey(installer.Descriptor.Id), "安装器 ID", installer.Descriptor.Id);
                }

                // 所有构造都成功后才替换快照，失败不会遗留半个插件的能力。
                SingerPluginSnapshot next = new(
                    current.Plugins.Add(descriptor.Id, descriptor),
                    current.Types.AddRange(batch.Types.Select(type => new KeyValuePair<string, RegisteredSingerType>(type.Descriptor.Id, type))),
                    current.TypesByBridge.AddRange(batch.Types.Select(type => new KeyValuePair<USingerType, RegisteredSingerType>(type.Descriptor.BridgeValue, type))),
                    current.Providers.AddRange(batch.Providers.Select(provider => new KeyValuePair<string, RegisteredSingerProvider>(provider.Id, provider))),
                    current.Installers.AddRange(batch.Installers.Select(installer => new KeyValuePair<string, RegisteredSingerInstaller>(installer.Descriptor.Id, installer))),
                    batch.Settings is { } settings ? current.Settings.Add(descriptor.Id, settings) : current.Settings);
                Volatile.Write(ref snapshot, next);
            }
        }

        /// <summary>按已验证类型返回渲染器；不可用或未登记类型返回空数组。</summary>
        public string[] GetSupportedRenderers(USingerType bridge)
        {
            if (!Snapshot.TypesByBridge.TryGetValue(bridge, out RegisteredSingerType? type) || !type.Descriptor.IsAvailable)
            {
                return [];
            }
            return type.PluginId == "opum.builtin"
                ? Renderers.GetSupportedRenderers(bridge).ToArray()
                : type.Descriptor.RendererIds.ToArray();
        }

        /// <summary>安全取得默认渲染器，保留 Classic 的宿主偏好。</summary>
        public string GetDefaultRenderer(USingerType bridge)
        {
            SingerPluginSnapshot current = Snapshot;
            if (!current.TypesByBridge.TryGetValue(bridge, out RegisteredSingerType? type) || !type.Descriptor.IsAvailable)
            {
                return string.Empty;
            }
            return type.PluginId == "opum.builtin"
                ? Renderers.GetDefaultRenderer(bridge)
                : type.Descriptor.DefaultRendererId ?? string.Empty;
        }

        private static void RequireUnique(bool exists, string kind, string id)
        {
            if (exists)
            {
                throw new InvalidOperationException($"{kind} 冲突：{id}");
            }
        }

        /// <summary>固定内置身份，不把已加载的第三方 renderer 纳入内置描述。</summary>
        private static SingerPluginSnapshot CreateBuiltinSnapshot()
        {
            SingerTypeDescriptor[] descriptors =
            [
                new("builtin:utau", USingerType.Classic, "UTAU",
                    [Renderers.WORLDLINE_R, Renderers.WORLDLINE_R11, Renderers.WORLDLINE_R2, Renderers.CLASSIC], Renderers.WORLDLINE_R),
                new("builtin:enunu", USingerType.Enunu, "ENUNU", [Renderers.ENUNU], Renderers.ENUNU),
                new("builtin:vogen", USingerType.Vogen, "Vogen", [Renderers.VOGEN], Renderers.VOGEN),
                new("builtin:diffsinger", USingerType.DiffSinger, "DiffSinger", [Renderers.DIFFSINGER], Renderers.DIFFSINGER),
                new("builtin:voicevox", USingerType.Voicevox, "VOICEVOX", [Renderers.VOICEVOX], Renderers.VOICEVOX),
                new("builtin:neutrino", USingerType.Neutrino, "NEUTRINO", [Renderers.NEUTRINO], Renderers.NEUTRINO),
            ];
            RegisteredSingerType[] types = descriptors.Select(descriptor => new RegisteredSingerType("opum.builtin", descriptor)).ToArray();
            return new(
                ImmutableDictionary<string, SingerPluginDescriptor>.Empty.WithComparers(StringComparer.Ordinal)
                    .Add("opum.builtin", new("opum.builtin", "OpenUtau Mobile")),
                types.ToImmutableDictionary(type => type.Descriptor.Id, StringComparer.Ordinal),
                types.ToImmutableDictionary(type => type.Descriptor.BridgeValue),
                ImmutableDictionary<string, RegisteredSingerProvider>.Empty.WithComparers(StringComparer.Ordinal),
                ImmutableDictionary<string, RegisteredSingerInstaller>.Empty.WithComparers(StringComparer.Ordinal),
                ImmutableDictionary<string, RegisteredPluginSettings>.Empty.WithComparers(StringComparer.Ordinal));
        }
    }
}
