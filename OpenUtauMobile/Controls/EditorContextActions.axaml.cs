using System.Collections.Generic;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using IconPacks.Avalonia.PhosphorIcons;
using OpenUtauMobile.Themes.OpenUtauMobile.Tokens.Semantic;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Controls;

/// <summary>沿编辑区边缘排列、始终显示的可滚动上下文操作栏。</summary>
public partial class EditorContextActions : UserControl
{
    /// <summary>现有视图模型生成的上下文操作。</summary>
    public static readonly StyledProperty<IReadOnlyList<ContextActionItem>?> ActionsProperty =
        AvaloniaProperty.Register<EditorContextActions, IReadOnlyList<ContextActionItem>?>(nameof(Actions));

    /// <summary>沿所在编辑区边缘的排列方向。</summary>
    public static readonly StyledProperty<Orientation> OrientationProperty =
        AvaloniaProperty.Register<EditorContextActions, Orientation>(nameof(Orientation), Orientation.Vertical);

    /// <summary>现有视图模型生成的上下文操作。</summary>
    public IReadOnlyList<ContextActionItem>? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }

    /// <summary>横向用于走带底栏，纵向用于钢琴卷帘侧栏。</summary>
    public Orientation Orientation
    {
        get => GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>初始化有限滚动视口。</summary>
    public EditorContextActions()
    {
        InitializeComponent();
        UpdateOrientation();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (ActionsList == null) return;
        if (change.Property == ActionsProperty) UpdateActionsList();
        if (change.Property == OrientationProperty) UpdateOrientation();
    }

    /// <summary>将滚动方向同步到所在边缘。</summary>
    private void UpdateOrientation()
    {
        bool horizontal = Orientation == Orientation.Horizontal;
        ActionsList.Orientation = Orientation;
        ActionsScrollViewer.HorizontalScrollBarVisibility = horizontal ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Disabled;
        ActionsScrollViewer.VerticalScrollBarVisibility = horizontal ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Hidden;
    }

    /// <summary>保留操作命令及状态文字，以紧凑图标和文字显示普通操作。</summary>
    private void UpdateActionsList()
    {
        ActionsList.Children.Clear();
        if (Actions == null) return;
        foreach (ContextActionItem action in Actions)
        {
            Control symbol = action.Label != null
                ? new TextBlock { Text = action.Label, FontSize = TypographyTokens.LabelSSize, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center }
                : new PackIconPhosphorIcons { Kind = action.Icon, Width = IconTokens.Small, Height = IconTokens.Small, HorizontalAlignment = HorizontalAlignment.Center };
            Button button = new()
            {
                Classes = { "ContextActionBtn" },
                Command = action.Command,
                Content = new StackPanel
                {
                    Spacing = 2,
                    Children =
                    {
                        symbol,
                        new TextBlock { Classes = { "ContextActionCaption" }, Text = action.Tip, FontSize = TypographyTokens.LabelSSize, LineHeight = 14, MaxHeight = 28, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center },
                    },
                },
            };
            ToolTip.SetTip(button, action.Tip);
            AutomationProperties.SetName(button, action.Tip);
            ActionsList.Children.Add(button);
        }
    }
}
