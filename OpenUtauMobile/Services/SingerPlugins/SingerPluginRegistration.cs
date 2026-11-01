using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Plugin.Abstractions;

namespace OpenUtauMobile.Services.SingerPlugins
{
    /// <summary>短生命周期登记收集器；关闭后拒绝插件保留引用继续追加。</summary>
    internal sealed class SingerPluginRegistration : ISingerPluginRegistrar
    {
        private readonly SingerPluginDescriptor plugin;
        private readonly Assembly assembly;
        private readonly Dictionary<string, RegisteredSingerType> types = new(StringComparer.Ordinal);
        private readonly Dictionary<string, RegisteredSingerProvider> providers = new(StringComparer.Ordinal);
        private readonly Dictionary<string, RegisteredSingerInstaller> installers = new(StringComparer.Ordinal);
        private readonly int registrationThread = Environment.CurrentManagedThreadId;
        private bool closed;

        internal IReadOnlyCollection<RegisteredSingerType> Types => types.Values;
        internal IReadOnlyCollection<RegisteredSingerProvider> Providers => providers.Values;
        internal IReadOnlyCollection<RegisteredSingerInstaller> Installers => installers.Values;
        internal RegisteredPluginSettings? Settings { get; private set; }

        internal SingerPluginRegistration(SingerPluginDescriptor plugin, Assembly assembly)
        {
            RequireId(plugin.Id);
            RequireText(plugin.DisplayName, "插件名称");
            if (plugin.Id.Contains(':') || plugin.Id.StartsWith("opum.", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("插件 ID 不能包含冒号或使用宿主保留前缀 opum.。");
            }
            this.plugin = plugin;
            this.assembly = assembly;
        }

        /// <summary>固定类型描述；值类型集合不会随插件后续代码改变。</summary>
        public void RegisterSingerType(SingerTypeDescriptor descriptor)
        {
            RequireOpen();
            ArgumentNullException.ThrowIfNull(descriptor);
            RequireOwnedId(descriptor.Id);
            RequireText(descriptor.DisplayName, "类型名称");
            if ((int)descriptor.BridgeValue < SingerPluginContract.ExternalTypeMinimum)
            {
                throw new InvalidOperationException($"类型 {descriptor.Id} 的桥接值不在外部区间。");
            }
            descriptor = descriptor with
            {
                RendererIds = OrEmpty(descriptor.RendererIds),
                LocalizedNames = descriptor.LocalizedNames?.WithComparers(StringComparer.OrdinalIgnoreCase),
            };
            Add(types, descriptor.Id, new(plugin.Id, descriptor));
        }

        /// <summary>捕获提供方身份，不在登记阶段扫描声库。</summary>
        public void RegisterProvider(ISingerProvider provider)
        {
            RequireOpen();
            ArgumentNullException.ThrowIfNull(provider);
            string id = provider.Id;
            RequireOwnedId(id);
            Add(providers, id, new(plugin.Id, id, OrEmpty(provider.SingerTypeIds), provider));
        }

        /// <summary>捕获文件选择器描述，允许不同安装器使用相同后缀。</summary>
        public void RegisterInstaller(ISingerPackageInstaller installer)
        {
            RequireOpen();
            ArgumentNullException.ThrowIfNull(installer);
            SingerInstallerDescriptor descriptor = installer.Descriptor
                ?? throw new InvalidOperationException("安装器缺少描述。");
            RequireOwnedId(descriptor.Id);
            RequireText(descriptor.DisplayName, "安装器名称");
            ImmutableArray<string> extensions = OrEmpty(descriptor.Extensions);
            if (extensions.IsEmpty || extensions.Any(extension => !IsValidExtension(extension)))
            {
                throw new InvalidOperationException($"安装器 {descriptor.Id} 的文件后缀无效。");
            }
            descriptor = descriptor with
            {
                Extensions = extensions.Select(extension => extension.ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToImmutableArray(),
                SingerTypeIds = OrEmpty(descriptor.SingerTypeIds),
                MimeTypes = OrEmpty(descriptor.MimeTypes),
            };
            foreach (string mime in descriptor.MimeTypes)
            {
                RequireText(mime, "MIME 类型");
                if (!mime.Contains('/') || mime.Any(char.IsWhiteSpace))
                {
                    throw new InvalidOperationException($"无效的 MIME 类型：{mime}");
                }
            }
            Add(installers, descriptor.Id, new(plugin.Id, descriptor, installer));
        }

        /// <summary>每个插件登记一个设置分组；描述中没有任意控件或脚本。</summary>
        public void RegisterSettings(IPluginSettingsHandler settings)
        {
            RequireOpen();
            ArgumentNullException.ThrowIfNull(settings);
            if (Settings != null)
            {
                throw new InvalidOperationException("同一插件只能登记一个设置分组。");
            }
            PluginSettingsDescriptor descriptor = settings.Descriptor
                ?? throw new InvalidOperationException("设置缺少描述。");
            RequireText(descriptor.DisplayName, "设置名称");
            if (descriptor.SchemaVersion < 1)
            {
                throw new InvalidOperationException("设置数据版本必须为正整数。");
            }
            ImmutableArray<PluginSettingDescriptor> fields = OrEmpty(descriptor.Fields);
            ValidateFields(fields.Select(field => field.Field));
            if (fields.Any(field => !Enum.IsDefined(field.Application)))
            {
                throw new InvalidOperationException("设置包含未知生效方式。");
            }
            Settings = new(plugin.Id, descriptor with { Fields = fields }, settings);
        }

        internal void Close() => closed = true;

        /// <summary>校验整批引用与工厂；允许先登记提供方再登记类型。</summary>
        internal void Validate()
        {
            RequireDistinct(types.Values.Select(type => type.Descriptor.BridgeValue), "类型桥接值");
            foreach (RegisteredSingerType type in types.Values)
            {
                ValidateRenderers(type.Descriptor);
            }
            foreach (RegisteredSingerProvider provider in providers.Values)
            {
                ValidateTypeReferences(provider.SingerTypeIds, provider.Id);
            }
            foreach (RegisteredSingerInstaller installer in installers.Values)
            {
                ValidateTypeReferences(installer.Descriptor.SingerTypeIds, installer.Descriptor.Id);
            }
        }

        /// <summary>Core 默认选择不能被宿主描述覆盖，因此登记时要求二者一致。</summary>
        private void ValidateRenderers(SingerTypeDescriptor type)
        {
            RequireDistinct(type.RendererIds, "渲染器 ID");
            if (!type.IsAvailable)
            {
                RequireText(type.UnavailableReason, "不可用原因");
                if (!type.RendererIds.IsEmpty || type.DefaultRendererId != null)
                {
                    throw new InvalidOperationException($"不可用类型 {type.Id} 不能声明可选渲染器。");
                }
                return;
            }
            if (type.RendererIds.IsEmpty || type.DefaultRendererId == null || !type.RendererIds.Contains(type.DefaultRendererId))
            {
                throw new InvalidOperationException($"类型 {type.Id} 缺少有效默认渲染器。");
            }
            string[] coreIds = Renderers.GetSupportedRenderers(type.BridgeValue);
            if (!coreIds.ToHashSet(StringComparer.Ordinal).SetEquals(type.RendererIds)
                || Renderers.GetDefaultRenderer(type.BridgeValue) != type.DefaultRendererId)
            {
                throw new InvalidOperationException($"类型 {type.Id} 的渲染器声明与 Core 工厂或默认顺序不一致。");
            }
            foreach (string id in type.RendererIds)
            {
                RequireText(id, "渲染器 ID");
                IRenderer renderer = Renderers.GetOrCreate(id)
                    ?? throw new InvalidOperationException($"无法创建渲染器 {id}。");
                if (renderer.SingerType != type.BridgeValue || renderer.GetType().Assembly != assembly)
                {
                    throw new InvalidOperationException($"渲染器 {id} 的类型值或所属程序集与插件不一致。");
                }
            }
        }

        private void ValidateTypeReferences(ImmutableArray<string> ids, string owner)
        {
            if (ids.IsEmpty || ids.Any(id => id == null || !types.ContainsKey(id)))
            {
                throw new InvalidOperationException($"{owner} 必须引用本插件已登记的歌手类型。");
            }
            RequireDistinct(ids, "引用类型 ID");
        }

        private void RequireOwnedId(string id)
        {
            RequireId(id);
            if (!id.StartsWith(plugin.Id + ":", StringComparison.Ordinal) || id.Length == plugin.Id.Length + 1)
            {
                throw new InvalidOperationException($"能力 ID {id} 必须位于 {plugin.Id}: 命名空间。");
            }
        }

        private void RequireOpen()
        {
            if (closed || Environment.CurrentManagedThreadId != registrationThread)
            {
                throw new InvalidOperationException("只能在 Register 的同步调用范围内登记能力。");
            }
        }

        private static void RequireId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 160 || !char.IsAsciiLetterLower(id[0])
                || id.Any(character => !char.IsAsciiLetterLower(character) && !char.IsAsciiDigit(character)
                    && character is not '.' and not '-' and not '_' and not ':'))
            {
                throw new InvalidOperationException($"无效的稳定 ID：{id}");
            }
        }

        private static void RequireText(string? text, string name)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException($"{name} 不能为空。");
            }
        }

        private static void RequireDistinct<T>(IEnumerable<T> values, string name)
        {
            HashSet<T> seen = [];
            foreach (T value in values)
            {
                if (!seen.Add(value))
                {
                    throw new InvalidOperationException($"{name} 重复：{value}");
                }
            }
        }

        private static void Add<T>(Dictionary<string, T> items, string id, T value)
        {
            if (!items.TryAdd(id, value))
            {
                throw new InvalidOperationException($"同批能力 ID 重复：{id}");
            }
        }

        private static ImmutableArray<T> OrEmpty<T>(ImmutableArray<T> items) => items.IsDefault ? [] : items;

        private static bool IsValidExtension(string extension)
        {
            return !string.IsNullOrWhiteSpace(extension) && extension.StartsWith('.')
                && extension[1..].Split('.').All(part => part.Length > 0 && part.All(char.IsAsciiLetterOrDigit));
        }

        /// <summary>在注册边界拒绝无法由宿主表单表达的字段描述。</summary>
        internal static void ValidateFields(IEnumerable<PluginFieldDescriptor> fields)
        {
            HashSet<string> ids = new(StringComparer.Ordinal);
            foreach (PluginFieldDescriptor field in fields)
            {
                RequireId(field.Id);
                RequireText(field.Label, "字段名称");
                if (!ids.Add(field.Id) || !Enum.IsDefined(field.Kind) || field.Minimum > field.Maximum)
                {
                    throw new InvalidOperationException($"无效或重复的字段：{field.Id}");
                }
                ImmutableArray<PluginFieldOption> options = OrEmpty(field.Options);
                if (OrEmpty(field.FileExtensions).Any(extension => !IsValidExtension(extension)))
                {
                    throw new InvalidOperationException($"字段 {field.Id} 包含无效文件后缀。");
                }
                RequireDistinct(options.Select(option => option.Id), "选项 ID");
                foreach (PluginFieldOption option in options)
                {
                    RequireText(option.Id, "选项 ID");
                    RequireText(option.Label, "选项名称");
                }
                bool valueMatches = field.DefaultValue switch
                {
                    null => true,
                    PluginValue.Text => field.Kind == PluginFieldKind.Text,
                    PluginValue.Boolean => field.Kind == PluginFieldKind.Boolean,
                    PluginValue.Number number => field.Kind == PluginFieldKind.Number
                        && (!field.Minimum.HasValue || number.Value >= field.Minimum.Value)
                        && (!field.Maximum.HasValue || number.Value <= field.Maximum.Value),
                    PluginValue.Selection selection => field.Kind == PluginFieldKind.Selection && options.Any(option => option.Id == selection.Value),
                    PluginValue.MultipleSelection selection => field.Kind == PluginFieldKind.MultipleSelection
                        && OrEmpty(selection.Values).All(value => options.Any(option => option.Id == value)),
                    PluginValue.Path path => path.IsDirectory ? field.Kind == PluginFieldKind.Directory : field.Kind == PluginFieldKind.File,
                    _ => false,
                };
                if (!valueMatches || (field.Kind is PluginFieldKind.Selection or PluginFieldKind.MultipleSelection && options.IsEmpty))
                {
                    throw new InvalidOperationException($"字段 {field.Id} 的默认值或选项与类型不符。");
                }
            }
        }
    }
}
