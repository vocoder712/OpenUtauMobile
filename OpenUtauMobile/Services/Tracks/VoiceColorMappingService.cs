using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using OpenUtau.Core;
using OpenUtau.Core.Format;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Controls;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services.Dialogs;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Services.Tracks;

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
        if (isMapping || TrackPreparationService.Inst.IsBusy || !ReferenceEquals(manager.Project, project)) return;
        if (manager.HasOpenUndoGroup) throw new InvalidOperationException("An edit is still active.");
        UTrack[] tracks = requestedTrack == null ? [.. project.tracks] : [requestedTrack];
        isMapping = true;
        try
        {
            foreach (UTrack track in tracks)
            {
                if (!IsCurrent(project, track)) break;
                TrackPreparationOwner owner = new(project, track);
                if (track.Singer is { Found: true, Loaded: false } singer)
                {
                    await TrackPreparationService.Inst.RunAsync(async _ => await TrackPreparationService.EnsureSingerAsync(singer));
                    if (!owner.IsCurrent) return;
                    track.Validate(new ValidateOptions { SkipTiming = true }, project);
                }
                string[] oldColors = [.. track.VoiceColorNames];
                string[] newColors = [.. track.VoiceColorExp?.options ?? []];
                // 不调用会提前覆盖 VoiceColorNames 的校验方法，取消映射后保留原音色表。
                if (!validate || oldColors.Length > 1 && newColors.Length > 0 && !oldColors.SequenceEqual(newColors))
                {
                    UVoicePart[] parts = [.. project.parts.OfType<UVoicePart>()
                        .Where(part => part.trackNo == track.TrackNo && part.notes.Count > 0)];
                    if (parts.Length > 0 && newColors.Length > 0)
                    {
                        VoiceColorMappingViewModel model = new(oldColors, newColors, track.TrackName);
                        if (await PopupService.Show<bool>(new VoiceColorMappingPopup(), model) && owner.IsCurrent)
                        {
                            await ApplyVoiceColorsAsync(project, track, parts, model, owner, newColors);
                        }
                    }
                    else if (!validate)
                    {
                        ToastService.Enqueue(L.S("VoiceMapping.NoNotes"));
                    }
                }
                if (owner.IsCurrent) await RemapVocalModesAsync(project, track, owner);
            }
        }
        finally
        {
            isMapping = false;
        }
    }

    private static bool IsCurrent(UProject project, UTrack track)
        => ReferenceEquals(DocManager.Inst.Project, project) && project.tracks.Contains(track);

    /// <summary>在模态下分片捕获音素表达式，后台查表，最后只提交一个可撤销命令。</summary>
    private static async Task ApplyVoiceColorsAsync(UProject project, UTrack track, UVoicePart[] parts,
        VoiceColorMappingViewModel model, TrackPreparationOwner owner, string[] newColors)
    {
        Dictionary<float, int> mappings = model.Mappings.ToDictionary(row => (float)row.OldIndex, row => row.SelectedIndex);
        await TrackPreparationService.Inst.RunAsync(async loading =>
        {
            loading.SetIndeterminate(L.S("TrackPreparation.Mapping"));
            List<(UVoicePart Part, UPhoneme Phoneme, float Value)> inputs = [];
            int work = 0;
            foreach (UVoicePart part in parts)
            {
                // 后台响应可能替换派生音素列表，分片前先固定本批引用，不持有活动枚举器跨 await。
                UPhoneme[] phonemes = [.. part.phonemes];
                foreach (UPhoneme phoneme in phonemes)
                {
                    if (!owner.IsCurrent) return;
                    inputs.Add((part, phoneme, phoneme.GetExpression(project, track, Ustx.CLR).Item1));
                    if (++work % 128 == 0) await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                }
            }
            (int Index, float? Value)[] results = await Task.Run<(int Index, float? Value)[]>(() => [.. inputs.Select((input, index) =>
            {
                bool found = mappings.TryGetValue(input.Value, out int selected);
                return (Index: index, Value: found && selected != DefaultIndex ? (float?)selected : null,
                    Changed: !found || selected != input.Value);
            }).Where(result => result.Changed).Select(result => (result.Index, result.Value))]);
            if (!owner.IsCurrent) return;
            List<SetPhonemeExpressionCommand> commands = [];
            foreach ((int index, float? value) in results)
            {
                (UVoicePart part, UPhoneme phoneme, _) = inputs[index];
                if (!project.parts.Contains(part) || phoneme.Parent == null || !part.notes.Contains(phoneme.Parent)) return;
                commands.Add(new SetPhonemeExpressionCommand(project, track, part, phoneme, Ustx.CLR, value));
                if (++work % 128 == 0) await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            }
            if (!owner.IsCurrent) return;
            string[] previousColors = track.VoiceColorNames;
            Commit(new VoiceMappingCommand(project, track,
                () => { foreach (SetPhonemeExpressionCommand command in commands) command.Execute(); track.VoiceColorNames = newColors; },
                () => { for (int index = commands.Count - 1; index >= 0; index--) commands[index].Unexecute(); track.VoiceColorNames = previousColors; },
                rephonemize: true));
        });
    }

    private static async Task RemapVocalModesAsync(UProject project, UTrack track, TrackPreparationOwner owner)
    {
        if (track.Singer?.SingerType != USingerType.DiffSinger) return;
        if (!track.Singer.Loaded) return;
        UVoicePart[] parts = [.. project.parts.OfType<UVoicePart>().Where(part => part.trackNo == track.TrackNo)];
        string[] modes = [.. parts.SelectMany(part => part.curves)
            .Where(curve => curve.descriptor != null && IsImportedVocalModeCurve(curve.abbr))
            .Select(curve => curve.descriptor.name).Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)];
        if (modes.Length == 0) return;
        string[] colors = [.. track.Singer.Subbanks.Select(subbank => subbank.Color)];
        if (colors.Length == 0) return;
        VoiceColorMappingViewModel model = new([string.Empty, .. modes], colors, track.TrackName, true);
        if (!await PopupService.Show<bool>(new VoiceColorMappingPopup(), model) || !owner.IsCurrent) return;
        await TrackPreparationService.Inst.RunAsync(async loading =>
        {
            loading.SetIndeterminate(L.S("TrackPreparation.Mapping"));
            List<UExpressionDescriptor> descriptors = [];
            List<(UVoicePart Part, UCurve Curve)> additions = [];
            foreach (VoiceColorMappingRow mapping in model.Mappings.Where(row => row.OldIndex > DefaultIndex && row.SelectedIndex > DefaultIndex))
            {
                string sourceName = modes[mapping.OldIndex - 1];
                UExpressionDescriptor? sourceDescriptor = project.expressions.Values.FirstOrDefault(descriptor =>
                    descriptor.name.Equals(sourceName, StringComparison.OrdinalIgnoreCase));
                if (sourceDescriptor == null) continue;
                string targetAbbr = VoiceCurvePrefix + mapping.SelectedIndex.ToString(VoiceCurveIndexFormat);
                UExpressionDescriptor? targetDescriptor = descriptors.FirstOrDefault(descriptor => descriptor.abbr == targetAbbr);
                if (targetDescriptor == null && !project.expressions.TryGetValue(targetAbbr, out targetDescriptor))
                {
                    targetDescriptor = new UExpressionDescriptor($"voice color {colors[mapping.SelectedIndex]}",
                        targetAbbr, DefaultIndex, PercentMaximum, DefaultIndex) { type = UExpressionType.Curve };
                    descriptors.Add(targetDescriptor);
                }
                foreach (UVoicePart part in parts)
                {
                    UCurve? source = part.curves.FirstOrDefault(curve => curve.abbr == sourceDescriptor.abbr);
                    if (source == null || part.curves.Any(curve => curve.abbr == targetAbbr) ||
                        additions.Any(addition => ReferenceEquals(addition.Part, part) && addition.Curve.abbr == targetAbbr)) continue;
                    // UI 只捕获数值快照，后台复制、归一化，不覆盖既有目标曲线。
                    int[] xs = [.. source.xs];
                    int[] ys = [.. source.ys];
                    UCurve prepared = await Task.Run(() => new UCurve(targetDescriptor)
                    {
                        xs = [.. xs],
                        ys = [.. ys.Select(value => Math.Clamp(value <= NormalizedMaximum
                            ? value * PercentMaximum : value, DefaultIndex, PercentMaximum))],
                    });
                    if (!owner.IsCurrent || !project.parts.Contains(part)) return;
                    additions.Add((part, prepared));
                }
            }
            if (!owner.IsCurrent || additions.Count == 0) return;
            Commit(new VoiceMappingCommand(project, track,
                () =>
                {
                    foreach (UExpressionDescriptor descriptor in descriptors) project.RegisterExpression(descriptor);
                    foreach ((UVoicePart part, UCurve curve) in additions) part.curves.Add(curve);
                },
                () =>
                {
                    foreach ((UVoicePart part, UCurve curve) in additions) part.curves.Remove(curve);
                    foreach (UExpressionDescriptor descriptor in descriptors) project.expressions.Remove(descriptor.abbr);
                }, rephonemize: false));
        });
    }

    /// <summary>等待弹窗和计算期间没有开放撤销组，提交后只触发一次合并校验。</summary>
    private static void Commit(VoiceMappingCommand command)
    {
        DocManager manager = DocManager.Inst;
        if (manager.HasOpenUndoGroup) return;
        Stopwatch commit = Stopwatch.StartNew();
        manager.StartUndoGroup("VoiceMapping.Undo");
        try { manager.ExecuteCmd(command); }
        catch { manager.RollBackUndoGroup(); throw; }
        finally
        {
            try { if (manager.HasOpenUndoGroup) manager.EndUndoGroup(); }
            finally { Serilog.Log.Information("轨道提交 Mapping：{Milliseconds:F2} ms", commit.Elapsed.TotalMilliseconds); }
        }
    }

    /// <summary>仅在 UI 原子地应用已准备的映射，撤销不重新运行映射计算。</summary>
    private sealed class VoiceMappingCommand : TrackCommand
    {
        private readonly Action apply;
        private readonly Action undo;
        private readonly bool rephonemize;

        internal VoiceMappingCommand(UProject project, UTrack track, Action apply, Action undo, bool rephonemize)
        {
            this.project = project;
            this.track = track;
            this.apply = apply;
            this.undo = undo;
            this.rephonemize = rephonemize;
        }

        public override ValidateOptions ValidateOptions => new() { SkipTiming = true, SkipPhonemizer = !rephonemize };
        public override void Execute() => apply();
        public override void Unexecute() => undo();
        public override string ToString() => "Map track voice colors";
    }

    private static bool IsImportedVocalModeCurve(string abbreviation)
    {
        if (abbreviation.StartsWith(VoiceCurvePrefix, StringComparison.OrdinalIgnoreCase)) return false;
        return abbreviation != Ustx.DYN && abbreviation != Ustx.PITD && abbreviation != Ustx.TENC &&
            abbreviation != Ustx.BREC && abbreviation != Ustx.GENC && abbreviation != Ustx.VOIC &&
            abbreviation != Ustx.SHFC && abbreviation != Ustx.CLR && abbreviation != Ustx.CLRY && abbreviation != OpeningCurve;
    }
}
