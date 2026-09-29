using System;
using System.Collections.Generic;

namespace OpenUtauMobile.Services.Graphics
{
    /// <summary>平台预设仅保存资源键，启动阶段不依赖界面或语言资源。</summary>
    public sealed class GraphicsBackendPreset(string id, string titleResourceKey, params string[] fallbackOrder)
    {
        public string Id { get; } = id;
        public string TitleResourceKey { get; } = titleResourceKey;
        public IReadOnlyList<string> FallbackOrder { get; } = Array.AsReadOnly((string[])fallbackOrder.Clone());
    }
}
