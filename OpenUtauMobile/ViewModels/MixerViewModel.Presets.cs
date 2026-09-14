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

namespace OpenUtauMobile.ViewModels;

public partial class MixerViewModel
{
    public ObservableCollection<Preferences.MixFxUserPreset> UserPresets { get; } = [];
    private Preferences.MixFxUserPreset? _defaultPreset;
    private bool _changingLibrary;
    private Preferences.MixFxUserPreset? _selectedUserPreset;
    public Preferences.MixFxUserPreset? SelectedUserPreset
    {
        get => _selectedUserPreset;
        set
        {
            if (value == null) return;
            if (_selectedUserPreset == value) return;
            this.RaiseAndSetIfChanged(ref _selectedUserPreset, value);
            this.RaisePropertyChanged(nameof(HasUserPreset));
            if (!_changingLibrary && value != null) ApplyPresetSnapshot(value.Fx.Clone());
        }
    }
    public bool HasUserPreset => SelectedUserPreset != null && SelectedUserPreset != _defaultPreset;

    private void LoadUserPresets()
    {
        _defaultPreset = new Preferences.MixFxUserPreset
        {
            Name = L.S("Mixer.PresetDefault"),
            Fx = new UMixFx { Enabled = true, ReverbPreDelayMs = FxPresets.Reverb["small_room"].PreDelayMs }
        };
        UserPresets.Add(_defaultPreset);
        foreach (Preferences.MixFxUserPreset preset in Preferences.Default.MixFxUserPresets ?? [])
        {
            if (preset?.Fx != null) UserPresets.Add(new Preferences.MixFxUserPreset { Name = preset.Name, Fx = preset.Fx.Clone() });
        }
        SelectDefaultPreset();
    }

    private void SelectDefaultPreset()
    {
        // 恢复预设操作目标时不应用快照，避免打开面板或切换轨道覆盖已有参数。
        _changingLibrary = true;
        try { SelectedUserPreset = _defaultPreset; }
        finally { _changingLibrary = false; }
    }

    public void ApplyEffectPreset(string effect, string key)
    {
        MixerChannelViewModel? channel = SelectedChannel;
        if (channel?.Track == null) return;
        UMixFx fx = channel.Track.MixFx?.Clone() ?? new UMixFx();
        // 参数和预设键作为一个快照提交；压缩时序、混响干湿比例等隐含参数仍由 Core 的预设表决定。
        if (effect == "Eq" && FxPresets.Eq.ContainsKey(key))
        {
            fx.EqPreset = key;
            FxPresets.EqParams preset = FxPresets.Eq[fx.EqPreset];
            fx.EqLowDb = preset.LowDb; fx.EqMidFreq = preset.MidFreq;
            fx.EqMidDb = preset.MidDb; fx.EqHighDb = preset.HighDb;
        }
        else if (effect == "Comp" && FxPresets.Comp.ContainsKey(key))
        {
            fx.CompPreset = key;
            FxPresets.CompParams preset = FxPresets.Comp[fx.CompPreset];
            fx.CompThresholdDb = preset.ThresholdDb; fx.CompRatio = preset.Ratio; fx.CompMakeupDb = preset.MakeupDb;
        }
        else if (effect == "Reverb" && FxPresets.Reverb.ContainsKey(key))
        {
            fx.ReverbPreset = key;
            FxPresets.ReverbParams preset = FxPresets.Reverb[fx.ReverbPreset];
            fx.ReverbSize = preset.RoomSize; fx.ReverbDamp = preset.Damp;
            fx.ReverbWet = 1; fx.ReverbPreDelayMs = preset.PreDelayMs;
        }
        else return;
        ApplyPresetSnapshot(fx);
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

    public string? ValidatePresetName(string value)
    {
        string name = value.Trim();
        if (name.Length == 0) return L.S("Mixer.PresetNameRequired");
        return UserPresets.Any(preset => preset.Name == name) ? L.S("Mixer.PresetNameExists") : null;
    }

    public void AddUserPreset(string name)
    {
        if (ValidatePresetName(name) is string error) { ToastService.Enqueue(error); return; }
        UTrack? track = SelectedChannel?.Track;
        if (track == null || _project != DocManager.Inst.Project || !_project.tracks.Contains(track)) return;
        Preferences.MixFxUserPreset snapshot = new() { Name = name.Trim(), Fx = track.MixFx?.Clone() ?? new UMixFx() };
        _changingLibrary = true;
        try { UserPresets.Add(snapshot); SelectedUserPreset = snapshot; }
        finally { _changingLibrary = false; }
        PersistUserPresets();
        ToastService.Enqueue(string.Format(L.S("Mixer.PresetSaved"), snapshot.Name));
    }

    public void UpdateUserPreset()
    {
        UTrack? track = SelectedChannel?.Track;
        if (!HasUserPreset || track == null || _project != DocManager.Inst.Project || !_project.tracks.Contains(track)) return;
        Preferences.MixFxUserPreset snapshot = new() { Name = SelectedUserPreset!.Name, Fx = track.MixFx?.Clone() ?? new UMixFx() };
        _changingLibrary = true;
        try { UserPresets[UserPresets.IndexOf(SelectedUserPreset)] = snapshot; SelectedUserPreset = snapshot; }
        finally { _changingLibrary = false; }
        PersistUserPresets();
        ToastService.Enqueue(string.Format(L.S("Mixer.PresetSaved"), snapshot.Name));
    }

    public void DeleteUserPreset()
    {
        if (!HasUserPreset) return;
        _changingLibrary = true;
        try { UserPresets.Remove(SelectedUserPreset!); SelectedUserPreset = _defaultPreset; }
        finally { _changingLibrary = false; }
        PersistUserPresets();
    }

    private void PersistUserPresets()
    {
        Preferences.Default.MixFxUserPresets = UserPresets.Where(preset => preset != _defaultPreset).Select(preset =>
            new Preferences.MixFxUserPreset { Name = preset.Name, Fx = preset.Fx.Clone() }).ToList();
        Preferences.Save();
    }
}
