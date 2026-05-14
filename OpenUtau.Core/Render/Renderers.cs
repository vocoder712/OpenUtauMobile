using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using OpenUtau.Classic;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;

namespace OpenUtau.Core.Render {
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class RendererAttribute : Attribute {
        public RendererAttribute(string name, USingerType singerType) {
            Name = name;
            SingerType = singerType;
        }

        public string Name { get; }
        public USingerType SingerType { get; }
    }

    public static class Renderers {
        public const string CLASSIC = "CLASSIC";
        public const string WORLDLINE_R = "WORLDLINE-R";
        public const string WORLDLINE_R2 = "WORLDLINE-R2";
        public const string ENUNU = "ENUNU";
        public const string VOGEN = "VOGEN";
        public const string DIFFSINGER = "DIFFSINGER";
        public const string VOICEVOX = "VOICEVOX";

        static readonly string[] classicRenderers = new[] { WORLDLINE_R, CLASSIC };
        static readonly string[] enunuRenderers = new[] { ENUNU };
        static readonly string[] vogenRenderers = new[] { VOGEN };
        static readonly string[] diffSingerRenderers = new[] { DIFFSINGER };
        static readonly string[] voicevoxRenderers = new[] { VOICEVOX };
        static readonly string[] noRenderers = new string[0];
        static readonly HashSet<string> builtinRendererNames = new HashSet<string> {
            CLASSIC,
            WORLDLINE_R,
            WORLDLINE_R2,
            ENUNU,
            VOGEN,
            DIFFSINGER,
            VOICEVOX,
        };

        sealed class RendererFactory {
            public Type type;
            public string name;
            public USingerType singerType;

            public IRenderer Create() {
                return Activator.CreateInstance(type) as IRenderer;
            }
        }

        static readonly object externalRenderersLock = new object();
        static readonly Dictionary<string, RendererFactory> externalRendererFactories = new Dictionary<string, RendererFactory>(StringComparer.Ordinal);
        static readonly Dictionary<USingerType, string[]> externalRenderersBySingerType = new Dictionary<USingerType, string[]>();
        static readonly HashSet<string> registeredAssemblyKeys = new HashSet<string>(StringComparer.Ordinal);
        static bool initialAssemblyScanCompleted;

        static Renderers() {
            AppDomain.CurrentDomain.AssemblyLoad += (_, args) => RegisterAssembly(args.LoadedAssembly);
        }

        public static string[] GetSupportedRenderers(USingerType singerType) {
            var builtin = GetBuiltinSupportedRenderers(singerType);
            var external = GetExternalSupportedRenderers(singerType);
            if (external.Length == 0) {
                return builtin;
            }
            return builtin.Concat(external).ToArray();
        }

        static string[] GetBuiltinSupportedRenderers(USingerType singerType) {
            switch (singerType) {
                case USingerType.Classic:
                    return classicRenderers;
                case USingerType.Enunu:
                    return enunuRenderers;
                case USingerType.Vogen:
                    return vogenRenderers;
                case USingerType.DiffSinger:
                    return diffSingerRenderers;
                case USingerType.Voicevox:
                    return voicevoxRenderers;
                default:
                    return noRenderers;
            }
        }

        public static List<string> getRendererOptions() {
            var options = new List<string> {
                "WORLDLINE-R",
                "Classic"
            };
            options.AddRange(GetExternalSupportedRenderers(USingerType.Classic));
            return options;
        }

        public static string GetDefaultRenderer(USingerType singerType) {
            if (Preferences.Default.DefaultRenderer == "Classic" && singerType == USingerType.Classic) {
                return CLASSIC;
            } else {
                return GetSupportedRenderers(singerType)[0];
            }
        }

        public static IRenderer CreateRenderer(string renderer) {
            if (renderer == CLASSIC) {
                return new ClassicRenderer();
            } else if (renderer == WORLDLINE_R2) {
                return new WorldlineRenderer(version: 2);
            } else if (renderer?.StartsWith(WORLDLINE_R.Substring(0, 9)) ?? false) {
                return new WorldlineRenderer(version: 1);
            } else if (renderer == ENUNU) {
                return new Enunu.EnunuRenderer();
            } else if (renderer == VOGEN) {
                return new Vogen.VogenRenderer();
            } else if (renderer == DIFFSINGER) {
                return new DiffSinger.DiffSingerRenderer();
            } else if (renderer == VOICEVOX) {
                return new Voicevox.VoicevoxRenderer();
            }
            return TryCreateExternalRenderer(renderer);
        }

        static string[] GetExternalSupportedRenderers(USingerType singerType) {
            EnsureExternalRenderersLoaded();
            lock (externalRenderersLock) {
                if (externalRenderersBySingerType.TryGetValue(singerType, out var renderers)) {
                    return renderers;
                }
            }
            return noRenderers;
        }

        static IRenderer TryCreateExternalRenderer(string renderer) {
            if (string.IsNullOrEmpty(renderer)) {
                return null;
            }
            EnsureExternalRenderersLoaded();
            lock (externalRenderersLock) {
                if (!externalRendererFactories.TryGetValue(renderer, out var factory)) {
                    return null;
                }
                try {
                    return factory.Create();
                } catch {
                    return null;
                }
            }
        }

        static void EnsureExternalRenderersLoaded() {
            lock (externalRenderersLock) {
                if (initialAssemblyScanCompleted) {
                    return;
                }
                RegisterAssembliesNoLock(AppDomain.CurrentDomain.GetAssemblies());
                initialAssemblyScanCompleted = true;
            }
        }

        static void RegisterAssembly(Assembly assembly) {
            lock (externalRenderersLock) {
                RegisterAssemblyNoLock(assembly);
            }
        }

        static void RegisterAssembliesNoLock(IEnumerable<Assembly> assemblies) {
            foreach (var assembly in assemblies) {
                RegisterAssemblyNoLock(assembly);
            }
        }

        static void RegisterAssemblyNoLock(Assembly assembly) {
            if (assembly == null || assembly == typeof(Renderers).Assembly) {
                return;
            }
            var assemblyKey = GetAssemblyKey(assembly);
            if (string.IsNullOrEmpty(assemblyKey) || !registeredAssemblyKeys.Add(assemblyKey)) {
                return;
            }
            bool changed = false;
            foreach (var type in GetLoadableTypes(assembly)) {
                changed |= TryRegisterExternalRendererNoLock(type);
            }
            if (changed) {
                RebuildExternalRendererListsNoLock();
            }
        }

        static bool TryRegisterExternalRendererNoLock(Type type) {
            if (type.IsAbstract
                || !typeof(IRenderer).IsAssignableFrom(type)
                || type.GetConstructor(Type.EmptyTypes) == null) {
                return false;
            }
            var attr = type.GetCustomAttribute<RendererAttribute>();
            if (attr == null || string.IsNullOrWhiteSpace(attr.Name)) {
                return false;
            }
            if (builtinRendererNames.Contains(attr.Name) || externalRendererFactories.ContainsKey(attr.Name)) {
                return false;
            }
            externalRendererFactories[attr.Name] = new RendererFactory {
                type = type,
                name = attr.Name,
                singerType = attr.SingerType,
            };
            return true;
        }

        static void RebuildExternalRendererListsNoLock() {
            externalRenderersBySingerType.Clear();
            foreach (var group in externalRendererFactories.Values.GroupBy(factory => factory.singerType)) {
                externalRenderersBySingerType[group.Key] = group
                    .Select(factory => factory.name)
                    .OrderBy(name => name)
                    .ToArray();
            }
        }

        static string GetAssemblyKey(Assembly assembly) {
            try {
                string location = string.Empty;
                try {
                    if (!assembly.IsDynamic) {
                        location = assembly.Location ?? string.Empty;
                    }
                } catch {
                }
                return $"{assembly.FullName}|{location}|{assembly.ManifestModule.ModuleVersionId}";
            } catch {
                return assembly.FullName;
            }
        }

        static IEnumerable<Type> GetLoadableTypes(Assembly assembly) {
            try {
                return assembly.GetExportedTypes();
            } catch (ReflectionTypeLoadException e) {
                return e.Types.Where(type => type != null).Cast<Type>();
            } catch {
                return Enumerable.Empty<Type>();
            }
        }

        readonly static ConcurrentDictionary<string, object> cacheLockMap
            = new ConcurrentDictionary<string, object>();

        public static object GetCacheLock(string key) {
            return cacheLockMap.GetOrAdd(key, _ => new object());
        }

        public static void ApplyDynamics(RenderPhrase phrase, RenderResult result) {
            const int interval = 5;
            if (phrase.dynamics == null) {
                return;
            }
            int startTick = phrase.position - phrase.leading;
            double startMs = result.positionMs - result.leadingMs;
            int startSample = 0;
            for (int i = 0; i < phrase.dynamics.Length; ++i) {
                int endTick = startTick + interval;
                double endMs = phrase.timeAxis.TickPosToMsPos(endTick);
                int endSample = Math.Min((int)((endMs - startMs) / 1000 * 44100), result.samples.Length);
                float a = phrase.dynamics[i];
                float b = (i + 1) == phrase.dynamics.Length ? phrase.dynamics[i] : phrase.dynamics[i + 1];
                for (int j = startSample; j < endSample; ++j) {
                    result.samples[j] *= a + (b - a) * (j - startSample) / (endSample - startSample);
                }
                startTick = endTick;
                startSample = endSample;
            }
        }

        public static IReadOnlyList<IResampler> GetSupportedResamplers(IWavtool? wavtool) {
            if (wavtool is SharpWavtool) {
                return ToolsManager.Inst.Resamplers;
            } else {
                return ToolsManager.Inst.Resamplers
                    .Where(r => !(r is WorldlineResampler))
                    .ToArray();
            }
        }

        public static IReadOnlyList<IWavtool> GetSupportedWavtools(IResampler? resampler) {
            return ToolsManager.Inst.Wavtools;
        }
    }
}