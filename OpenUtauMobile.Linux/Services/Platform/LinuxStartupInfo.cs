using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using OpenUtauMobile.Services;
using Serilog;

namespace OpenUtauMobile.Linux.Services.Platform;

/// <summary>读取 Linux 发行版、内核和系统内存信息。</summary>
internal static class LinuxStartupInfo
{
    /// <summary>将 Linux 平台信息写入启动日志。</summary>
    internal static void Write(ILogger logger)
    {
        const string osRelease = "/etc/os-release";
        string releasePath = File.Exists(osRelease) ? osRelease : "/usr/lib/os-release";
        string? version = ReadValue(releasePath, "VERSION_ID=")?.Trim('"', '\'');
        string? kernelVersion = ReadValue("/proc/sys/kernel/osrelease", string.Empty);
        string? memTotal = ReadValue("/proc/meminfo", "MemTotal:");
        ulong? totalMemoryBytes = memTotal is not null &&
            ulong.TryParse(memTotal.Split(' ', 2)[0], NumberStyles.None, CultureInfo.InvariantCulture, out ulong kibibytes)
                ? kibibytes * 1024UL
                : null;

        AppLogging.LogOperatingSystem(logger, "Linux", RuntimeInformation.OSArchitecture,
            version, kernelVersion, totalMemoryBytes);
    }

    private static string? ReadValue(string path, string prefix)
    {
        try
        {
            string? line = File.ReadLines(path).FirstOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal));
            return line?[prefix.Length..].Trim();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
