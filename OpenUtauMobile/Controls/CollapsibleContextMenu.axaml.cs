using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using IconPacks.Avalonia.PhosphorIcons;
using OpenUtauMobile.Themes.OpenUtauMobile.Runtime;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Controls;

public partial class CollapsibleContextMenu : UserControl
{
    // 依赖属性：上下文操作列表
    public static readonly StyledProperty<IReadOnlyList<ContextActionItem>?> ActionsProperty =
        AvaloniaProperty.Register<CollapsibleContextMenu, IReadOnlyList<ContextActionItem>?>(nameof(Actions));

    /// <summary>
    /// 操作项列表
    /// </summary>
    public IReadOnlyList<ContextActionItem>? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }

    // 依赖属性：是否展开（双向绑定到 ViewModel）
    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<CollapsibleContextMenu, bool>(nameof(IsExpanded),
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    /// <summary>
    /// 是否展开
    /// </summary>
    public bool IsExpanded
    {
        get => GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    public CollapsibleContextMenu()
    {
        InitializeComponent();
        UpdateVisualState();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsExpandedProperty || change.Property == ActionsProperty)
        {
            UpdateVisualState();
        }
    }

    /// <summary>
    /// 更新视图状态
    /// </summary>
    private void UpdateVisualState()
    {
        if (Actions?.Count == 0)
        {
            IsVisible = false;
            return;
        }

        IsVisible = true;
        UpdateActionsList();
        ActionsList.Measure(Size.Infinity);
        ContentHost.Height = IsExpanded ? ActionsList.DesiredSize.Height : 0;
        ToggleButton.Content = IsExpanded
            ? new PackIconPhosphorIcons
            {
                Kind = PackIconPhosphorIconsKind.CaretUp,
                Foreground = ThemeResources.GetBrush("Sem.Color.OnSecondaryContainer"),
            }
            : new PackIconPhosphorIcons
            {
                Kind = PackIconPhosphorIconsKind.CaretDown,
                Foreground = ThemeResources.GetBrush("Sem.Color.OnSecondaryContainer"),
            };
    }

    private void UpdateActionsList()
    {
        ActionsList.Children.Clear();

        if (Actions == null)
        {
            return;
        }

        // 逐一创建按钮
        foreach (ContextActionItem action in Actions)
        {
            Button button = new()
            {
                Classes = { "ContextActionBtn" },
                Command = action.Command,
                Content = new PackIconPhosphorIcons
                {
                    Kind = action.Icon,
                },
            };

            ToolTip.SetTip(button, action.Tip);
            ActionsList.Children.Add(button);
        }
    }

    private void OnToggleClick(object? sender, RoutedEventArgs e)
    {
        IsExpanded = !IsExpanded;
    }
}