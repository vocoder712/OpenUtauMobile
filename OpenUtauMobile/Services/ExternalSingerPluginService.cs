using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtauMobile.Services;

/// <summary>初始化声明启动入口的外部插件，并集中筛选具体歌手的渲染器。</summary>
public static class ExternalSingerPluginService
{
    private static readonly List<Func<USinger, string[]?>> policies = [];
    private static readonly HashSet<Assembly> initialized = [];
    private static readonly List<Action> refreshers = [];
    private static readonly List<Func<UProject, bool>> normalizers = [];

    /// <summary>在核心歌手首次扫描后、工程载入前调用插件的约定入口。</summary>
    public static void Initialize()
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (initialized.Contains(assembly)) continue;
            string? entry = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(attribute => attribute.Key == "OpumPluginStartup")?.Value;
            if (entry == null) continue;
            try
            {
                Type type = assembly.GetType(entry, true)!;
                MethodInfo method = type.GetMethod("Initialize", [typeof(Action<Func<USinger, string[]>>)])
                    ?? throw new MissingMethodException(entry, "Initialize");
                List<Func<USinger, string[]?>> pending = [];
                method.Invoke(null, [new Action<Func<USinger, string[]?>>(pending.Add)]);
                policies.AddRange(pending);
                if (type.GetMethod("RefreshSingers", Type.EmptyTypes) is { } refresh)
                    refreshers.Add(() => refresh.Invoke(null, null));
                if (type.GetMethod("NormalizeProject", [typeof(UProject)]) is { } normalize)
                    normalizers.Add(project => (bool)normalize.Invoke(null, [project])!);
                initialized.Add(assembly);
            }
            catch (Exception exception) { Log.Error(exception, "Failed to initialize singer plugin {Assembly}", assembly.FullName); }
        }
    }

    /// <summary>在没有发布歌手变更通知的直接重扫之后恢复外部注册。</summary>
    public static void RefreshSingers()
    {
        foreach (Action refresh in refreshers)
            try { refresh(); } catch (Exception exception) { Log.Error(exception, "Failed to refresh external singers"); }
    }

    /// <summary>按实际歌手实例筛选可用渲染器。</summary>
    public static string[] GetSupportedRenderers(USinger singer)
    {
        foreach (Func<USinger, string[]?> policy in policies)
            if (policy(singer) is { } options) return options;
        return OpenUtau.Core.Render.Renderers.GetSupportedRenderers(singer.SingerType);
    }

    /// <summary>返回具体歌手的默认渲染器。</summary>
    public static string GetDefaultRenderer(USinger singer)
    {
        string[] options = GetSupportedRenderers(singer);
        string preferred = OpenUtau.Core.Render.Renderers.GetDefaultRenderer(singer.SingerType);
        return options.Contains(preferred) ? preferred : options.FirstOrDefault() ?? string.Empty;
    }

    /// <summary>在载入及轨道导入时补正外部 renderer，并重新校验快照。</summary>
    public static void NormalizeProject(UProject project)
    {
        bool changed = false;
        foreach (Func<UProject, bool> normalize in normalizers) changed |= normalize(project);
        if (changed) project.ValidateFull();
    }
}
