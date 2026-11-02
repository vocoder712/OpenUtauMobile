using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Core;
using OpenUtauMobile.Plugin.Abstractions;

namespace OpenUtauMobile.Services.SingerPlugins
{
    /// <summary>校验错误携带字段定位，不允许失败的草稿覆盖原设置。</summary>
    public sealed class PluginSettingsValidationException(ImmutableArray<PluginDiagnostic> diagnostics)
        : Exception(string.Join("\n", diagnostics.Select(item => item.Message)))
    {
        public ImmutableArray<PluginDiagnostic> Diagnostics { get; } = diagnostics;
    }

    /// <summary>按插件隔离版本化配置；保留未知 JSON 字段，原子提交并检测并发编辑。</summary>
    public static class PluginSettingsService
    {
        private static readonly ConcurrentDictionary<string, Snapshot> effective = new(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> gates = new(StringComparer.Ordinal);
        private static readonly JsonSerializerOptions json = new() { WriteIndented = true };

        /// <summary>插件标识通过散列映射目录，不能成为宿主路径的一部分。</summary>
        public static string DirectoryFor(string pluginId) => Path.Combine(PathManager.Inst.DataPath, "PluginData",
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pluginId))).ToLowerInvariant());

        /// <summary>首次使用固定当前有效值；保存 Restart 字段不改变此快照。</summary>
        public static IPluginSettingsReader Effective(string pluginId)
        {
            string directory = DirectoryFor(pluginId);
            lock (effective)
            {
                if (effective.TryGetValue(directory, out Snapshot? cached)) return cached;
                RegisteredPluginSettings? registration = SingerPluginService.Registry.Snapshot.Settings.GetValueOrDefault(pluginId);
                Snapshot snapshot = registration == null ? new(0, Path.Combine(directory, "Resources"), ImmutableDictionary<string, PluginValue>.Empty)
                    : Read(registration).Snapshot;
                registration?.Instance.Apply(snapshot);
                effective[directory] = snapshot;
                return snapshot;
            }
        }

        /// <summary>打开独立草稿，损坏或未来版本配置只报错，不回退成可覆盖的空配置。</summary>
        public static PluginSettingsSession Open(RegisteredPluginSettings registration)
        {
            _ = Effective(registration.PluginId);
            Document document = Read(registration);
            return new(registration, document.Snapshot.Values, document.Fingerprint);
        }

        /// <summary>读取时只解释已知的类型化值，未来字段的原始节点保留在文档内。</summary>
        private static Document Read(RegisteredPluginSettings registration)
        {
            string directory = DirectoryFor(registration.PluginId);
            string path = Path.Combine(directory, "settings.json");
            bool exists = File.Exists(path);
            if (exists && new FileInfo(path).Length is 0 or > 1024 * 1024) throw new InvalidDataException("插件设置文件为空或过大。");
            byte[] bytes = exists ? File.ReadAllBytes(path) : [];
            JsonObject root = bytes.Length == 0 ? new() : JsonNode.Parse(bytes)?.AsObject()
                ?? throw new InvalidDataException("插件设置为空。");
            int version = root["schemaVersion"]?.GetValue<int>() ?? (bytes.Length == 0 ? registration.Descriptor.SchemaVersion : 0);
            if (version < 1 || version > registration.Descriptor.SchemaVersion)
                throw new InvalidDataException($"不支持插件设置版本 {version}，文件未修改：{path}");
            JsonObject raw = root["values"] is JsonObject values ? values : bytes.Length == 0 ? new()
                : throw new InvalidDataException("插件设置缺少 values 对象。");
            ImmutableDictionary<string, PluginValue>.Builder parsed = ImmutableDictionary.CreateBuilder<string, PluginValue>(StringComparer.Ordinal);
            foreach (PluginSettingDescriptor field in registration.Descriptor.Fields)
            {
                if (raw.TryGetPropertyValue(field.Field.Id, out JsonNode? node))
                    parsed[field.Field.Id] = node?.Deserialize<PluginValue>() ?? throw new InvalidDataException($"设置 {field.Field.Id} 为空。");
                else if (field.Field.DefaultValue is { } fallback) parsed[field.Field.Id] = fallback;
            }
            // 旧版本转换可读取可识别的未知值；无法识别的扩展数据始终按原样保留。
            foreach ((string key, JsonNode? node) in raw)
            {
                if (parsed.ContainsKey(key)) continue;
                try { if (node?.Deserialize<PluginValue>() is { } value) parsed[key] = value; }
                catch (JsonException) { }
            }
            ImmutableDictionary<string, PluginValue> result = parsed.ToImmutable();
            if (version < registration.Descriptor.SchemaVersion) result = registration.Instance.Upgrade(version, result);
            ValidateFields(registration.Descriptor.Fields.Select(field => field.Field).Where(field => result.ContainsKey(field.Id)), result);
            return new(root, raw, Convert.ToHexString(SHA256.HashData(bytes)),
                new(registration.Descriptor.SchemaVersion, Path.Combine(directory, "Resources"), result));
        }

        /// <summary>同一插件的保存互斥，过期会话必须重新打开。</summary>
        internal static async Task SaveAsync(PluginSettingsSession session, ImmutableDictionary<string, PluginValue> values,
            CancellationToken cancellationToken)
        {
            SemaphoreSlim gate = gates.GetOrAdd(DirectoryFor(session.Registration.PluginId), _ => new(1, 1));
            await gate.WaitAsync(cancellationToken);
            try { await CommitAsync(session, values, cancellationToken); }
            finally { gate.Release(); }
        }

        private static async Task CommitAsync(PluginSettingsSession session, ImmutableDictionary<string, PluginValue> values,
            CancellationToken cancellationToken)
        {
            RegisteredPluginSettings registration = session.Registration;
            Document current = Read(registration);
            if (current.Fingerprint != session.Fingerprint) throw new IOException("插件设置已改变，请重新打开设置页面。");
            ValidateFields(registration.Descriptor.Fields.Select(field => field.Field), values);
            PluginSettingsValidation validation = await registration.Instance.ValidateAsync(values, cancellationToken);
            if (!validation.IsValid) throw new PluginSettingsValidationException(validation.Diagnostics);
            JsonObject raw = (JsonObject)current.Raw.DeepClone();
            foreach ((string key, PluginValue value) in values)
                if (!raw.ContainsKey(key) || registration.Descriptor.Fields.Any(field => field.Field.Id == key))
                    raw[key] = JsonSerializer.SerializeToNode(value, json);
            current.Root["values"] = raw;
            current.Root["schemaVersion"] = registration.Descriptor.SchemaVersion;
            string directory = DirectoryFor(registration.PluginId);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "settings.json");
            string temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(current.Root, json);
            try
            {
                await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporary, path, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            session.Accept(values, Convert.ToHexString(SHA256.HashData(bytes)));
            lock (effective)
            {
                Snapshot previous = effective[directory];
                ImmutableDictionary<string, PluginValue> applied = previous.Values;
                foreach (PluginSettingDescriptor field in registration.Descriptor.Fields)
                    if (field.Application != PluginSettingApplication.Restart && values.TryGetValue(field.Field.Id, out PluginValue? value))
                        applied = applied.SetItem(field.Field.Id, value);
                Snapshot updated = previous with { Values = applied };
                registration.Instance.Apply(updated);
                effective[directory] = updated;
            }
        }

        /// <summary>在插件回调前统一校验类型、必填、数值范围与选项，不默默修复损坏的保存值。</summary>
        private static void ValidateFields(IEnumerable<PluginFieldDescriptor> fields, ImmutableDictionary<string, PluginValue> values)
        {
            List<PluginDiagnostic> diagnostics = [];
            foreach (PluginFieldDescriptor field in fields)
            {
                values.TryGetValue(field.Id, out PluginValue? value);
                bool valid = value == null ? !field.IsRequired : field.Kind switch
                {
                    PluginFieldKind.Text => value is PluginValue.Text text && (!field.IsRequired || !string.IsNullOrWhiteSpace(text.Value)),
                    PluginFieldKind.Boolean => value is PluginValue.Boolean boolean && (!field.IsRequired || boolean.Value),
                    PluginFieldKind.Number => value is PluginValue.Number number && !(number.Value < field.Minimum || number.Value > field.Maximum),
                    PluginFieldKind.Selection => value is PluginValue.Selection selection &&
                        ((!field.IsRequired && string.IsNullOrEmpty(selection.Value)) || (!field.Options.IsDefault && field.Options.Any(option => option.Id == selection.Value))),
                    PluginFieldKind.MultipleSelection => value is PluginValue.MultipleSelection multiple &&
                        (!field.IsRequired || !multiple.Values.IsDefaultOrEmpty) && (multiple.Values.IsDefaultOrEmpty ||
                        (!field.Options.IsDefault && multiple.Values.All(id => field.Options.Any(option => option.Id == id)))),
                    PluginFieldKind.File or PluginFieldKind.Directory => value is PluginValue.Path path &&
                        path.IsDirectory == (field.Kind == PluginFieldKind.Directory) && (!field.IsRequired || !string.IsNullOrWhiteSpace(path.Value)),
                    _ => false,
                };
                if (!valid) diagnostics.Add(new("settings.invalid-field", $"{field.Label}：无效的设置值。", FieldId: field.Id));
            }
            if (diagnostics.Count > 0) throw new PluginSettingsValidationException([.. diagnostics]);
        }

        private sealed record Snapshot(int SchemaVersion, string ResourceDirectory,
            ImmutableDictionary<string, PluginValue> Values) : IPluginSettingsReader
        {
            public bool TryGet(string key, out PluginValue? value) => Values.TryGetValue(key, out value);
        }
        private sealed record Document(JsonObject Root, JsonObject Raw, string Fingerprint, Snapshot Snapshot);
    }

    /// <summary>页面持有的独立编辑会话；保留未展示的字段与读取版本。</summary>
    public sealed class PluginSettingsSession
    {
        internal RegisteredPluginSettings Registration { get; }
        internal string Fingerprint { get; private set; }
        public ImmutableDictionary<string, PluginValue> Values { get; private set; }
        internal PluginSettingsSession(RegisteredPluginSettings registration, ImmutableDictionary<string, PluginValue> values, string fingerprint)
        { Registration = registration; Values = values; Fingerprint = fingerprint; }
        internal void Accept(ImmutableDictionary<string, PluginValue> values, string fingerprint)
        { Values = values; Fingerprint = fingerprint; }
        /// <summary>后台执行完整校验及原子保存。</summary>
        public Task SaveAsync(ImmutableDictionary<string, PluginValue> values, CancellationToken cancellationToken = default) =>
            Task.Run(() => PluginSettingsService.SaveAsync(this, values, cancellationToken), cancellationToken);
    }
}
