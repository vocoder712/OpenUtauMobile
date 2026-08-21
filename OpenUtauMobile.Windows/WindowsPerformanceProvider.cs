using System;
using System.Runtime.InteropServices;
using OpenUtauMobile.Services.Performance;

namespace OpenUtauMobile.Windows;

/// <summary>读取 Windows 全局内存和 CPU 时间。</summary>
internal sealed class WindowsPerformanceProvider : IPlatformPerformanceProvider
{
    private const double PercentScale = 100.0;
    private ulong? _previousIdleTime;
    private ulong? _previousKernelTime;
    private ulong? _previousUserTime;

    public PlatformPerformanceMetrics Capture()
    {
        long? totalMemoryBytes = null;
        long? availableMemoryBytes = null;
        MemoryStatusEx memoryStatus = new MemoryStatusEx();
        if (GlobalMemoryStatusEx(ref memoryStatus))
        {
            totalMemoryBytes = checked((long)memoryStatus.TotalPhysical);
            availableMemoryBytes = checked((long)memoryStatus.AvailablePhysical);
        }

        double? systemCpuUsage = CaptureSystemCpuUsage();
        return new PlatformPerformanceMetrics(
            null,
            totalMemoryBytes,
            availableMemoryBytes,
            systemCpuUsage);
    }

    private double? CaptureSystemCpuUsage()
    {
        if (!GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user))
        {
            ResetCpuBaseline();
            return null;
        }

        ulong idleTime = idle.ToUInt64();
        ulong kernelTime = kernel.ToUInt64();
        ulong userTime = user.ToUInt64();
        double? usage = null;

        if (_previousIdleTime is ulong previousIdle &&
            _previousKernelTime is ulong previousKernel &&
            _previousUserTime is ulong previousUser)
        {
            ulong idleDelta = idleTime - previousIdle;
            ulong kernelDelta = kernelTime - previousKernel;
            ulong userDelta = userTime - previousUser;
            ulong totalDelta = kernelDelta + userDelta;
            if (totalDelta > 0UL)
            {
                usage = Math.Clamp(
                    (double)(totalDelta - Math.Min(idleDelta, totalDelta)) / totalDelta * PercentScale,
                    0.0,
                    PercentScale);
            }
        }

        _previousIdleTime = idleTime;
        _previousKernelTime = kernelTime;
        _previousUserTime = userTime;
        return usage;
    }

    private void ResetCpuBaseline()
    {
        _previousIdleTime = null;
        _previousKernelTime = null;
        _previousUserTime = null;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;

        public MemoryStatusEx()
        {
            Length = checked((uint)Marshal.SizeOf<MemoryStatusEx>());
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct FileTime
    {
        private readonly uint _lowDateTime;
        private readonly uint _highDateTime;

        public ulong ToUInt64()
        {
            return ((ulong)_highDateTime << 32) | _lowDateTime;
        }
    }
}

