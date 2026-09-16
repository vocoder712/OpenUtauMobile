using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using OpenUtau.Core.Util;
using OpenUtauMobile.Helpers;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtauMobile.ViewModels;

/// <summary>预设列表也是草稿；保存、删除在整个属性弹窗确认时写入。</summary>
public sealed class NotePropertyPresets : ReactiveObject, IDisposable
{
    private readonly Func<string, object> create;
    private readonly Action<object> select;
    private readonly Action<IReadOnlyList<object>> commit;
    private object? selected;
    private bool modified;
    public ObservableCollection<object> Items { get; }
    [Reactive] public string Name { get; set; } = string.Empty;
    [Reactive] public string Error { get; private set; } = string.Empty;
    public ReactiveCommand<Unit, Unit> SaveCommand { get; }
    public ReactiveCommand<Unit, Unit> RemoveCommand { get; }
    public object? Selected
    {
        get => selected;
        set
        {
            if (selected == value)
            {
                return;
            }
            this.RaiseAndSetIfChanged(ref selected, value);
            if (value != null)
            {
                select(value);
            }
        }
    }

    public NotePropertyPresets(IEnumerable<object> items, Func<string, object> create,
        Action<object> select, Action<IReadOnlyList<object>> commit)
    {
        Items = new ObservableCollection<object>(items);
        this.create = create;
        this.select = select;
        this.commit = commit;
        SaveCommand = ReactiveCommand.Create(Save);
        RemoveCommand = ReactiveCommand.Create(Remove, this.WhenAnyValue(vm => vm.Selected).Select(value => value != null));
    }

    private void Save()
    {
        string name = Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            Error = L.S("NoteProperties.PresetNameRequired");
            return;
        }
        try
        {
            object preset = create(name);
            Items.Add(preset);
            // 保存当前草稿不再次套用预设，以免影响多选中的其他音符。
            this.RaiseAndSetIfChanged(ref selected, preset, nameof(Selected));
            modified = true;
            Name = string.Empty;
            Error = string.Empty;
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            Error = L.S("NoteProperties.Invalid");
        }
    }

    private void Remove()
    {
        if (Selected != null && Items.Remove(Selected))
        {
            Selected = null;
            modified = true;
        }
    }

    public bool Commit()
    {
        if (!modified)
        {
            return false;
        }
        commit(Items.ToArray());
        return true;
    }

    public void Dispose()
    {
        SaveCommand.Dispose();
        RemoveCommand.Dispose();
    }
}

public sealed class NoteVibratoOptions : ReactiveObject
{
    private readonly NotePropertyField length;
    private bool? enabled;
    [Reactive] public bool AutoEnabled { get; set; }
    public NotePropertyField Duration { get; }
    public decimal? MinimumDuration
    {
        get => Duration.Number;
        set => Duration.Number = value;
    }
    public bool? Enabled
    {
        get => enabled;
        set
        {
            if (enabled == value)
            {
                return;
            }
            this.RaiseAndSetIfChanged(ref enabled, value);
            if (value.HasValue)
            {
                length.SetNumber(value.Value ? (decimal)NotePresets.Default.DefaultVibrato.VibratoLength : 0);
            }
        }
    }

    public NoteVibratoOptions(NotePropertyField length)
    {
        this.length = length;
        enabled = length.Number.HasValue ? length.Number > 0 : null;
        AutoEnabled = NotePresets.Default.AutoVibratoToggle;
        Duration = new NotePropertyField(L.S("NoteProperties.AutoDuration"), 10, 1920,
            [(decimal)NotePresets.Default.AutoVibratoNoteDuration], 1)
        {
            RequireInteger = true
        };
        length.NumberChanged = () =>
            this.RaiseAndSetIfChanged(ref enabled, length.Number.HasValue ? length.Number > 0 : null, nameof(Enabled));
    }

    public bool IsValid => !AutoEnabled || Duration.IsValid && MinimumDuration is >= 10 and <= 1920
        && MinimumDuration == decimal.Truncate(MinimumDuration.Value);
}
