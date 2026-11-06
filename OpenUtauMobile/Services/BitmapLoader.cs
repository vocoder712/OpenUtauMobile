using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace OpenUtauMobile.Services;

/// <summary>在后台解码独立图片数据，限制并行数以控制移动设备的内存峰值。</summary>
internal static class BitmapLoader
{
    private static readonly SemaphoreSlim DecodeSlots = new(2, 2);

    /// <summary>字节数据在解码期间保持只读；调用方拥有返回图片并负责释放晚到结果。</summary>
    public static async Task<Bitmap> DecodeAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        await DecodeSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using MemoryStream stream = new(data);
                return new Bitmap(stream);
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            DecodeSlots.Release();
        }
    }
}
