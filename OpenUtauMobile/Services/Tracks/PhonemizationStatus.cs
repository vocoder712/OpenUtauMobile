using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Avalonia.Threading;
using OpenUtau.Core.Ustx;

namespace OpenUtauMobile.Services.Tracks;

/// <summary>只在 UI 观察 Core 已有的音素结果版本，不新增后台队列或改变音素器的串行约束。</summary>
internal sealed class PhonemizationStatus : IDisposable
{
    private readonly Func<UProject?> currentProject;
    private readonly Action<int, int, TimeSpan> update;
    private readonly DispatcherTimer timer;
    private readonly HashSet<UVoicePart> batch = [];
    private readonly Stopwatch elapsed = new();
    private UProject? project;

    internal PhonemizationStatus(Func<UProject?> currentProject, Action<int, int, TimeSpan> update)
    {
        this.currentProject = currentProject;
        this.update = update;
        timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(200) };
        timer.Tick += OnTick;
        timer.Start();
    }

    /// <summary>等待包括排队、模型初始化和远程调用；真实完成以当前片段结果版本为准。</summary>
    private void OnTick(object? sender, EventArgs args)
    {
        UProject? current = currentProject();
        if (!ReferenceEquals(project, current))
        {
            project = current;
            batch.Clear();
            elapsed.Reset();
        }
        if (current == null)
        {
            update(0, 0, TimeSpan.Zero);
            return;
        }
        UVoicePart[] pending = [.. current.parts.OfType<UVoicePart>().Where(part => !part.PhonemesUpToDate)];
        if (pending.Length == 0)
        {
            batch.Clear();
            elapsed.Reset();
            update(0, 0, TimeSpan.Zero);
            return;
        }
        if (!elapsed.IsRunning) elapsed.Restart();
        HashSet<UPart> currentParts = [.. current.parts];
        batch.RemoveWhere(part => !currentParts.Contains(part));
        batch.UnionWith(pending);
        update(pending.Length, batch.Count, elapsed.Elapsed);
    }

    /// <summary>编辑器销毁后停止观察，释放片段引用。</summary>
    public void Dispose()
    {
        timer.Stop();
        timer.Tick -= OnTick;
        batch.Clear();
    }
}
