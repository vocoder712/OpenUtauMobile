using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using OpenUtau.Core;
using OpenUtauMobile.Services.Graphics;
using Serilog;

/*
 * TODO: 后续统一持久化存储后移除这种workaround
 */

namespace OpenUtauMobile.Browser.Graphics
{
    /// <summary>将浏览器持久化列表恢复到 Preferences，再由共享服务读取。</summary>
    public sealed partial class BrowserGraphicsBackendPreferenceStore : PreferencesGraphicsBackendPreferenceStore
    {
        private const string ModuleName = "OpenUtauMobileGraphicsPreferences";
        private bool _canSave;
        public override bool CanSave => _canSave;

        public static async Task<BrowserGraphicsBackendPreferenceStore> CreateAsync()
        {
            BrowserGraphicsBackendPreferenceStore store = new();
            try
            {
                Directory.CreateDirectory(PathManager.Inst.DataPath);
                await JSHost.ImportAsync(ModuleName, "./graphics-preferences.js");
                string? json = ReadOrder();
                store._canSave = true;
                if (json != null)
                {
                    try
                    {
                        string[] order = JsonSerializer.Deserialize(json, GraphicsPreferenceJsonContext.Default.StringArray)
                            ?? throw new JsonException("Expected a graphics backend array.");
                        SetOrder("Browser", order);
                    }
                    catch (JsonException exception)
                    {
                        Log.Warning(exception, "Invalid browser graphics preferences; using automatic rendering");
                        SetOrder("Browser", []);
                    }
                }
            }
            catch (Exception exception)
            {
                store._canSave = false;
                Log.Warning(exception, "Browser graphics preferences storage is unavailable");
                SetOrder("Browser", []);
            }
            return store;
        }

        public override Task SaveAsync(string platformId, IReadOnlyList<string> order)
        {
            if (!_canSave)
            {
                throw new InvalidOperationException("Browser graphics preferences storage is unavailable.");
            }
            // 先写入持久化存储；失败时不修改当前偏好，也不报告保存成功。
            WriteOrder(JsonSerializer.Serialize([.. order], GraphicsPreferenceJsonContext.Default.StringArray));
            return base.SaveAsync(platformId, order);
        }

        [JSImport("readOrder", ModuleName)]
        private static partial string? ReadOrder();

        [JSImport("writeOrder", ModuleName)]
        private static partial void WriteOrder(string json);
    }

    [JsonSerializable(typeof(string[]))]
    internal partial class GraphicsPreferenceJsonContext : JsonSerializerContext
    {
    }
}
