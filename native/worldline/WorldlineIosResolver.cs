using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OpenUtau.Core.Render;

namespace OpenUtauMobile.iOS
{
    internal static class WorldlineIosResolver
    {
        [ModuleInitializer]
        internal static void Initialize()
        {
            // Worldline 已静态链入主程序；在宿主层解析，保留 Core 的跨平台库名。
            NativeLibrary.SetDllImportResolver(typeof(Worldline).Assembly, Resolve);
        }

        private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            return libraryName == "worldline" ? NativeLibrary.GetMainProgramHandle() : IntPtr.Zero;
        }
    }
}