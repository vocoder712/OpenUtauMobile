using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenUtau.Core.Format;
using OpenUtau.Core.Ustx;

namespace OpenUtauMobile.Services;

/// <summary>读取来源工程供导入确认界面展示；导入行为直接复用 Core。</summary>
public static class TrackImportService
{
    public static readonly string[] FilePatterns =
        ["*.ustx", "*.ust", "*.vsqx", "*.mid", "*.midi", "*.ufdata", "*.xml", "*.musicxml", "*.svp"];

    // 逐个读取保留失败文件，避免静默导入批次中的一部分。
    public static IReadOnlyList<TrackImportSource> ReadFiles(IEnumerable<string> paths)
    {
        List<TrackImportSource> sources = [];
        StringComparer comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        foreach (string path in paths.Distinct(comparer))
        {
            try
            {
                UProject project = Formats.ReadProject([path]) ?? throw new InvalidDataException("Empty project.");
                if (project.parts.Any(part => part.trackNo < 0 || part.trackNo >= project.tracks.Count))
                {
                    throw new InvalidDataException("Invalid source track index.");
                }
                sources.Add(new TrackImportSource(path, project));
            }
            catch (Exception exception)
            {
                sources.Add(new TrackImportSource(path, null, exception));
            }
        }
        return sources;
    }

}
