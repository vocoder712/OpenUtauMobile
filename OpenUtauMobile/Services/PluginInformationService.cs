using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using OpenUtau.Core;
using OpenUtauMobile.Services.Projects;
using OpenUtauMobile.Services.SingerPlugins;
using Serilog;

namespace OpenUtauMobile.Services;

/// <summary>详情页可展示的一项信息，标签使用本地化资源键。</summary>
public sealed record PluginInformationItem(string LabelKey, string Value);

/// <summary>按模块标识保存的插件身份和能力，不序列化插件实例。</summary>
public sealed record PluginRegistrationInfo(string Id, string Name, PluginInformationItem[] Capabilities);

/// <summary>静态文件信息与最近一次成功登记的能力快照。</summary>
public sealed record PluginInformation(string Name, Guid ModuleId, PluginInformationItem[] Metadata,
    PluginRegistrationInfo? Registration, PluginInformationItem[] StaticCapabilities);

/// <summary>只读提取程序集元数据，并缓存已登记的插件信息；不会加载外部程序集。</summary>
public static class PluginInformationService
{
    /// <summary>从磁盘提取版本、签名标志、引用和文件摘要，优先使用本进程的登记信息。</summary>
    public static PluginInformation Read(string relativePath)
    {
        string path = PluginManagementService.ResolvePluginPath(relativePath);
        using FileStream stream = File.OpenRead(path);
        using PEReader pe = new(stream, PEStreamOptions.LeaveOpen);
        MetadataReader reader = pe.GetMetadataReader();
        AssemblyDefinition assembly = reader.GetAssemblyDefinition();
        Guid moduleId = reader.GetGuid(reader.GetModuleDefinition().Mvid);
        string assemblyName = reader.GetString(assembly.Name);
        List<PluginInformationItem> items =
        [
            new("PluginDetails.Assembly", assemblyName),
            new("PluginDetails.Version", assembly.Version.ToString()),
            new("PluginDetails.File", relativePath),
            new("PluginDetails.Size", $"{stream.Length / 1024d:N1} KB"),
            new("PluginDetails.Architecture", pe.PEHeaders.CoffHeader.Machine.ToString()),
            new("PluginDetails.Module", moduleId.ToString()),
        ];
        string name = assemblyName;
        foreach (CustomAttributeHandle handle in assembly.GetCustomAttributes())
        {
            CustomAttribute attribute = reader.GetCustomAttribute(handle);
            if (attribute.Constructor.Kind != HandleKind.MemberReference) continue;
            MemberReference constructor = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
            if (constructor.Parent.Kind != HandleKind.TypeReference) continue;
            TypeReference type = reader.GetTypeReference((TypeReferenceHandle)constructor.Parent);
            if (reader.GetString(type.Namespace) == "OpenUtauMobile.Plugin.Abstractions"
                && reader.GetString(type.Name) == "SingerPluginAttribute")
            {
                BlobReader entry = reader.GetBlobReader(attribute.Value);
                if (entry.ReadUInt16() == 1)
                {
                    items.Add(new("PluginDetails.EntryPoint", entry.ReadSerializedString() ?? ""));
                    items.Add(new("PluginDetails.ContractVersion", entry.ReadInt32().ToString()));
                }
                continue;
            }
            string? label = (reader.GetString(type.Namespace), reader.GetString(type.Name)) switch
            {
                ("System.Reflection", "AssemblyTitleAttribute") => "PluginDetails.Name",
                ("System.Reflection", "AssemblyDescriptionAttribute") => "PluginDetails.Description",
                ("System.Reflection", "AssemblyCompanyAttribute") => "PluginDetails.Author",
                ("System.Reflection", "AssemblyProductAttribute") => "PluginDetails.Product",
                ("System.Reflection", "AssemblyCopyrightAttribute") => "PluginDetails.Copyright",
                ("System.Reflection", "AssemblyInformationalVersionAttribute") => "PluginDetails.ProductVersion",
                ("System.Reflection", "AssemblyFileVersionAttribute") => "PluginDetails.FileVersion",
                ("System.Runtime.Versioning", "TargetFrameworkAttribute") => "PluginDetails.Framework",
                _ => null,
            };
            if (label == null) continue;
            BlobReader blob = reader.GetBlobReader(attribute.Value);
            if (blob.ReadUInt16() != 1 || blob.ReadSerializedString() is not { Length: > 0 } value) continue;
            items.Add(new(label, value));
            if (label == "PluginDetails.Name") name = value;
        }
        byte[] key = reader.GetBlobBytes(assembly.PublicKey);
        if (key.Length > 0)
        {
            byte[] hash = SHA1.HashData(key);
            byte[] token = [.. hash.TakeLast(8).Reverse()];
            items.Add(new("PluginDetails.PublicKeyToken", Convert.ToHexString(token).ToLowerInvariant()));
            items.Add(new("PluginDetails.PublicKey", Convert.ToHexString(key)));
        }
        items.Add(new("PluginDetails.StrongName", Helpers.L.S(
            (pe.PEHeaders.CorHeader!.Flags & CorFlags.StrongNameSigned) != 0
                ? "PluginDetails.SignaturePresent" : "PluginDetails.Unsigned")));
        items.Add(new("PluginDetails.Authenticode", Helpers.L.S(
            pe.PEHeaders.PEHeader?.CertificateTableDirectory.Size > 0
                ? "PluginDetails.SignaturePresent" : "PluginDetails.Unsigned")));
        items.Add(new("PluginDetails.References", string.Join(Environment.NewLine,
            reader.AssemblyReferences.Select(handle => reader.GetAssemblyReference(handle))
                .Select(reference => $"{reader.GetString(reference.Name)}, {reference.Version}"))));
        stream.Position = 0;
        items.Add(new("PluginDetails.Hash", Convert.ToHexString(SHA256.HashData(stream))));

        PluginRegistrationInfo? registration = CurrentRegistration(relativePath) ?? ReadCached(moduleId);
        List<PluginInformationItem> staticCapabilities = [];
        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
        {
            TypeDefinition type = reader.GetTypeDefinition(handle);
            if ((type.Attributes & TypeAttributes.VisibilityMask) is not TypeAttributes.Public and not TypeAttributes.NestedPublic
                || (type.Attributes & TypeAttributes.Abstract) != 0) continue;
            string fullName = reader.GetString(type.Namespace) + "." + reader.GetString(type.Name);
            if (DerivesFrom(reader, type.BaseType, "OpenUtau.Api", "Phonemizer"))
                staticCapabilities.Add(new("PluginDetails.Phonemizer", fullName));
            if (DerivesFrom(reader, type.BaseType, "OpenUtau.Core.Editing", "BatchEdit"))
                staticCapabilities.Add(new("PluginDetails.BatchEdit", fullName));
        }
        return new(registration?.Name ?? name, moduleId, [.. items], registration, [.. staticCapabilities]);
    }

    /// <summary>在启动登记结束后保存快照，使禁用后的详情页仍可展示稳定 ID 和能力。</summary>
    public static void CacheRegistrations()
    {
        IReadOnlyList<PluginFileEntry> files;
        try { files = PluginManagementService.ListFiles(); }
        catch (Exception error) { Log.Warning(error, "Failed to list plugins for information cache"); return; }
        foreach (PluginFileEntry file in files)
        {
            try
            {
                if (CurrentRegistration(file.RelativePath) is not { } registration) continue;
                using FileStream stream = File.OpenRead(PluginManagementService.ResolvePluginPath(file.RelativePath));
                using PEReader pe = new(stream);
                MetadataReader reader = pe.GetMetadataReader();
                Guid module = reader.GetGuid(reader.GetModuleDefinition().Mvid);
                AtomicFileWriter.Write(CachePath(module), writer => writer.Write(Json.Serialize(registration)));
            }
            catch (Exception error) { Log.Warning(error, "Failed to cache plugin information {Plugin}", file.RelativePath); }
        }
    }

    private static PluginRegistrationInfo? CurrentRegistration(string relativePath)
    {
        RegisteredSingerPlugin? plugin = PluginWebSettingsService.FindPlugin(relativePath);
        if (plugin == null) return null;
        SingerPluginSnapshot snapshot = SingerPluginService.Registry.Snapshot;
        List<PluginInformationItem> capabilities = [];
        foreach (RegisteredSingerType type in snapshot.Types.Values.Where(type => type.PluginId == plugin.PluginId).OrderBy(type => type.Descriptor.Id))
        {
            capabilities.Add(new("PluginDetails.SingerType", $"{type.Descriptor.DisplayName} ({type.Descriptor.Id})"));
            foreach (string renderer in type.Descriptor.RendererIds)
                capabilities.Add(new("PluginDetails.Renderer", renderer));
        }
        foreach (RegisteredSingerProvider provider in snapshot.Providers.Values.Where(provider => provider.PluginId == plugin.PluginId).OrderBy(provider => provider.Id))
            capabilities.Add(new("PluginDetails.Provider", provider.Id));
        foreach (RegisteredSingerInstaller installer in snapshot.Installers.Values.Where(installer => installer.PluginId == plugin.PluginId).OrderBy(installer => installer.Descriptor.Id))
            capabilities.Add(new("PluginDetails.Installer", $"{installer.Descriptor.DisplayName} ({installer.Descriptor.Id}) {string.Join(", ", installer.Descriptor.Extensions)}"));
        return new(plugin.PluginId, snapshot.Plugins[plugin.PluginId].DisplayName, [.. capabilities]);
    }

    private static PluginRegistrationInfo? ReadCached(Guid module)
    {
        try
        {
            return File.Exists(CachePath(module))
                ? Json.Deserialize<PluginRegistrationInfo>(File.ReadAllText(CachePath(module))) : null;
        }
        catch (Exception error) { Log.Debug(error, "Failed to read plugin information cache {Module}", module); return null; }
    }

    private static string CachePath(Guid module) => Path.Combine(PathManager.Inst.DataPath, "PluginInfoCache", $"{module:N}.json");

    /// <summary>只沿元数据继承链识别可导出的能力，不解析或加载外部依赖。</summary>
    private static bool DerivesFrom(MetadataReader reader, EntityHandle handle, string expectedNamespace, string expectedName)
    {
        HashSet<EntityHandle> visited = [];
        while (!handle.IsNil && visited.Add(handle))
        {
            if (handle.Kind == HandleKind.TypeReference)
            {
                TypeReference type = reader.GetTypeReference((TypeReferenceHandle)handle);
                return reader.GetString(type.Namespace) == expectedNamespace && reader.GetString(type.Name) == expectedName;
            }
            if (handle.Kind != HandleKind.TypeDefinition) return false;
            TypeDefinition definition = reader.GetTypeDefinition((TypeDefinitionHandle)handle);
            if (reader.GetString(definition.Namespace) == expectedNamespace && reader.GetString(definition.Name) == expectedName) return true;
            handle = definition.BaseType;
        }
        return false;
    }
}
