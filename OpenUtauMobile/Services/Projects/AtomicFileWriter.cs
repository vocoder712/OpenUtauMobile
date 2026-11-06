using System;
using System.IO;
using System.Text;
using Serilog;

namespace OpenUtauMobile.Services.Projects;

/// <summary>将独立数据写入同目录暂存文件，再替换目标；失败时保留旧目标。</summary>
internal static class AtomicFileWriter
{
    /// <summary>仅供后台调用；显式刷新编码器及文件后才执行替换。</summary>
    public static long Write(string path, Action<TextWriter> serialize, Action? replacing = null)
    {
        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        string staging = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            long length;
            using (FileStream stream = new(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536))
            {
                using (StreamWriter writer = new(stream, Encoding.UTF8, 65536, leaveOpen: true))
                {
                    serialize(writer);
                    writer.Flush();
                }
                // 一次最终持久化刷新，不对每个缓冲块同步闪存。
                if (OperatingSystem.IsBrowser()) stream.Flush();
                else stream.Flush(flushToDisk: true);
                length = stream.Length;
            }
            replacing?.Invoke();
            File.Move(staging, fullPath, overwrite: true);
            return length;
        }
        finally
        {
            try { if (File.Exists(staging)) File.Delete(staging); }
            catch (Exception error) { Log.Warning(error, "无法清理保存暂存文件 {Path}", staging); }
        }
    }
}
