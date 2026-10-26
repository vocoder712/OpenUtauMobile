using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using IconPacks.Avalonia.PhosphorIcons;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Controls;

/// <summary>保留强类型编辑模式绑定，复用贴边模式选择窗口。</summary>
public partial class TrackEditModeSwitcher : UserControl
{
    /// <summary>当前编辑模式。</summary>
    public static readonly StyledProperty<TrackEditMode> CurrentModeProperty =
        AvaloniaProperty.Register<TrackEditModeSwitcher, TrackEditMode>(nameof(CurrentMode),
            TrackEditMode.Normal, defaultBindingMode: BindingMode.TwoWay);

    /// <summary>当前编辑区使用的模式。</summary>
    public TrackEditMode CurrentMode
    {
        get => GetValue(CurrentModeProperty);
        set => SetValue(CurrentModeProperty, value);
    }

    /// <summary>初始化模式选项并将用户选择写回现有绑定。</summary>
    public TrackEditModeSwitcher()
    {
        InitializeComponent();
        UpdateOptions();
        Picker.SelectedValue = (int)CurrentMode;
        Picker.ModeSelected += value => SetCurrentValue(CurrentModeProperty, (TrackEditMode)value);
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
            new EditorModeOption((int)TrackEditMode.Normal, PackIconPhosphorIconsKind.Hand, "Editor.NormalMode"),
            new EditorModeOption((int)TrackEditMode.MultiSelect, PackIconPhosphorIconsKind.Selection, "Editor.MultiSelectMode"),
        ]);
    }
}