using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using OpenUtauMobile.Themes.OpenUtauMobile.Controls;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Controls;

/// <summary>编辑区右上角的固定纵向浮动工具栏，复用公共工具栏的溢出和键盘行为。</summary>
public partial class EditorContextActions : UserControl
{
    /// <summary>现有视图模型生成的上下文操作。</summary>
    public static readonly StyledProperty<IReadOnlyList<ContextActionItem>?> ActionsProperty =
        AvaloniaProperty.Register<EditorContextActions, IReadOnlyList<ContextActionItem>?>(nameof(Actions));

    /// <summary>保留原有交互的编辑模式入口。</summary>
    public static readonly StyledProperty<Control?> ModeSwitchProperty =
        AvaloniaProperty.Register<EditorContextActions, Control?>(nameof(ModeSwitch));

    /// <summary>现有视图模型生成的上下文操作。</summary>
    public IReadOnlyList<ContextActionItem>? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }

    /// <summary>独立于操作列表的编辑模式入口。</summary>
    public Control? ModeSwitch
    {
        get => GetValue(ModeSwitchProperty);
        set => SetValue(ModeSwitchProperty, value);
    }

    /// <summary>初始化固定在当前编辑区内的浮动层。</summary>
    public EditorContextActions()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (Toolbar == null) return;
        if (change.Property == ActionsProperty) UpdateActionsList();
        if (change.Property == ModeSwitchProperty) Toolbar.LeadingContent = ModeSwitch;
    }

    /// <summary>将现有操作适配到公共工具栏，命令与无障碍名称保持一致。</summary>
    private void UpdateActionsList()
    {
        IReadOnlyList<FloatingToolbarAction> actions = Actions == null ? [] : [.. Actions.Select(action => new FloatingToolbarAction
        {
            Text = action.Tip,
            Icon = action.Icon,
            Label = action.Label,
            Command = action.Command,
            IsDanger = action.IsDanger
        })];
        Toolbar.Actions = actions;
    }
}
