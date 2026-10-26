using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using IconPacks.Avalonia.PhosphorIcons;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Controls;

/// <summary>保留强类型编辑模式绑定，复用贴边模式选择窗口。</summary>
public partial class PhonemePanelModeSwitcher : UserControl
{
    /// <summary>当前编辑模式。</summary>
    public static readonly StyledProperty<PhonemePanelMode> CurrentModeProperty =
        AvaloniaProperty.Register<PhonemePanelModeSwitcher, PhonemePanelMode>(nameof(CurrentMode),
            PhonemePanelMode.PhonemeSimple, defaultBindingMode: BindingMode.TwoWay);

    /// <summary>当前编辑区使用的模式。</summary>
    public PhonemePanelMode CurrentMode
    {
        get => GetValue(CurrentModeProperty);
        set => SetValue(CurrentModeProperty, value);
    }

    /// <summary>是否展示移动端独立擦除模式。</summary>
    public static readonly StyledProperty<bool> ShowEraseModeProperty =
        AvaloniaProperty.Register<PhonemePanelModeSwitcher, bool>(nameof(ShowEraseMode), true);

    /// <summary>桌面端由鼠标右键擦除，隐藏独立擦除模式。</summary>
    public bool ShowEraseMode
    {
        get => GetValue(ShowEraseModeProperty);
        set => SetValue(ShowEraseModeProperty, value);
    }

    /// <summary>初始化模式选项并将用户选择写回现有绑定。</summary>
    public PhonemePanelModeSwitcher()
    {
        InitializeComponent();
        UpdateOptions();
        UpdateSelection();
        Picker.ModeSelected += value => SetCurrentValue(CurrentModeProperty, (PhonemePanelMode)value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (Picker == null) return;
        if (change.Property == ShowEraseModeProperty) UpdateOptions();
        if (change.Property == CurrentModeProperty || change.Property == ShowEraseModeProperty) UpdateSelection();
    }

    /// <summary>桌面隐藏独立擦除入口时，仍按原有规则展示参数绘制图标。</summary>
    private void UpdateSelection()
    {
        Picker.SelectedValue = (int)(!ShowEraseMode && CurrentMode == PhonemePanelMode.ParameterErase
            ? PhonemePanelMode.ParameterDraw : CurrentMode);
    }

    /// <summary>使用当前项目定义的模式及图标构建选择项。</summary>
    private void UpdateOptions()
    {
        System.Collections.Generic.List<EditorModeOption> items = [
            new((int)PhonemePanelMode.PhonemeSimple, PackIconPhosphorIconsKind.Rows, "PhonemePanel.Mode.Simple"),
            new((int)PhonemePanelMode.PhonemeAdvanced, PackIconPhosphorIconsKind.WaveTriangle, "PhonemePanel.Mode.Advanced"),
            new((int)PhonemePanelMode.ParameterDraw, PackIconPhosphorIconsKind.Pencil, "PhonemePanel.Mode.Draw"),
            new((int)PhonemePanelMode.ParameterErase, PackIconPhosphorIconsKind.Eraser, "PhonemePanel.Mode.Erase"),
        ];
        if (!ShowEraseMode) items.RemoveAt(items.Count - 1);
        Picker.SetOptions(items);
    }
}
