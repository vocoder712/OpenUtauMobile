using System;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenUtauMobile.Services.Platform;

/// <summary>读取 Apple 系统的 Darwin 内核与物理内存信息。</summary>
public static partial class AppleSystemInfo
{
    /// <summary>读取 Darwin 内核发行版本。</summary>
    public static unsafe string? GetKernelVersion()
    {
        const string name = "kern.osrelease";
        nuint length = 0;
        if (SysctlByName(name, null, ref length, 0, 0) != 0 || length == 0)
        {
            return null;
        }

        byte* buffer = stackalloc byte[(int)length];
        return SysctlByName(name, buffer, ref length, 0, 0) == 0
            ? Encoding.UTF8.GetString(new ReadOnlySpan<byte>(buffer, (int)length - 1))
            : null;
    }

    /// <summary>读取系统报告的总物理内存字节数。</summary>
    public static unsafe ulong? GetTotalPhysicalMemoryBytes()
    {
        ulong bytes = 0;
        nuint length = sizeof(ulong);
        return SysctlByName("hw.memsize", &bytes, ref length, 0, 0) == 0 ? bytes : null;
    }

    [LibraryImport("/usr/lib/libSystem.B.dylib", EntryPoint = "sysctlbyname", StringMarshalling = StringMarshalling.Utf8)]
    private static unsafe partial int SysctlByName(string name, void* oldValue, ref nuint oldLength,
        nint newValue, nuint newLength);
}
