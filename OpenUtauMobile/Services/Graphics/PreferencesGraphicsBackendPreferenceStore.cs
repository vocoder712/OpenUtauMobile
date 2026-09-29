using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenUtau.Core.Util;

namespace OpenUtauMobile.Services.Graphics
{
    public class PreferencesGraphicsBackendPreferenceStore : IGraphicsBackendPreferenceStore
    {
        public virtual bool CanSave => true;

        public IReadOnlyList<string> Read(string platformId)
        {
            return Preferences.Default.GraphicsBackendFallbackOrders?.GetValueOrDefault(platformId)?.ToArray() ?? [];
        }

        public virtual Task SaveAsync(string platformId, IReadOnlyList<string> order)
        {
            SetOrder(platformId, order);
            Preferences.Save();
            return Task.CompletedTask;
        }

        /// <summary>只更新当前平台，保留其他平台的设置。</summary>
        protected static void SetOrder(string platformId, IReadOnlyList<string> order)
        {
            Preferences.Default.GraphicsBackendFallbackOrders ??= new();
            Preferences.Default.GraphicsBackendFallbackOrders[platformId] = order.ToList();
        }
    }
}
