using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services;
using ReactiveUI;

namespace OpenUtauMobile.ViewModels;

/// <summary>导入确认仅修改选择状态，确认后将筛选后的来源工程交给 Core。</summary>
public sealed class ImportTracksViewModel : PopupViewModelBase
{
    private bool useSourceTiming;
    private bool changingSelection;
    public ObservableCollection<ImportSourceViewModel> Sources { get; } = [];
    public int SelectedCount => Sources.Sum(source => source.Tracks.Count(track => track.IsSelected));
    public bool CanImport => SelectedCount > 0 && Sources.All(source => !source.HasError) &&
        (!UseSourceTiming || TimingSource != null);
    public string ImportLabel => string.Format(L.S("ImportTracks.ImportCount"), SelectedCount);
    public bool UseSourceTiming
    {
        get => useSourceTiming;
        set
        {
            this.RaiseAndSetIfChanged(ref useSourceTiming, value);
            this.RaisePropertyChanged(nameof(CanImport));
        }
    }
    // 与桌面端一致：时间设置来自传入 Core 的首个工程，不单独指定另一来源。
    public ImportSourceViewModel? TimingSource => Sources.FirstOrDefault(source => source.Tracks.Any(track => track.IsSelected));
    public string TimingSummary
    {
        get
        {
            UProject? project = TimingSource?.Source.Project;
            if (project == null || project.tempos.Count == 0 || project.timeSignatures.Count == 0) return string.Empty;
            return string.Format(L.S("ImportTracks.TimingSummary"), project.tempos[0].bpm,
                project.timeSignatures[0].beatPerBar, project.timeSignatures[0].beatUnit,
                project.tempos.Count, project.timeSignatures.Count);
        }
    }
    public string TimingDetails => TimingSource?.Source.Project is UProject project
        ? string.Join(Environment.NewLine, project.tempos.Select(tempo =>
            string.Format(L.S("ImportTracks.TempoMarker"), tempo.position, tempo.bpm))
            .Concat(project.timeSignatures.Select(signature => string.Format(L.S("ImportTracks.SignatureMarker"),
                signature.barPosition + 1, signature.beatPerBar, signature.beatUnit)))) : string.Empty;

    public ReactiveCommand<Unit, Unit> ImportCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }
    public ReactiveCommand<Unit, Unit> SelectAllCommand { get; }
    public ReactiveCommand<Unit, Unit> SelectNoneCommand { get; }
    public ReactiveCommand<ImportSourceViewModel, Unit> RemoveSourceCommand { get; }

    public ImportTracksViewModel(UProject destination, IReadOnlyList<TrackImportSource> sources)
    {
        useSourceTiming = destination.parts.Count == 0;
        foreach (TrackImportSource source in sources)
        {
            Sources.Add(new ImportSourceViewModel(source, RefreshSelection));
        }
        RefreshSelection();
        ImportCommand = ReactiveCommand.Create(() => RaiseClose(true), this.WhenAnyValue(model => model.CanImport));
        CancelCommand = ReactiveCommand.Create(RequestBack);
        SelectAllCommand = ReactiveCommand.Create(() => SelectAll(true));
        SelectNoneCommand = ReactiveCommand.Create(() => SelectAll(false));
        RemoveSourceCommand = ReactiveCommand.Create<ImportSourceViewModel>(source =>
        {
            Sources.Remove(source);
            RefreshSelection();
        });
    }

    public UProject[] GetSelectedProjects()
    {
        if (!CanImport) throw new InvalidOperationException("Import is not ready.");
        List<UProject> projects = [];
        foreach (ImportSourceViewModel source in Sources)
        {
            ImportTrackViewModel[] selected = source.Tracks.Where(track => track.IsSelected).ToArray();
            if (selected.Length == 0) continue;
            UProject project = source.Source.Project!;
            Dictionary<int, int> indices = selected.Select((track, index) =>
                new KeyValuePair<int, int>(track.Selection.TrackIndex, index)).ToDictionary();
            // 这里只筛选尚未提交的来源数据；追加、表达式合并和时间设置全部由 Core 负责。
            List<UTrack> tracks = selected.Select(track => project.tracks[track.Selection.TrackIndex]).ToList();
            List<UPart> parts = project.parts.Where(part => indices.ContainsKey(part.trackNo)).ToList();
            foreach (UPart part in parts) part.trackNo = indices[part.trackNo];
            for (int index = 0; index < tracks.Count; index++) tracks[index].TrackNo = index;
            project.tracks = tracks;
            project.parts = parts;
            projects.Add(project);
        }
        return projects.ToArray();
    }

    private void SelectAll(bool selected)
    {
        changingSelection = true;
        try
        {
            foreach (ImportTrackViewModel track in Sources.SelectMany(source => source.Tracks)) track.IsSelected = selected;
        }
        finally
        {
            changingSelection = false;
            RefreshSelection();
        }
    }

    private void RefreshSelection()
    {
        if (changingSelection) return;
        this.RaisePropertyChanged(nameof(TimingSource));
        this.RaisePropertyChanged(nameof(TimingSummary));
        this.RaisePropertyChanged(nameof(TimingDetails));
        this.RaisePropertyChanged(nameof(SelectedCount));
        this.RaisePropertyChanged(nameof(ImportLabel));
        this.RaisePropertyChanged(nameof(CanImport));
    }
}

public sealed class ImportSourceViewModel
{
    public TrackImportSource Source { get; }
    public string Name => Source.Name;
    public bool HasError => Source.Error != null;
    public string Error => Source.Error?.Message ?? string.Empty;
    public string Summary => string.Format(L.S("ImportTracks.SourceSummary"), Tracks.Count);
    public IReadOnlyList<ImportTrackViewModel> Tracks { get; }

    public ImportSourceViewModel(TrackImportSource source, Action changed)
    {
        Source = source;
        Tracks = source.Project?.tracks.Select((track, index) =>
            new ImportTrackViewModel(new TrackImportSelection(source, index), changed)).ToArray() ?? [];
    }
}

public sealed class ImportTrackViewModel : ReactiveObject
{
    private bool selected = true;
    private readonly Action changed;
    public TrackImportSelection Selection { get; }
    public string Name { get; }
    public string Summary { get; }
    public string Warning { get; }
    public bool HasWarning => !string.IsNullOrEmpty(Warning);
    public bool IsSelected
    {
        get => selected;
        set
        {
            if (selected == value) return;
            this.RaiseAndSetIfChanged(ref selected, value);
            changed();
        }
    }

    public ImportTrackViewModel(TrackImportSelection selection, Action changed)
    {
        Selection = selection;
        this.changed = changed;
        UProject project = selection.Source.Project!;
        UTrack track = project.tracks[selection.TrackIndex];
        UPart[] parts = project.parts.Where(part => part.trackNo == selection.TrackIndex).ToArray();
        Name = track.TrackName;
        Summary = string.Format(L.S("ImportTracks.TrackSummary"), parts.OfType<UVoicePart>().Count(), parts.OfType<UWavePart>().Count());
        List<string> warnings = [];
        if (parts.OfType<UVoicePart>().Any() && (track.Singer == null || !track.Singer.Found))
        {
            warnings.Add(string.Format(L.S("ImportTracks.MissingSinger"), track.Singer?.Id ?? track.singer ?? L.S("ImportTracks.Unassigned")));
        }
        foreach (UWavePart part in parts.OfType<UWavePart>().Where(part => !File.Exists(part.FilePath)))
        {
            warnings.Add(string.Format(L.S("ImportTracks.MissingAudio"), part.FilePath));
        }
        // ValidateVoiceColor 会改写名称数组；预览只比较，保留来源映射供用户检查。
        if (track.VoiceColorNames.Length > 1 && track.VoiceColorExp != null &&
            !track.VoiceColorNames.SequenceEqual(track.VoiceColorExp.options))
        {
            warnings.Add(L.S("ImportTracks.ColorMismatch"));
        }
        Warning = string.Join(Environment.NewLine, warnings);
    }
}
