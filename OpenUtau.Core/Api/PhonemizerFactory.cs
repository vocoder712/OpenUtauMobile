using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using OpenUtau.Core;
using Serilog;

namespace OpenUtau.Api {
    public class PhonemizerFactory {
        public Type type;
        public string name;
        public string tag;
        public string author;
        public string language;

        public Phonemizer Create() {
            var phonemizer = Activator.CreateInstance(type) as Phonemizer;
            phonemizer.Name = name;
            phonemizer.Tag = tag;
            phonemizer.Language = language;
            return phonemizer;
        }

        public override string ToString() => string.IsNullOrEmpty(author)
            ? $"[{tag}] {name}"
            : $"[{tag}] {name} (Contributed by {author})";

        private static readonly ConcurrentDictionary<Type, PhonemizerFactory> factories = new();
        private static PhonemizerFactory[] orderedFactories = [];
        public static PhonemizerFactory Get(Type type) {
            if (factories.TryGetValue(type, out var factory)) {
                return factory;
            }
            var attr = type.GetCustomAttribute<PhonemizerAttribute>();
            if (attr == null || string.IsNullOrEmpty(attr.Name) || string.IsNullOrEmpty(attr.Tag)) {
                return null;
            }
            factory = new PhonemizerFactory() {
                type = type,
                name = attr.Name,
                tag = attr.Tag,
                author = attr.Author,
                language = attr.Language,
            };
            return factories.GetOrAdd(type, factory);
        }

        public static PhonemizerFactory? Get(string typeFullName) {
            foreach (var factory in factories.Values) {
                if (factory.type.FullName == typeFullName) {
                    return factory;
                }
            }
            return null;
        }

        public static void BuildList() {
            orderedFactories = factories.Values.OrderBy(f => f.tag).ToArray();
        }

        public static PhonemizerFactory[] GetAll() => orderedFactories;

        private static bool resolverRegistered = false;
        private static readonly object loadLock = new();

        /// <summary>
        /// 扫描程序集里所有带 [Phonemizer] 特性的类，注册到 factories。
        /// 返回注册的音素器数量。
        /// </summary>
        public static int RegisterFromAssembly(Assembly assembly) {
            Type baseType = typeof(Phonemizer);
            int registered = 0;
            foreach (Type type in assembly.GetTypes()) {
                try {
                    if (type.IsAbstract || type.IsInterface || !baseType.IsAssignableFrom(type)) {
                        continue;
                    }
                    if (Get(type) != null) {
                        registered++;
                    }
                } catch (Exception e) {
                    Log.Error(e, "Failed to inspect type {Type}", type.FullName);
                }
            }
            if (registered > 0) {
                BuildList();
                Log.Information("Registered {Count} phonemizer(s) from {Assembly}",
                    registered, assembly.GetName().Name);
            }
            return registered;
        }

        public static void LoadPluginDll(string dllPath) {
            if (!File.Exists(dllPath)) {
                throw new FileNotFoundException("Plugin DLL not found.", dllPath);
            }
            string pluginDir = Path.GetDirectoryName(dllPath);

            EnsureResolver(pluginDir);

            Assembly assembly = Assembly.LoadFrom(dllPath);
            RegisterFromAssembly(assembly);
        }

        public static void LoadAllFromPluginsDirectory() {
            string pluginsPath;
            try {
                pluginsPath = PathManager.Inst.PluginsPath;
            } catch (Exception e) {
                Log.Warning(e, "Cannot resolve PluginsPath when loading plugins.");
                return;
            }
            if (!Directory.Exists(pluginsPath)) {
                return;
            }
            foreach (string dllPath in Directory.GetFiles(pluginsPath, "*.dll")) {
                try {
                    LoadPluginDll(dllPath);
                } catch (Exception e) {
                    Log.Error(e, "Failed to load plugin {Path}", dllPath);
                }
            }
        }

        private static void EnsureResolver(string pluginDir) {
            lock (loadLock) {
                if (resolverRegistered) {
                    return;
                }
                resolverRegistered = true;
                AppDomain.CurrentDomain.AssemblyResolve += (sender, args) => {
                    try {
                        string asmFileName = new AssemblyName(args.Name).Name + ".dll";

                        try {
                            string pluginsPath = PathManager.Inst.PluginsPath;
                            string candidate = Path.Combine(pluginsPath, asmFileName);
                            if (File.Exists(candidate)) {
                                return Assembly.LoadFrom(candidate);
                            }
                        } catch {
                            // PathManager 可能尚未初始化；忽略并继续
                        }

                        if (!string.IsNullOrEmpty(pluginDir)) {
                            string localCandidate = Path.Combine(pluginDir, asmFileName);
                            if (File.Exists(localCandidate)) {
                                return Assembly.LoadFrom(localCandidate);
                            }
                        }
                    } catch (Exception e) {
                        Log.Warning(e, "AssemblyResolve failed for {Name}", args.Name);
                    }
                    return null;
                };
            }
        }
    }
}