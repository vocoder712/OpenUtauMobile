using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using IconPacks.Avalonia.PhosphorIcons;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Controls;

/// <summary>保留强类型编辑模式绑定，复用贴边模式选择窗口。</summary>
public partial class PianoRollEditModeSwitcher : UserControl
{
    /// <summary>当前编辑模式。</summary>
    public static readonly StyledProperty<PianoRollEditMode> CurrentModeProperty =
        AvaloniaProperty.Register<PianoRollEditModeSwitcher, PianoRollEditMode>(nameof(CurrentMode),
            PianoRollEditMode.Hand, defaultBindingMode: BindingMode.TwoWay);

    /// <summary>当前编辑区使用的模式。</summary>
    public PianoRollEditMode CurrentMode
    {
        get => GetValue(CurrentModeProperty);
        set => SetValue(CurrentModeProperty, value);
    }

    /// <summary>初始化模式选项并将用户选择写回现有绑定。</summary>
    public PianoRollEditModeSwitcher()
    {
        InitializeComponent();
        UpdateOptions();
        Picker.SelectedValue = (int)CurrentMode;
        Picker.ModeSelected += value => SetCurrentValue(CurrentModeProperty, (PianoRollEditMode)value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (Picker == null) return;
        if (change.Property == CurrentModeProperty) Picker.SelectedValue = (int)CurrentMode;
    }

    /// <summary>使用当前项目定义的模式及图标构建选择项。</summary>
    private void UpdateOptions()
    {
        Picker.SetOptions([
            new((int)PianoRollEditMode.Hand, PackIconPhosphorIconsKind.Hand, "PianoRoll.DragSelect"),
            new((int)PianoRollEditMode.MultiSelect, PackIconPhosphorIconsKind.Selection, "PianoRoll.MultiSelect"),
            new((int)PianoRollEditMode.Note, PackIconPhosphorIconsKind.MusicNoteSimple, "PianoRoll.NotePen"),
            new((int)PianoRollEditMode.PitchPen, PackIconPhosphorIconsKind.Pencil, "PianoRoll.PitchPen"),
            new((int)PianoRollEditMode.Anchor, PackIconPhosphorIconsKind.Cursor, "PianoRoll.Anchor"),
            new((int)PianoRollEditMode.Vibrato, PackIconPhosphorIconsKind.WaveSine, "PianoRoll.Vibrato"),
        ]);
    }
}