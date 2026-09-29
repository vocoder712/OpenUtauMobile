using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Serilog;

namespace OpenUtauMobile.Services.Graphics
{
    /// <summary>管理启动策略与偏好；保存设置不会重新初始化图形设备。</summary>
    public sealed class GraphicsBackendService : IGraphicsBackendService
    {
        private readonly IGraphicsBackendPreferenceStore store;
        public string PlatformId { get; }
        public IReadOnlyList<GraphicsBackendPreset> Presets { get; }
        public IReadOnlyList<string> StartupFallbackOrder { get; }
        public IReadOnlyList<string> SavedFallbackOrder { get; private set; }
        public bool HasInvalidConfiguration { get; private set; }
        public bool CanSave => store.CanSave;
        public bool RequiresRestart => !StartupFallbackOrder.SequenceEqual(SavedFallbackOrder);

        public GraphicsBackendService(IPlatformGraphicsBackendProvider provider,
            IGraphicsBackendPreferenceStore? store = null)
        {
            this.store = store ?? new PreferencesGraphicsBackendPreferenceStore();
            PlatformId = provider.PlatformId;
            Presets = provider.Presets;
            IReadOnlyList<string> raw = this.store.Read(PlatformId);
            List<string> normalized = [];
            foreach (string? value in raw)
            {
                string? backend = provider.SupportedBackendIds.FirstOrDefault(id =>
                    string.Equals(id, value?.Trim(), StringComparison.OrdinalIgnoreCase));
                if (backend == null || normalized.Contains(backend))
                {
                    HasInvalidConfiguration = true;
                    continue;
                }
                normalized.Add(backend);
            }

            StartupFallbackOrder = normalized.AsReadOnly();
            SavedFallbackOrder = StartupFallbackOrder;
            Log.Information("Graphics startup policy for {Platform}: requested {Requested}; policy {Policy}",
                PlatformId, string.Join(", ", raw), normalized.Count == 0 ? "Auto" : string.Join(" -> ", normalized));
            if (HasInvalidConfiguration)
            {
                Log.Warning("Invalid or duplicate graphics backend entries ignored for {Platform}; preferences unchanged",
                    PlatformId);
            }
        }

        public async Task SavePresetAsync(GraphicsBackendPreset preset)
        {
            if (!CanSave)
            {
                throw new InvalidOperationException("Graphics preferences storage is unavailable.");
            }
            if (!Presets.Contains(preset))
            {
                throw new ArgumentException("Unknown graphics preset.", nameof(preset));
            }
            await store.SaveAsync(PlatformId, preset.FallbackOrder);
            SavedFallbackOrder = preset.FallbackOrder;
            HasInvalidConfiguration = false;
        }
    }
}
