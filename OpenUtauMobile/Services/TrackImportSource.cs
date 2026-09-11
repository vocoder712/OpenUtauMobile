using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenUtau.Core.Ustx;

namespace OpenUtauMobile.Services;

/// <summary>一次读取的来源工程及其诊断；不持有当前编辑工程。</summary>
public sealed class TrackImportSource
{
    public string Path { get; }
    public string Name => System.IO.Path.GetFileName(Path);
    public UProject? Project { get; }
    public Exception? Error { get; }

    public TrackImportSource(string path, UProject? project, Exception? error = null)
    {
        Path = path;
        Project = project;
        Error = error;
    }
}

/// <summary>确认后的轨道选择，保存来源索引，避免轨道重编号影响选择。</summary>
public sealed record TrackImportSelection(TrackImportSource Source, int TrackIndex);
