using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reactive;
using Avalonia;
using OpenUtau.Api;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using OpenUtauMobile.Helpers;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtauMobile.ViewModels;

/// <summary>以草稿编辑选中音符，确认后统一提交命令。</summary>
public sealed class NotePropertiesViewModel : PopupViewModelBase, IDisposable
{
    private readonly UProject project;
    private readonly UVoicePart part;
    private readonly UNote[] notes;
    private readonly List<Action<List<UCommand>>> edits = [];
    private NotePropertyField? tone;
    public List<NotePropertyGroup> Groups { get; } = [];
    public string Summary { get; }
    [Reactive] public int SelectedTabIndex { get; set; }
    [Reactive] public string Error { get; private set; } = string.Empty;
    public ReactiveCommand<Unit, Unit> ApplyCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    public NotePropertiesViewModel(UVoicePart part, IReadOnlyCollection<UNote> selectedNotes)
    {
        project = DocManager.Inst.Project;
        this.part = part;
        notes = part.notes.Where(selectedNotes.Contains).ToArray();
        Summary = string.Format(L.S("NoteProperties.Summary"), notes.Length);
        ApplyCommand = ReactiveCommand.Create(Apply);
        CancelCommand = ReactiveCommand.Create(() => RaiseClose(null));
        if (notes.Length == 0)
        {
            return;
        }

        NotePropertyGroup basic = AddGroup("Basic");
        NotePropertyField lyric = new(L.S("NoteProperties.Lyric"), notes.Select(n => n.lyric));
        basic.Fields.Add(lyric);
        edits.Add(commands =>
        {
            if (lyric.IsEdited && !string.IsNullOrWhiteSpace(lyric.Text))
            {
                commands.AddRange(notes.Where(n => n.lyric != lyric.Text).Select(n => new ChangeNoteLyricCommand(part, n, lyric.Text!)));
            }
        });
        tone = new NotePropertyField(L.S("NoteProperties.Tone"), notes.Select(n => MusicMath.GetToneName(n.tone)))
        {
            Hint = L.S("NoteProperties.ToneHint")
        };
        basic.Fields.Add(tone);
        edits.Add(commands =>
        {
            if (tone.IsEdited && TryGetTones(out int[] tones))
            {
                for (int i = 0; i < notes.Length; i++)
                {
                    if (tones[i] != notes[i].tone)
                    {
                        commands.Add(new MoveNoteCommand(part, notes[i], 0, tones[i] - notes[i].tone));
                    }
                }
            }
        });
        AddNumber(basic, "Tuning", -100, 100, n => n.tuning, (n, v) => new ChangeNoteTuningCommand(part, n, (int)v), 1);

        PhonemizerFactory[] factories = PhonemizerFactory.GetAll();
        List<string?> ids = [null, .. factories.Select(f => f.type.FullName)];
        UTrack currentTrack = project.tracks[part.trackNo];
        List<string> names = [$"{L.S("NoteProperties.TrackDefault")} ({currentTrack.Phonemizer.GetType().Name})", .. factories.Select(f => f.ToString())];
        foreach (string id in notes.Select(n => n.PhonemizerOverride).OfType<string>().Where(id => !ids.Contains(id)).Distinct())
        {
            ids.Add(id);
            names.Add(id);
        }
        NotePropertyField phonemizer = AddChoice(basic, "Phonemizer", names.ToArray(), notes.Select(n => ids.IndexOf(n.PhonemizerOverride)));
        string DraftLyric(UNote note) => lyric.IsEdited ? lyric.Text ?? note.lyric : note.lyric;
        phonemizer.Hint = L.S("NoteProperties.PhonemizerHint");
        phonemizer.IsEnabled = notes.Any(n => !n.lyric.StartsWith("+", StringComparison.Ordinal));
        lyric.TextChanged = () => phonemizer.IsEnabled = notes.Any(n => !DraftLyric(n).StartsWith("+", StringComparison.Ordinal));
        edits.Add(commands =>
        {
            if (phonemizer.IsEdited && phonemizer.SelectedIndex >= 0)
            {
                commands.AddRange(notes.Where(n => !DraftLyric(n).StartsWith("+", StringComparison.Ordinal) && n.PhonemizerOverride != ids[phonemizer.SelectedIndex])
                    .Select(n => new ChangeNotePhonemizerCommand(part, n, ids[phonemizer.SelectedIndex])));
            }
        });

        NotePropertyGroup portamento = AddGroup("Portamento");
        NotePropertyField length = new(L.S("NoteProperties.PortamentoLength"), 2, 320,
            notes.Select(n => (decimal)(n.pitch.data.Count < 2 ? NotePresets.Default.DefaultPortamento.PortamentoLength : n.pitch.data[^1].X - n.pitch.data[0].X)), 1)
        {
            Minimum = 0.001m,
            Maximum = decimal.MaxValue
        };
        NotePropertyField start = new(L.S("NoteProperties.PortamentoStart"), -200, 200,
            notes.Select(n => (decimal)(n.pitch.data.FirstOrDefault()?.X ?? NotePresets.Default.DefaultPortamento.PortamentoStart)), 1)
        {
            Minimum = decimal.MinValue,
            Maximum = decimal.MaxValue
        };
        NotePropertyField shape = AddChoice(portamento, "Shape",
            [L.S("NoteProperties.EaseInOut"), L.S("NoteProperties.Linear"), L.S("NoteProperties.EaseIn"), L.S("NoteProperties.EaseOut"), L.S("NoteProperties.Smooth")],
            notes.SelectMany(n => n.pitch.data.Select(p => (int)p.shape)));
        portamento.Fields.InsertRange(0, [length, start]);
        UPitch? presetPitch = null;
        UPitch DraftPitch(UNote note)
        {
            UPitch pitch = note.pitch.Clone();
            if (presetPitch != null)
            {
                pitch.data = presetPitch.data.Select(p => p.Clone()).ToList();
            }
            if (pitch.data.Count < 2)
            {
                return pitch;
            }
            float oldStart = pitch.data[0].X;
            float oldLength = pitch.data[^1].X - oldStart;
            float referenceStart = presetPitch?.data[0].X
                ?? notes[0].pitch.data.FirstOrDefault()?.X ?? NotePresets.Default.DefaultPortamento.PortamentoStart;
            float newStart = start.IsEdited && start.Number.HasValue ? oldStart + (float)start.Number.Value - referenceStart : oldStart;
            float scale = length.IsEdited && length.Number.HasValue && oldLength > 0 ? (float)length.Number.Value / oldLength : 1;
            foreach (PitchPoint point in pitch.data)
            {
                point.X = newStart + (point.X - oldStart) * scale;
                if (shape.IsEdited && shape.SelectedIndex >= 0)
                {
                    point.shape = (PitchPointShape)shape.SelectedIndex;
                }
            }
            return pitch;
        }
        portamento.Presets = new NotePropertyPresets(NotePresets.Default.PortamentoPresets,
            name =>
            {
                length.CommitInput();
                start.CommitInput();
                if (!length.IsValid || !start.IsValid)
                {
                    throw new ArgumentException();
                }
                UPitch pitch = DraftPitch(notes[0]);
                if (pitch.data.Count > 2)
                {
                    return new NotePresets.PortamentoPreset(name, pitch.data.Select(p => p.Clone()).ToList());
                }
                float presetStart = pitch.data.FirstOrDefault()?.X ?? NotePresets.Default.DefaultPortamento.PortamentoStart;
                float presetLength = pitch.data.Count >= 2 ? pitch.data[^1].X - presetStart : NotePresets.Default.DefaultPortamento.PortamentoLength;
                return new NotePresets.PortamentoPreset(name, checked((int)presetLength), checked((int)presetStart));
            },
            value =>
            {
                NotePresets.PortamentoPreset preset = (NotePresets.PortamentoPreset)value;
                presetPitch = preset.PitchPoints.Count >= 2 ? new UPitch { data = preset.PitchPoints.Select(p => p.Clone()).ToList() } : null;
                if (presetPitch != null)
                {
                    shape.LoadChoice(presetPitch.data.Select(p => (int)p.shape));
                }
                length.SetNumber(presetPitch != null ? (decimal)(presetPitch.data[^1].X - presetPitch.data[0].X) : preset.PortamentoLength);
                start.SetNumber(presetPitch != null ? (decimal)presetPitch.data[0].X : preset.PortamentoStart);
            },
            items => NotePresets.Default.PortamentoPresets = items.Cast<NotePresets.PortamentoPreset>().ToList());
        edits.Add(commands =>
        {
            if (length.IsEdited || start.IsEdited || shape.IsEdited || presetPitch != null)
            {
                commands.AddRange(notes.Select(note => new SetPitchPointsCommand(part, note, DraftPitch(note))));
            }
        });

        NotePropertyGroup vibrato = AddGroup("Vibrato");
        // 使用完整颤音命令，保留淡入淡出联动值的撤销结果。
        List<(NotePropertyField Field, Action<UVibrato, float> Set)> vibratoFields = [];
        void AddVibrato(string key, decimal min, decimal max, Func<UNote, float> get, Action<UVibrato, float> set)
        {
            NotePropertyField field = new(L.S("NoteProperties." + key), min, max, notes.Select(n => (decimal)get(n)));
            vibrato.Fields.Add(field);
            vibratoFields.Add((field, set));
        }
        AddVibrato("VibratoLength", 0, 100, n => n.vibrato.length, (v, x) => v.length = x);
        AddVibrato("Period", 5, 500, n => n.vibrato.period, (v, x) => v.period = x);
        AddVibrato("Depth", 5, 200, n => n.vibrato.depth, (v, x) => v.depth = x);
        AddVibrato("FadeIn", 0, 100, n => n.vibrato.@in, (v, x) => v.@in = x);
        AddVibrato("FadeOut", 0, 100, n => n.vibrato.@out, (v, x) => v.@out = x);
        AddVibrato("Shift", 0, 100, n => n.vibrato.shift, (v, x) => v.shift = x);
        AddVibrato("Drift", -100, 100, n => n.vibrato.drift, (v, x) => v.drift = x);
        AddVibrato("VolumeLink", -100, 100, n => n.vibrato.volLink, (v, x) => v.volLink = x);
        NotePropertyField fadeIn = vibratoFields[3].Field;
        NotePropertyField fadeOut = vibratoFields[4].Field;
        fadeIn.NumberChanged = () =>
        {
            if (fadeIn.Number + fadeOut.Number > 100)
            {
                fadeOut.SetNumber(100 - fadeIn.Number!.Value);
            }
        };
        fadeOut.NumberChanged = () =>
        {
            if (fadeIn.Number + fadeOut.Number > 100)
            {
                fadeIn.SetNumber(100 - fadeOut.Number!.Value);
            }
        };
        vibrato.Vibrato = new NoteVibratoOptions(vibratoFields[0].Field);
        UVibrato DraftVibrato(UNote note)
        {
            UVibrato value = note.vibrato.Clone();
            foreach ((NotePropertyField field, Action<UVibrato, float> set) in vibratoFields)
            {
                if (field.IsEdited && field.Number.HasValue)
                {
                    set(value, (float)field.Number.Value);
                }
            }
            return value;
        }
        vibrato.Presets = new NotePropertyPresets(NotePresets.Default.VibratoPresets,
            name =>
            {
                foreach ((NotePropertyField field, Action<UVibrato, float> _) in vibratoFields)
                {
                    field.CommitInput();
                }
                if (vibratoFields.Any(f => !f.Field.IsValid))
                {
                    throw new ArgumentException();
                }
                UVibrato value = DraftVibrato(notes[0]);
                return new NotePresets.VibratoPreset(name, value.length, value.period, value.depth,
                    value.@in, value.@out, value.shift, value.drift, value.volLink);
            },
            value =>
            {
                NotePresets.VibratoPreset preset = (NotePresets.VibratoPreset)value;
                float[] values = [preset.VibratoLength, preset.VibratoPeriod, preset.VibratoDepth, preset.VibratoIn, preset.VibratoOut, preset.VibratoShift, preset.VibratoDrift, preset.VibratoVolLink];
                for (int i = 0; i < values.Length; i++)
                {
                    NotePropertyField field = vibratoFields[i].Field;
                    field.SetNumber(Math.Clamp((decimal)values[i], field.Minimum, field.Maximum));
                }
            },
            items => NotePresets.Default.VibratoPresets = items.Cast<NotePresets.VibratoPreset>().ToList());
        edits.Add(commands =>
        {
            if (!vibratoFields.Any(f => f.Field.IsEdited && f.Field.Number.HasValue))
            {
                return;
            }
            foreach (UNote note in notes)
            {
                UVibrato value = DraftVibrato(note);
                // 与桌面一致：首个音符始终应用长度，其余短音符由自动颤音条件过滤。
                if (vibratoFields[0].Field.IsEdited && note != notes[0] && vibrato.Vibrato.AutoEnabled
                    && note.duration < vibrato.Vibrato.MinimumDuration)
                {
                    value.length = 0;
                }
                commands.Add(new SetVibratoCommand(part, note, value));
            }
        });

        NotePropertyGroup expressions = AddGroup("Expressions");
        UTrack track = project.tracks[part.trackNo];
        foreach (UExpressionDescriptor descriptor in track.GetSupportedExps(project).Where(d => d.type != UExpressionType.Curve))
        {
            UExpressionDescriptor effective = descriptor.abbr == OpenUtau.Core.Format.Ustx.CLR && track.VoiceColorExp?.options.Length > 0 ? track.VoiceColorExp : descriptor;
            decimal[] values = notes.SelectMany(n =>
            {
                List<Tuple<float, bool>> values = n.GetExpression(project, track, descriptor.abbr);
                return values.Count == 0
                    ? n.phonemeExpressions.Where(e => e.abbr == descriptor.abbr).Select(e => (decimal)e.value)
                        .DefaultIfEmpty((decimal)effective.CustomDefaultValue).ToArray()
                    : values.Select(v => (decimal)v.Item1).ToArray();
            }).ToArray();
            NotePropertyField field = effective.type == UExpressionType.Options
                ? new NotePropertyField(effective.ToString(), effective.options, values.Select(v => (int)v))
                : new NotePropertyField(effective.ToString(), (decimal)effective.min, (decimal)effective.max, values, 1);
            field.CanReset = true;
            expressions.Fields.Add(field);
            edits.Add(commands =>
            {
                if (field.ResetRequested)
                {
                    commands.Add(new NotePropertiesExpressionCommand(project, track, part, notes, descriptor.abbr, null));
                }
                else if (field.IsEdited && (field.IsChoice ? field.SelectedIndex >= 0 : field.Number.HasValue))
                {
                    float value = field.IsChoice ? field.SelectedIndex : (float)field.Number!.Value;
                    float defaultValue = field.IsChoice ? effective.defaultValue : effective.CustomDefaultValue;
                    commands.Add(new NotePropertiesExpressionCommand(project, track, part, notes, descriptor.abbr,
                        value == defaultValue ? null : value));
                }
            });
        }
        expressions.EmptyMessage = expressions.Fields.Count == 0 ? L.S("NoteProperties.NoExpressions") : string.Empty;
    }

    private NotePropertyGroup AddGroup(string key)
    {
        NotePropertyGroup group = new(L.S("NoteProperties." + key), L.S("NoteProperties.Tab." + key));
        Groups.Add(group);
        return group;
    }

    private static NotePropertyField AddChoice(NotePropertyGroup group, string key, string[] options, IEnumerable<int> values)
    {
        NotePropertyField field = new(L.S("NoteProperties." + key), options, values);
        group.Fields.Add(field);
        return field;
    }

    private void AddNumber(NotePropertyGroup group, string key, decimal min, decimal max, Func<UNote, float> get, Func<UNote, float, UCommand> command, decimal increment)
    {
        NotePropertyField field = new(L.S("NoteProperties." + key), min, max, notes.Select(n => (decimal)get(n)), increment);
        field.RequireInteger = true;
        group.Fields.Add(field);
        edits.Add(commands =>
        {
            if (field.IsEdited && field.Number.HasValue)
            {
                commands.AddRange(notes.Where(n => get(n) != (float)field.Number.Value).Select(n => command(n, (float)field.Number.Value)));
            }
        });
    }

    private void Apply()
    {
        if (DocManager.Inst.Project != project || !project.parts.Contains(part) || notes.Any(n => !part.notes.Contains(n)))
        {
            Error = L.S("NoteProperties.Stale");
            return;
        }
        foreach (NotePropertyField field in Groups.SelectMany(g => g.Fields))
        {
            field.CommitInput();
        }
        foreach (NotePropertyGroup group in Groups)
        {
            group.Vibrato?.Duration.CommitInput();
        }
        int invalidGroup = Groups.FindIndex(g => g.Fields.Any(f => !f.IsValid) || g.Vibrato is { IsValid: false });
        if (invalidGroup >= 0)
        {
            SelectedTabIndex = invalidGroup;
            Error = L.S("NoteProperties.Invalid");
            return;
        }
        if (tone is { IsEdited: true } && !TryGetTones(out _))
        {
            SelectedTabIndex = 0;
            Error = L.S("NoteProperties.InvalidTone");
            return;
        }
        List<UCommand> commands = [];
        foreach (Action<List<UCommand>> edit in edits)
        {
            edit(commands);
        }
        if (commands.Count > 0)
        {
            DocManager.Inst.StartUndoGroup(deferValidate: true);
            try
            {
                foreach (UCommand command in commands)
                {
                    DocManager.Inst.ExecuteCmd(command);
                }
            }
            finally
            {
                DocManager.Inst.EndUndoGroup();
            }
        }
        bool presetsChanged = false;
        foreach (NotePropertyGroup group in Groups)
        {
            presetsChanged |= group.Presets?.Commit() ?? false;
        }
        if (presetsChanged)
        {
            NotePresets.Save();
        }
        RaiseClose(null);
    }

    private bool TryGetTones(out int[] tones)
    {
        tones = new int[notes.Length];
        string input = tone?.Text?.Trim() ?? string.Empty;
        bool relative = input.StartsWith('+') || input.StartsWith('-');
        if (relative && !int.TryParse(input, out _))
        {
            return false;
        }
        int value = int.TryParse(input, out int number) ? number : MusicMath.NameToTone(input);
        for (int i = 0; i < notes.Length; i++)
        {
            long target = relative ? (long)notes[i].tone + value : value;
            if (target < 0 || target > 127)
            {
                return false;
            }
            tones[i] = (int)target;
        }
        return true;
    }

    public void Dispose()
    {
        ApplyCommand.Dispose();
        CancelCommand.Dispose();
        foreach (NotePropertyGroup group in Groups)
        {
            group.Presets?.Dispose();
            group.Vibrato?.Duration.ResetCommand.Dispose();
        }
        foreach (NotePropertyField field in Groups.SelectMany(g => g.Fields))
        {
            field.ResetCommand.Dispose();
        }
    }
}

public sealed class NotePropertyGroup(string title, string tabTitle) : ReactiveObject
{
    public string Title { get; } = title;
    public string TabTitle { get; } = tabTitle;
    [Reactive] public Vector ScrollOffset { get; set; }
    public List<NotePropertyField> Fields { get; } = [];
    public NotePropertyPresets? Presets { get; set; }
    public NoteVibratoOptions? Vibrato { get; set; }
    public string EmptyMessage { get; set; } = string.Empty;
}

/// <summary>混合值留空；只提交明确修改的字段。</summary>
public sealed class NotePropertyField : ReactiveObject
{
    public string Label { get; }
    public bool IsNumber { get; }
    public bool IsChoice { get; }
    public bool IsText => !IsNumber && !IsChoice;
    public decimal Minimum { get; init; }
    public decimal Maximum { get; init; }
    public double SliderMinimum { get; }
    public double SliderMaximum { get; }
    public bool RequireInteger { get; set; }
    [Reactive] public bool IsEnabled { get; set; } = true;
    public string Hint { get; set; } = string.Empty;
    public Action? NumberChanged { get; set; }
    public Action? TextChanged { get; set; }
    public bool HasInvalidInput => IsNumber && NumberText != null && (NumberText.Length > 0 || Number.HasValue) &&
        (!decimal.TryParse(NumberText, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.CurrentCulture, out decimal value)
            || value < Minimum || value > Maximum);
    public bool IsValid => ResetRequested || !HasInvalidInput && (!IsEdited ||
        (IsNumber ? Number.HasValue && Number >= Minimum && Number <= Maximum
            && (!RequireInteger || Number == decimal.Truncate(Number.Value))
        : IsText ? !string.IsNullOrWhiteSpace(Text) : SelectedIndex >= 0 && SelectedIndex < Options.Length));
    public double SliderValue
    {
        get => Math.Clamp((double)(Number ?? 0), SliderMinimum, SliderMaximum);
        set
        {
            if (!double.IsFinite(value) || value == SliderValue)
            {
                return;
            }
            Number = Math.Round((decimal)value / Increment) * Increment;
        }
    }
    public decimal Increment { get; } = 0.1m;
    public string[] Options { get; } = [];
    public bool CanReset { get; set; }
    public bool IsEdited { get; private set; }
    [Reactive] public bool ResetRequested { get; private set; }
    private decimal? number;
    private string? numberText;
    public string? NumberText
    {
        get => numberText;
        set => this.RaiseAndSetIfChanged(ref numberText, value);
    }
    private string? text;
    private int selectedIndex = -1;
    public ReactiveCommand<Unit, Unit> ResetCommand { get; }
    public decimal? Number
    {
        get => number;
        set
        {
            if (number == value)
            {
                return;
            }
            this.RaiseAndSetIfChanged(ref number, value);
            NumberText = value?.ToString(CultureInfo.CurrentCulture);
            this.RaisePropertyChanged(nameof(SliderValue));
            MarkEdited();
            NumberChanged?.Invoke();
        }
    }
    public string? Text
    {
        get => text;
        set
        {
            if (text == value)
            {
                return;
            }
            this.RaiseAndSetIfChanged(ref text, value);
            MarkEdited();
            TextChanged?.Invoke();
        }
    }
    public int SelectedIndex
    {
        get => selectedIndex;
        set
        {
            if (selectedIndex == value)
            {
                return;
            }
            this.RaiseAndSetIfChanged(ref selectedIndex, value);
            if (value >= 0 && value < Options.Length)
            {
                MarkEdited();
            }
        }
    }
    private NotePropertyField(string label)
    {
        Label = label;
        ResetCommand = ReactiveCommand.Create(() =>
        {
            ResetRequested = !ResetRequested;
        });
    }
    public NotePropertyField(string label, decimal min, decimal max, IEnumerable<decimal> values, decimal increment = 0.1m) : this(label)
    {
        IsNumber = true;
        Increment = increment;
        SliderMinimum = (double)min;
        SliderMaximum = (double)max;
        decimal[] distinct = values.Distinct().ToArray();
        // 工程已有值可能超出常用编辑范围，避免控件初始化时截断并误标为修改。
        Minimum = distinct.Length > 0 ? Math.Min(min, distinct.Min()) : min;
        Maximum = distinct.Length > 0 ? Math.Max(max, distinct.Max()) : max;
        number = distinct.Length == 1 ? distinct[0] : null;
        numberText = number?.ToString(CultureInfo.CurrentCulture);
    }
    public NotePropertyField(string label, string[] options, IEnumerable<int> values) : this(label)
    {
        IsChoice = true;
        Options = options;
        int[] distinct = values.Distinct().Take(2).ToArray();
        selectedIndex = distinct.Length == 1 && distinct[0] >= 0 && distinct[0] < options.Length ? distinct[0] : -1;
    }
    public NotePropertyField(string label, IEnumerable<string> values) : this(label)
    {
        string[] distinct = values.Distinct().Take(2).ToArray();
        text = distinct.Length == 1 ? distinct[0] : null;
    }
    public void SetNumber(decimal value)
    {
        Number = value;
        MarkEdited();
    }

    public void LoadChoice(IEnumerable<int> values)
    {
        int[] distinct = values.Distinct().Take(2).ToArray();
        this.RaiseAndSetIfChanged(ref selectedIndex, distinct.Length == 1 ? distinct[0] : -1, nameof(SelectedIndex));
        IsEdited = false;
    }

    public void CommitInput()
    {
        if (IsNumber && !ResetRequested && decimal.TryParse(NumberText,
            NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.CurrentCulture, out decimal value))
        {
            Number = value;
        }
    }

    private void MarkEdited()
    {
        IsEdited = true;
        ResetRequested = false;
    }
}
