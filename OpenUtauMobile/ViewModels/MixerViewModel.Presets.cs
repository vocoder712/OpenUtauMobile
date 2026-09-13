using System;
using System.Collections.ObjectModel;
using System.Linq;
using OpenUtau.Core;
using OpenUtau.Core.SignalChain.Effects;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtauMobile.ViewModels;

public partial class MixerViewModel
{
    public ObservableCollection<Preferences.MixFxUserPreset> UserPresets { get; } = [];
    [Reactive] public string PresetName { get; set; } = string.Empty;
    private Preferences.MixFxUserPreset? _selectedUserPreset;
    public Preferences.MixFxUserPreset? SelectedUserPreset
    {
        get => _selectedUserPreset;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedUserPreset, value);
            this.RaisePropertyChanged(nameof(HasUserPreset));
            if (value != null) PresetName = value.Name;
        }
    }
    public bool HasUserPreset => SelectedUserPreset != null;

    private void LoadUserPresets()
    {
        foreach (Preferences.MixFxUserPreset preset in Preferences.Default.MixFxUserPresets ?? [])
        {
            if (preset?.Fx != null) UserPresets.Add(new Preferences.MixFxUserPreset { Name = preset.Name, Fx = preset.Fx.Clone() });
        }
    }

    public void ApplyEffectPreset(string effect)
    {
        MixerChannelViewModel? channel = SelectedChannel;
        if (channel?.Track == null) return;
        UMixFx fx = channel.Track.MixFx?.Clone() ?? new UMixFx();
        // 参数和预设键作为一个快照提交；压缩时序、混响干湿比例等隐含参数仍由 Core 的预设表决定。
        if (effect == "Eq" && channel.EqPresetIndex >= 0 && channel.EqPresetIndex < FxPresets.EqPresetNames.Length)
        {
            fx.EqPreset = FxPresets.EqPresetNames[channel.EqPresetIndex];
            FxPresets.EqParams preset = FxPresets.Eq[fx.EqPreset];
            fx.EqLowDb = preset.LowDb; fx.EqMidFreq = preset.MidFreq;
            fx.EqMidDb = preset.MidDb; fx.EqHighDb = preset.HighDb;
        }
        else if (effect == "Comp" && channel.CompPresetIndex >= 0 && channel.CompPresetIndex < FxPresets.CompPresetNames.Length)
        {
            fx.CompPreset = FxPresets.CompPresetNames[channel.CompPresetIndex];
            FxPresets.CompParams preset = FxPresets.Comp[fx.CompPreset];
            fx.CompThresholdDb = preset.ThresholdDb; fx.CompRatio = preset.Ratio; fx.CompMakeupDb = preset.MakeupDb;
        }
        else if (effect == "Reverb" && channel.ReverbPresetIndex >= 0 && channel.ReverbPresetIndex < FxPresets.ReverbPresetNames.Length)
        {
            fx.ReverbPreset = FxPresets.ReverbPresetNames[channel.ReverbPresetIndex];
            FxPresets.ReverbParams preset = FxPresets.Reverb[fx.ReverbPreset];
            fx.ReverbSize = preset.RoomSize; fx.ReverbDamp = preset.Damp;
            fx.ReverbWet = 1; fx.ReverbPreDelayMs = preset.PreDelayMs;
        }
        else return;
        ApplyPresetSnapshot(fx);
    }

    public void ApplyDefaultPreset()
    {
        // 与桌面的推荐整链一致；UMixFx 的零预延迟默认值为旧工程兼容值。
        ApplyPresetSnapshot(new UMixFx { Enabled = true, ReverbPreDelayMs = FxPresets.Reverb["small_room"].PreDelayMs });
    }

    public void ApplyUserPreset()
    {
        if (SelectedUserPreset != null) ApplyPresetSnapshot(SelectedUserPreset.Fx.Clone());
    }

    private void ApplyPresetSnapshot(UMixFx fx)
    {
        UTrack? track = SelectedChannel?.Track;
        if (!_active || _project != DocManager.Inst.Project || track == null || !_project.tracks.Contains(track)) return;
        EndEdit();
        if (DocManager.Inst.HasOpenUndoGroup) return;
        DocManager.Inst.StartUndoGroup("Mixer.ApplyPresetUndo");
        try { DocManager.Inst.ExecuteCmd(new ChangeMixFxCommand(_project, track, fx)); }
        finally { DocManager.Inst.EndUndoGroup(); }
        RefreshParameters();
    }

    public void SaveUserPreset()
    {
        UTrack? track = SelectedChannel?.Track;
        if (track == null || _project != DocManager.Inst.Project || !_project.tracks.Contains(track)) return;
        string name = (PresetName ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            ToastService.Enqueue(L.S("Mixer.PresetNameRequired"));
            return;
        }
        Preferences.MixFxUserPreset snapshot = new() { Name = name, Fx = track.MixFx?.Clone() ?? new UMixFx() };
        Preferences.MixFxUserPreset? previous = UserPresets.FirstOrDefault(preset => preset.Name == name);
        if (previous == null) UserPresets.Add(snapshot);
        else UserPresets[UserPresets.IndexOf(previous)] = snapshot;
        SelectedUserPreset = snapshot;
        PersistUserPresets();
        ToastService.Enqueue(string.Format(L.S("Mixer.PresetSaved"), name));
    }

    public void DeleteUserPreset()
    {
        if (SelectedUserPreset == null) return;
        UserPresets.Remove(SelectedUserPreset);
        SelectedUserPreset = null;
        PersistUserPresets();
    }

    private void PersistUserPresets()
    {
        Preferences.Default.MixFxUserPresets = UserPresets.Select(preset =>
            new Preferences.MixFxUserPreset { Name = preset.Name, Fx = preset.Fx.Clone() }).ToList();
        Preferences.Save();
    }
}
