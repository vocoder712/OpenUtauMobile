using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenUtau.Core.Ustx;

namespace OpenUtauMobile.Services.Tracks;

/// <summary>一次读取的来源工程及其诊断；不持有当前编辑工程。</summary>
public sealed class TrackImportSource
{
    private readonly HashSet<UWavePart> missingAudio;
    public string Path { get; }
    public string Name => System.IO.Path.GetFileName(Path);
    public UProject? Project { get; }
    public Exception? Error { get; }

    public TrackImportSource(string path, UProject? project, Exception? error = null,
        IEnumerable<UWavePart>? missingAudio = null)
    {
        Path = path;
        Project = project;
        Error = error;
        this.missingAudio = [.. missingAudio ?? []];
    }

    /// <summary>使用来源读取阶段的文件状态，确认行构造时不再访问磁盘。</summary>
    public bool IsAudioMissing(UWavePart part) => missingAudio.Contains(part);
}

/// <summary>确认后的轨道选择，保存来源索引，避免轨道重编号影响选择。</summary>
public sealed record TrackImportSelection(TrackImportSource Source, int TrackIndex);
