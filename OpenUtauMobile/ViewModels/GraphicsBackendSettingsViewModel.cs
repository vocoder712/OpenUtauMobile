using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services.Graphics;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;

namespace OpenUtauMobile.ViewModels
{
    public sealed class GraphicsBackendOption(GraphicsBackendPreset? preset, string displayName)
    {
        public GraphicsBackendPreset? Preset { get; } = preset;
        public string DisplayName { get; } = displayName;
    }

    /// <summary>图形设置仅写入偏好；平台服务保留本次启动的策略。</summary>
    public sealed class GraphicsBackendSettingsViewModel : ReactiveObject
    {
        private readonly IGraphicsBackendService? service;
        private GraphicsBackendOption? selectedOption;
        private bool refreshing;
        private bool saving;
        private bool saveFailed;

        public bool IsAvailable => service != null;
        [Reactive] public bool CanSelect { get; private set; }
        [Reactive] public IReadOnlyList<GraphicsBackendOption> Options { get; private set; } = [];
        [Reactive] public string FallbackDescription { get; private set; } = string.Empty;
        [Reactive] public string RestartDescription { get; private set; } = string.Empty;
        [Reactive] public string StatusMessage { get; private set; } = string.Empty;
        [Reactive] public bool HasStatusMessage { get; private set; }

        public GraphicsBackendOption? SelectedOption
        {
            get => selectedOption;
            set
            {
                if (ReferenceEquals(value, selectedOption))
                {
                    return;
                }
                if (!refreshing && (saving || service?.CanSave != true || value?.Preset == null))
                {
                    return;
                }
                this.RaiseAndSetIfChanged(ref selectedOption, value);
                if (!refreshing && value?.Preset is GraphicsBackendPreset preset)
                {
                    _ = SaveAsync(preset);
                }
            }
        }

        public GraphicsBackendSettingsViewModel(IGraphicsBackendService? service)
        {
            this.service = service;
            RefreshLocalization();
        }

        public void RefreshLocalization()
        {
            if (service == null)
            {
                return;
            }
            refreshing = true;
            try
            {
                List<GraphicsBackendOption> options = service.Presets
                    .Select(preset => new GraphicsBackendOption(preset, L.S(preset.TitleResourceKey))).ToList();
                GraphicsBackendOption? selected = options.FirstOrDefault(option =>
                    option.Preset!.FallbackOrder.SequenceEqual(service.SavedFallbackOrder));
                if (selected == null)
                {
                    selected = new GraphicsBackendOption(null, L.S("Settings.Graphics.Custom"));
                    options.Add(selected);
                }
                Options = options;
                SelectedOption = selected;
                FallbackDescription = service.SavedFallbackOrder.Count == 0
                    ? L.S("Settings.Graphics.Default")
                    : string.Join(" → ", service.SavedFallbackOrder);
                bool browser = service.PlatformId == "Browser";
                RestartDescription = L.S(browser ? "Settings.Graphics.ReloadRequired" : "Settings.Graphics.RestartRequired");
                StatusMessage = !service.CanSave ? L.S("Settings.Graphics.StorageUnavailable")
                    : saveFailed ? L.S("Settings.Graphics.SaveFailed")
                    : service.HasInvalidConfiguration ? L.S("Settings.Graphics.InvalidConfiguration")
                    : service.RequiresRestart ? L.S(browser ? "Settings.Graphics.ReloadPending" : "Settings.Graphics.RestartPending")
                    : string.Empty;
                HasStatusMessage = StatusMessage.Length > 0;
                CanSelect = service.CanSave && !saving;
            }
            finally
            {
                refreshing = false;
            }
        }

        private async Task SaveAsync(GraphicsBackendPreset preset)
        {
            saving = true;
            CanSelect = false;
            saveFailed = false;
            try
            {
                await service!.SavePresetAsync(preset);
            }
            catch (Exception exception)
            {
                saveFailed = true;
                Log.Error(exception, "Failed to save graphics backend preferences");
            }
            finally
            {
                saving = false;
                RefreshLocalization();
            }
        }
    }
}
