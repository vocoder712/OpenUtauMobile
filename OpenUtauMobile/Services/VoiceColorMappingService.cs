using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenUtau.Core;
using OpenUtau.Core.Format;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Controls;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Services;

/// <summary>移植桌面的导入后映射流程，复用已有音素表达式命令。</summary>
public static class VoiceColorMappingService
{
    private const int DefaultIndex = VoiceColorMappingViewModel.DefaultIndex;
    private const int NormalizedMaximum = 1;
    private const int PercentMaximum = 100;
    private const string VoiceCurvePrefix = "cl";
    private const string VoiceCurveIndexFormat = "D2";
    private const string OpeningCurve = "opec";
    private static bool isMapping;

    /// <summary>在 UI 线程调用；导入检查全部轨道，歌手变更检查指定轨道。</summary>
    public static async Task ValidateAsync(UProject project, UTrack? requestedTrack = null, bool validate = true)
    {
        DocManager manager = DocManager.Inst;
        if (isMapping || !ReferenceEquals(manager.Project, project)) return;
        if (manager.HasOpenUndoGroup) throw new InvalidOperationException("An edit is still active.");
        UTrack[] tracks = requestedTrack == null ? project.tracks.ToArray() : [requestedTrack];
        isMapping = true;
        manager.StartUndoGroup("VoiceMapping.Undo");
        try
        {
            foreach (UTrack track in tracks)
            {
                if (!IsCurrent(project, track)) break;
                string[] oldColors = track.VoiceColorNames.ToArray();
                string[] newColors = track.VoiceColorExp?.options ?? [];
                if (!validate || track.ValidateVoiceColor(out oldColors, out newColors))
                {
                    UVoicePart[] parts = project.parts.OfType<UVoicePart>()
                        .Where(part => part.trackNo == track.TrackNo && part.notes.Count > 0).ToArray();
                    if (parts.Length > 0 && newColors.Length > 0)
                    {
                        VoiceColorMappingViewModel model = new(oldColors, newColors, track.TrackName);
                        if (await PopupService.Show<bool>(new VoiceColorMappingPopup(), model) && IsCurrent(project, track))
                        {
                            ApplyVoiceColors(project, track, parts, model);
                        }
                    }
                    else if (!validate)
                    {
                        ToastService.Enqueue(L.S("VoiceMapping.NoNotes"));
                    }
                }
                if (IsCurrent(project, track)) await RemapVocalModesAsync(project, track);
            }
        }
        finally
        {
            try
            {
                if (ReferenceEquals(manager.Project, project) && manager.HasOpenUndoGroup) manager.EndUndoGroup();
            }
            finally
            {
                isMapping = false;
            }
        }
    }

    private static bool IsCurrent(UProject project, UTrack track)
        => ReferenceEquals(DocManager.Inst.Project, project) && project.tracks.Contains(track);

    private static void ApplyVoiceColors(UProject project, UTrack track, IEnumerable<UVoicePart> parts,
        VoiceColorMappingViewModel model)
    {
        foreach (UVoicePart part in parts)
        {
            foreach (UPhoneme phoneme in part.phonemes)
            {
                float oldValue = phoneme.GetExpression(project, track, Ustx.CLR).Item1;
                VoiceColorMappingRow? mapping = model.Mappings.FirstOrDefault(row => row.OldIndex == oldValue);
                if (mapping != null && mapping.OldIndex == mapping.SelectedIndex) continue;
                float? value = mapping == null || mapping.SelectedIndex == DefaultIndex ? null : mapping.SelectedIndex;
                DocManager.Inst.ExecuteCmd(new SetPhonemeExpressionCommand(project, track, part, phoneme, Ustx.CLR, value));
            }
        }
    }

    private static async Task RemapVocalModesAsync(UProject project, UTrack track)
    {
        if (track.Singer?.SingerType != USingerType.DiffSinger) return;
        track.Singer.EnsureLoaded();
        if (!track.Singer.Loaded) return;
        UVoicePart[] parts = project.parts.OfType<UVoicePart>().Where(part => part.trackNo == track.TrackNo).ToArray();
        string[] modes = parts.SelectMany(part => part.curves)
            .Where(curve => curve.descriptor != null && IsImportedVocalModeCurve(curve.abbr))
            .Select(curve => curve.descriptor.name).Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (modes.Length == 0) return;
        string[] colors = track.Singer.Subbanks.Select(subbank => subbank.Color).ToArray();
        if (colors.Length == 0) return;
        VoiceColorMappingViewModel model = new(new[] { string.Empty }.Concat(modes).ToArray(), colors, track.TrackName, true);
        if (!await PopupService.Show<bool>(new VoiceColorMappingPopup(), model) || !IsCurrent(project, track)) return;
        bool changed = false;
        foreach (VoiceColorMappingRow mapping in model.Mappings.Where(row => row.OldIndex > DefaultIndex && row.SelectedIndex > DefaultIndex))
        {
            string sourceName = modes[mapping.OldIndex - 1];
            UExpressionDescriptor? sourceDescriptor = project.expressions.Values.FirstOrDefault(descriptor =>
                descriptor.name.Equals(sourceName, StringComparison.OrdinalIgnoreCase));
            if (sourceDescriptor == null) continue;
            string targetAbbr = VoiceCurvePrefix + mapping.SelectedIndex.ToString(VoiceCurveIndexFormat);
            if (!project.expressions.TryGetValue(targetAbbr, out UExpressionDescriptor? targetDescriptor))
            {
                targetDescriptor = new UExpressionDescriptor($"voice color {colors[mapping.SelectedIndex]}",
                    targetAbbr, DefaultIndex, PercentMaximum, DefaultIndex) { type = UExpressionType.Curve };
                project.RegisterExpression(targetDescriptor);
            }
            foreach (UVoicePart part in parts)
            {
                UCurve? source = part.curves.FirstOrDefault(curve => curve.abbr == sourceDescriptor.abbr);
                if (source == null || part.curves.Any(curve => curve.abbr == targetAbbr)) continue;
                // 与桌面保持一致：复制曲线、归一化数值，不覆盖既有目标曲线。
                part.curves.Add(new UCurve(targetDescriptor)
                {
                    xs = source.xs.ToList(),
                    ys = source.ys.Select(value => Math.Clamp(value <= NormalizedMaximum
                        ? value * PercentMaximum : value, DefaultIndex, PercentMaximum)).ToList(),
                });
                changed = true;
            }
        }
        if (changed) DocManager.Inst.ExecuteCmd(new ValidateProjectNotification());
    }

    private static bool IsImportedVocalModeCurve(string abbreviation)
    {
        if (abbreviation.StartsWith(VoiceCurvePrefix, StringComparison.OrdinalIgnoreCase)) return false;
        return abbreviation != Ustx.DYN && abbreviation != Ustx.PITD && abbreviation != Ustx.TENC &&
            abbreviation != Ustx.BREC && abbreviation != Ustx.GENC && abbreviation != Ustx.VOIC &&
            abbreviation != Ustx.SHFC && abbreviation != Ustx.CLR && abbreviation != Ustx.CLRY && abbreviation != OpeningCurve;
    }
}
