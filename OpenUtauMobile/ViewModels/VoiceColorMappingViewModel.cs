using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using OpenUtauMobile.Helpers;
using ReactiveUI;

namespace OpenUtauMobile.ViewModels;

/// <summary>与桌面相同的音色映射默认值，只在确认后应用。</summary>
public sealed class VoiceColorMappingViewModel : PopupViewModelBase
{
    public const int DefaultIndex = 0;
    public string TrackName { get; }
    public string Title { get; }
    public string Description { get; }
    public IReadOnlyList<VoiceColorMappingRow> Mappings { get; }
    public ReactiveCommand<Unit, Unit> ApplyCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    public VoiceColorMappingViewModel(string[] oldColors, string[] newColors, string trackName, bool vocalModes = false)
    {
        TrackName = trackName;
        Title = L.S(vocalModes ? "VoiceMapping.VocalModesTitle" : "VoiceMapping.Title");
        Description = L.S(vocalModes ? "VoiceMapping.VocalModesHint" : "VoiceMapping.Hint");
        string[] labels = newColors.Length == 0 ? [L.S("VoiceMapping.Default")] : newColors.ToArray();
        labels[DefaultIndex] = L.S("VoiceMapping.Default");
        List<VoiceColorMappingRow> mappings = [];
        for (int index = 0; index < oldColors.Length; index++)
        {
            int match = Array.IndexOf(newColors, oldColors[index]);
            int selected = index == DefaultIndex ? DefaultIndex
                : match >= DefaultIndex ? match
                : index < newColors.Length ? index : DefaultIndex;
            mappings.Add(new VoiceColorMappingRow(index == DefaultIndex ? labels[DefaultIndex] : oldColors[index],
                index, selected, labels));
        }
        Mappings = mappings;
        ApplyCommand = ReactiveCommand.Create(() => RaiseClose(true));
        CancelCommand = ReactiveCommand.Create(RequestBack);
    }
}

public sealed class VoiceColorMappingRow : ReactiveObject
{
    private int selectedIndex;
    public string Name { get; }
    public int OldIndex { get; }
    public IReadOnlyList<string> NewColors { get; }
    public int SelectedIndex
    {
        get => selectedIndex;
        set
        {
            if (value < VoiceColorMappingViewModel.DefaultIndex || value >= NewColors.Count) return;
            this.RaiseAndSetIfChanged(ref selectedIndex, value);
        }
    }

    public VoiceColorMappingRow(string name, int oldIndex, int selectedIndex, IReadOnlyList<string> colors)
    {
        Name = name;
        OldIndex = oldIndex;
        this.selectedIndex = selectedIndex;
        NewColors = colors;
    }
}
