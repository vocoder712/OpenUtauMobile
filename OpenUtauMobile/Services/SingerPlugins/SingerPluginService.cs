using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.IO;
using OpenUtau.Core;
using OpenUtauMobile.Plugin.Abstractions;
using Serilog;

namespace OpenUtauMobile.Services.SingerPlugins
{
    /// <summary>加载版本化歌手入口，隔离失败插件，不执行发现、安装或设置操作。</summary>
    public static class SingerPluginService
    {
        private static readonly object gate = new();
        private static readonly HashSet<Assembly> attempted = [];
        private static ImmutableArray<PluginDiagnostic> diagnostics = [];

        /// <summary>供 Mobile 各子系统读取的统一能力目录。</summary>
        public static SingerPluginRegistry Registry { get; } = new();

        /// <summary>登记失败的诊断快照，保留程序集位置与原始错误消息。</summary>
        public static ImmutableArray<PluginDiagnostic> Diagnostics
        {
            get
            {
                lock (gate)
                {
                    return diagnostics;
                }
            }
        }

        /// <summary>在 Core 扫描外部 DLL 前固定契约依赖的程序集身份。</summary>
        public static void EnsureContractsLoaded()
        {
            _ = typeof(ISingerPlugin).Assembly.GetName();
        }

        /// <summary>在歌手首次发现及工程恢复之前调用；重复调用不会重新实例化入口。</summary>
        public static void Initialize()
        {
            EnsureContractsLoaded();
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic)
                {
                    continue;
                }
                lock (gate)
                {
                    if (!attempted.Add(assembly))
                    {
                        continue;
                    }
                }
                try
                {
                    SingerPluginAttribute? attribute = assembly.GetCustomAttribute<SingerPluginAttribute>();
                    if (attribute == null)
                    {
                        if (assembly.GetCustomAttributesData().Any(data => data.AttributeType.FullName == typeof(SingerPluginAttribute).FullName))
                        {
                            throw new InvalidOperationException("插件使用了另一份契约程序集，请引用宿主提供的 OpenUtauMobile.Plugin.Abstractions。");
                        }
                        continue;
                    }
                    // 必须在创建插件实例和调用任何插件方法之前验证契约版本。
                    if (attribute.ContractVersion != SingerPluginContract.Version)
                    {
                        throw new NotSupportedException($"歌手插件契约版本 {attribute.ContractVersion} 不受支持，宿主版本为 {SingerPluginContract.Version}。");
                    }
                    Type entry = attribute.EntryPoint;
                    if (entry.Assembly != assembly || !entry.IsVisible || entry.IsAbstract || entry.ContainsGenericParameters
                        || !typeof(ISingerPlugin).IsAssignableFrom(entry) || entry.GetConstructor(Type.EmptyTypes) == null)
                    {
                        throw new InvalidOperationException("歌手插件入口必须是当前程序集内实现 ISingerPlugin 的公开非抽象类型，并有公开无参构造函数。");
                    }
                    ISingerPlugin plugin = (ISingerPlugin)Activator.CreateInstance(entry)!;
                    Registry.Register(plugin, assembly);
                }
                catch (Exception exception)
                {
                    Exception error = exception is TargetInvocationException { InnerException: { } inner } ? inner : exception;
                    string location = assembly.Location;
                    PluginDiagnostic diagnostic = new("plugin.registration.failed", error.Message,
                        Location: string.IsNullOrEmpty(location) ? assembly.FullName : location);
                    lock (gate)
                    {
                        diagnostics = diagnostics.Add(diagnostic);
                    }
                    Log.Error(error, "Failed to initialize singer plugin {Assembly}", diagnostic.Location);
                }
            }
            InspectUnloadedEntries();
        }

        /// <summary>只读检查未载入的声明入口，补充启动错误通知中的依赖及位数诊断。</summary>
        private static void InspectUnloadedEntries()
        {
            string root = PathManager.Inst.PluginsPath;
            if (!Directory.Exists(root)) return;
            HashSet<string> loaded = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            HashSet<Guid> loadedModules = [];
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic) continue;
                if (!string.IsNullOrEmpty(assembly.Location)) loaded.Add(Path.GetFullPath(assembly.Location));
                // Android 运行时可能返回映射后的 Location，模块标识仍能对应磁盘上的同一份程序集。
                loadedModules.Add(assembly.ManifestModule.ModuleVersionId);
            }
            try
            {
                EnumerationOptions options = new() { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = false };
                foreach (string path in Directory.EnumerateFiles(root, "*", options).Where(path => Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase)))
                {
                    if (loaded.Contains(Path.GetFullPath(path))) continue;
                    try
                    {
                        using FileStream stream = File.OpenRead(path);
                        using PEReader pe = new(stream);
                        if (!pe.HasMetadata) continue;
                        MetadataReader reader = pe.GetMetadataReader();
                        if (!reader.IsAssembly) continue;
                        if (loadedModules.Contains(reader.GetGuid(reader.GetModuleDefinition().Mvid))) continue;
                        bool entry = reader.GetAssemblyDefinition().GetCustomAttributes().Any(handle =>
                        {
                            CustomAttribute attribute = reader.GetCustomAttribute(handle);
                            if (attribute.Constructor.Kind != HandleKind.MemberReference) return false;
                            MemberReference constructor = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
                            if (constructor.Parent.Kind != HandleKind.TypeReference) return false;
                            TypeReference type = reader.GetTypeReference((TypeReferenceHandle)constructor.Parent);
                            return reader.GetString(type.Namespace) == typeof(SingerPluginAttribute).Namespace
                                && reader.GetString(type.Name) == nameof(SingerPluginAttribute);
                        });
                        if (!entry) continue;
                        AddLoadDiagnostic(path, $"插件程序集未能载入，请检查依赖及进程架构（{RuntimeInformation.ProcessArchitecture}）。");
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or BadImageFormatException)
                    { AddLoadDiagnostic(path, error.Message); }
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { AddLoadDiagnostic(root, error.Message); }
        }

        private static void AddLoadDiagnostic(string path, string message)
        {
            lock (gate)
            {
                if (diagnostics.Any(item => item.Location == path)) return;
                diagnostics = diagnostics.Add(new("plugin.load.failed", message, Location: path));
            }
            Log.Error("Plugin assembly unavailable: {Location}: {Message}", path, message);
        }
    }
}
