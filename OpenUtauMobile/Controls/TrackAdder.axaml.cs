using Avalonia;
using Avalonia.Controls;

namespace OpenUtauMobile.Controls;

/// <summary>
/// 轨道列表底部的"添加轨道"按钮。
/// </summary>
public partial class TrackAdder : UserControl
{
    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<TrackAdder, bool>(nameof(IsExpanded));

    public bool IsExpanded
    {
        get => GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // 控制标签显示：展开时显示"添加轨道"，收起时隐藏
        if (change.Property == IsExpandedProperty)
        {
            LabelText.IsVisible = IsExpanded;
        }
    }

    public TrackAdder()
    {
        InitializeComponent();
    }
}