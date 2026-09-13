using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Avalonia.Media;
using OpenUtau.Core.Ustx;
using OpenUtau.Core;
using OpenUtauMobile.Services;
using OpenUtauMobile.Themes.OpenUtauMobile.Runtime;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtauMobile.ViewModels;

/// <summary>混音面板状态；参数通过工程命令提交，电平来自实际播放。</summary>
public class MixerViewModel : ViewModelBase
{
    public ObservableCollection<MixerChannelViewModel> Channels { get; } = [];
    public MixerChannelViewModel Master { get; } = new(null);
    public ObservableCollection<MixerChannelViewModel> FxChannels { get; } = [];
    [Reactive] public MixerChannelViewModel? SelectedChannel { get; set; }
    [Reactive] public bool IsDetailOpen { get; set; }
    [Reactive] public bool IsWide { get; set; }
    [Reactive] public double ChannelHeight { get; set; } = 360;
    public bool IsEmpty => Channels.Count == 0;
    private UProject? _project;
    private bool _refreshing;
    private bool _active;
    private bool _ownsEdit;

    public MixerViewModel() => Master.PropertyChanged += OnChannelChanged;

    public void Activate() => _active = true;
    public void Deactivate() { EndEdit(); _active = false; }

    public void BeginEdit()
    {
        if (!_active || _ownsEdit || DocManager.Inst.HasOpenUndoGroup) return;
        DocManager.Inst.StartUndoGroup("调整混音参数");
        _ownsEdit = true;
    }

    public void EndEdit()
    {
        if (!_ownsEdit) return;
        _ownsEdit = false;
        if (DocManager.Inst.HasOpenUndoGroup) DocManager.Inst.EndUndoGroup();
    }

    private void OnChannelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_refreshing || !_active || _project != DocManager.Inst.Project || sender is not MixerChannelViewModel channel) return;
        if (channel.Track != null && !_project.tracks.Contains(channel.Track)) return;
        MixCommand? command = e.PropertyName switch
        {
            nameof(channel.Volume) when double.IsFinite(channel.Volume) => new ChangeMixVolumeCommand(_project, channel.Track, channel.Volume),
            nameof(channel.Pan) when double.IsFinite(channel.Pan) => new ChangeMixPanCommand(_project, channel.Track, channel.Pan),
            nameof(channel.Mute) => new ChangeMixMuteCommand(_project, channel.Track, channel.Mute),
            nameof(channel.Solo) when channel.Track != null => new ChangeMixSoloCommand(_project, channel.Track, channel.Solo),
            nameof(channel.FxEnabled) => ChangeFx(channel, fx => fx.Enabled = channel.FxEnabled),
            nameof(channel.LowDb) when double.IsFinite(channel.LowDb) => ChangeFx(channel, fx => fx.EqLowDb = channel.LowDb),
            nameof(channel.MidDb) when double.IsFinite(channel.MidDb) => ChangeFx(channel, fx => fx.EqMidDb = channel.MidDb),
            nameof(channel.HighDb) when double.IsFinite(channel.HighDb) => ChangeFx(channel, fx => fx.EqHighDb = channel.HighDb),
            nameof(channel.ThresholdDb) when double.IsFinite(channel.ThresholdDb) => ChangeFx(channel, fx => fx.CompThresholdDb = channel.ThresholdDb),
            nameof(channel.Ratio) when double.IsFinite(channel.Ratio) => ChangeFx(channel, fx => fx.CompRatio = channel.Ratio),
            nameof(channel.ReverbWet) when double.IsFinite(channel.ReverbWet) => ChangeFx(channel, fx => fx.ReverbWet = channel.ReverbWet),
            nameof(channel.ReverbSize) when double.IsFinite(channel.ReverbSize) => ChangeFx(channel, fx => fx.ReverbSize = channel.ReverbSize),
            _ => null
        };
        if (command == null || !command.HasChanges) return;
        bool singleEdit = !DocManager.Inst.HasOpenUndoGroup;
        if (singleEdit) DocManager.Inst.StartUndoGroup("调整混音参数");
        try { DocManager.Inst.ExecuteCmd(command); }
        finally { if (singleEdit) DocManager.Inst.EndUndoGroup(); }
    }

    private ChangeMixFxCommand ChangeFx(MixerChannelViewModel channel, Action<UMixFx> change)
    {
        UMixFx fx = (channel.Track == null ? _project!.MasterFx : channel.Track.MixFx)?.Clone() ?? new UMixFx();
        change(fx);
        return new ChangeMixFxCommand(_project!, channel.Track, fx);
    }

    public void RefreshParameters()
    {
        if (_project == null) return;
        _refreshing = true;
        try
        {
            Master.RefreshParameters(_project);
            foreach (MixerChannelViewModel channel in Channels) channel.RefreshParameters(_project);
        }
        finally { _refreshing = false; }
    }

    public void Refresh(UProject project)
    {
        if (_project != project) EndEdit();
        _project = project;
        UTrack? selected = SelectedChannel?.Track;
        bool masterSelected = SelectedChannel == Master;
        var previous = Channels.ToDictionary(channel => channel.Track!);
        foreach (MixerChannelViewModel channel in Channels) channel.PropertyChanged -= OnChannelChanged;
        Channels.Clear();
        FxChannels.Clear();
        FxChannels.Add(Master);
        foreach (UTrack track in project.tracks)
        {
            MixerChannelViewModel channel = previous.TryGetValue(track, out var existing)
                ? existing : new MixerChannelViewModel(track);
            channel.RefreshIdentity();
            channel.PropertyChanged += OnChannelChanged;
            Channels.Add(channel);
            FxChannels.Add(channel);
        }
        SelectedChannel = masterSelected ? Master : Channels.FirstOrDefault(channel => channel.Track == selected) ?? Channels.FirstOrDefault();
        if (SelectedChannel == null) IsDetailOpen = false;
        this.RaisePropertyChanged(nameof(IsEmpty));
        RefreshParameters();
    }
}

public class MixerChannelViewModel : ViewModelBase
{
    public UTrack? Track { get; }
    public bool IsMaster => Track == null;
    public UMixFx ResetDefaults { get; } = new();
    [Reactive] public string Name { get; set; } = string.Empty;
    [Reactive] public IBrush Color { get; set; } = Brushes.Transparent;
    [Reactive] public double Volume { get; set; }
    [Reactive] public double LeftDb { get; set; } = double.NegativeInfinity;
    [Reactive] public double RightDb { get; set; } = double.NegativeInfinity;
    private double _pan;
    public double Pan
    {
        get => _pan;
        set
        {
            this.RaiseAndSetIfChanged(ref _pan, value);
            this.RaisePropertyChanged(nameof(PanText));
        }
    }
    public string PanText => Math.Abs(Pan) < 0.5 ? "C"
        : (Pan < 0 ? "L" : "R") + Math.Abs(Pan).ToString("0", CultureInfo.InvariantCulture);
    [Reactive] public bool Mute { get; set; }
    [Reactive] public bool Solo { get; set; }
    [Reactive] public bool FxEnabled { get; set; }
    [Reactive] public double LowDb { get; set; }
    [Reactive] public double MidDb { get; set; }
    [Reactive] public double HighDb { get; set; }
    [Reactive] public double ThresholdDb { get; set; }
    [Reactive] public double Ratio { get; set; }
    [Reactive] public double ReverbWet { get; set; }
    [Reactive] public double ReverbSize { get; set; }

    public MixerChannelViewModel(UTrack? track)
    {
        Track = track;
        RefreshIdentity();
        Volume = track?.Volume ?? 0;
        Pan = track?.Pan ?? 0;
        Mute = track?.Mute ?? false;
        Solo = track?.Solo ?? false;
        UMixFx fx = track?.MixFx ?? new UMixFx();
        FxEnabled = fx.Enabled;
        LowDb = fx.EqLowDb;
        MidDb = fx.EqMidDb;
        HighDb = fx.EqHighDb;
        ThresholdDb = fx.CompThresholdDb;
        Ratio = fx.CompRatio;
        ReverbWet = fx.ReverbWet;
        ReverbSize = fx.ReverbSize;
    }

    public void RefreshParameters(UProject project)
    {
        Volume = Track?.Volume ?? project.MasterVolume;
        Pan = Track?.Pan ?? project.MasterPan;
        Mute = Track?.Mute ?? project.MasterMute;
        Solo = Track?.Solo ?? false;
        UMixFx fx = (Track == null ? project.MasterFx : Track.MixFx) ?? new UMixFx();
        FxEnabled = fx.Enabled;
        LowDb = fx.EqLowDb; MidDb = fx.EqMidDb; HighDb = fx.EqHighDb;
        ThresholdDb = fx.CompThresholdDb; Ratio = fx.CompRatio;
        ReverbWet = fx.ReverbWet; ReverbSize = fx.ReverbSize;
    }

    public void RefreshIdentity()
    {
        if (Track == null)
        {
            Name = "Master";
            return;
        }
        Name = Track.TrackName;
        Color = TrackPalette.GetTrackColor(Track.TrackColor).AccentColor;
    }
}
