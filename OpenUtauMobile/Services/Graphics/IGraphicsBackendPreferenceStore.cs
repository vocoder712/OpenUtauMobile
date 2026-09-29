using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenUtauMobile.Services.Graphics
{
    public interface IGraphicsBackendPreferenceStore
    {
        bool CanSave { get; }
        IReadOnlyList<string> Read(string platformId);
        Task SaveAsync(string platformId, IReadOnlyList<string> order);
    }
}
