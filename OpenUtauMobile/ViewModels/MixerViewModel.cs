using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Avalonia.Media;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Themes.OpenUtauMobile.Runtime;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtauMobile.ViewModels;

/// <summary>混音布局原型的会话状态，尚不向工程或音频线程提交参数。</summary>
public class MixerViewModel : ViewModelBase
{
    public ObservableCollection<MixerChannelViewModel> Channels { get; } = [];
    [Reactive] public MixerChannelViewModel? SelectedChannel { get; set; }
    [Reactive] public bool IsDetailOpen { get; set; }
    [Reactive] public bool IsWide { get; set; }
    [Reactive] public double ChannelHeight { get; set; } = 360;
    public bool IsEmpty => Channels.Count == 0;

    public void Refresh(UProject project)
    {
        UTrack? selected = SelectedChannel?.Track;
        var previous = Channels.ToDictionary(channel => channel.Track);
        Channels.Clear();
        foreach (UTrack track in project.tracks)
        {
            MixerChannelViewModel channel = previous.TryGetValue(track, out var existing)
                ? existing : new MixerChannelViewModel(track);
            channel.RefreshIdentity();
            Channels.Add(channel);
        }
        SelectedChannel = Channels.FirstOrDefault(channel => channel.Track == selected) ?? Channels.FirstOrDefault();
        if (SelectedChannel == null) IsDetailOpen = false;
        this.RaisePropertyChanged(nameof(IsEmpty));
    }
}

public class MixerChannelViewModel : ViewModelBase
{
    public UTrack Track { get; }
    public UMixFx ResetDefaults { get; } = new();
    [Reactive] public string Name { get; set; } = string.Empty;
    [Reactive] public IBrush Color { get; set; } = Brushes.Transparent;
    [Reactive] public double Volume { get; set; }
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

    public MixerChannelViewModel(UTrack track)
    {
        Track = track;
        RefreshIdentity();
        Volume = track.Volume;
        Pan = track.Pan;
        Mute = track.Mute;
        Solo = track.Solo;
        UMixFx fx = track.MixFx ?? new UMixFx();
        FxEnabled = fx.Enabled;
        LowDb = fx.EqLowDb;
        MidDb = fx.EqMidDb;
        HighDb = fx.EqHighDb;
        ThresholdDb = fx.CompThresholdDb;
        Ratio = fx.CompRatio;
        ReverbWet = fx.ReverbWet;
        ReverbSize = fx.ReverbSize;
    }

    public void RefreshIdentity()
    {
        Name = Track.TrackName;
        Color = TrackPalette.GetTrackColor(Track.TrackColor).AccentColor;
    }
}
