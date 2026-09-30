using System.Runtime.InteropServices.JavaScript;
using System.Threading.Tasks;

namespace OpenUtauMobile.Browser.Services;

internal static partial class BrowserFileSystem
{
    [JSImport("flush", "OpenUtauMobileFileSystem")]
    internal static partial Task FlushAsync();
}
