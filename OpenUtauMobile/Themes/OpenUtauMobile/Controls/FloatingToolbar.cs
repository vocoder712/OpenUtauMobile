using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.VisualTree;
using IconPacks.Avalonia.PhosphorIcons;
using OpenUtauMobile.Themes.OpenUtauMobile.Tokens.Components;

namespace OpenUtauMobile.Themes.OpenUtauMobile.Controls;

/// <summary>
/// 页内浮动操作栏。放入有限尺寸的覆盖层并拉伸至整个宿主，锚点使用宿主坐标。
/// 操作按集合顺序排列；主面板放不下的尾部操作及固定收纳项进入溢出面板。
/// </summary>
public sealed class FloatingToolbar : TemplatedControl
{
    /// <summary>按优先顺序排列的操作；替换集合以更新工具栏。</summary>
    public static readonly StyledProperty<IReadOnlyList<FloatingToolbarAction>> ActionsProperty =
        AvaloniaProperty.Register<FloatingToolbar, IReadOnlyList<FloatingToolbarAction>>(nameof(Actions), []);

    /// <summary>需要避让的选区或目标在宿主中的矩形。</summary>
    public static readonly StyledProperty<Rect> AnchorRectProperty =
        AvaloniaProperty.Register<FloatingToolbar, Rect>(nameof(AnchorRect));

    /// <summary>主面板是否只显示图标；固定纵向布局的溢出面板也遵循此设置。</summary>
    public static readonly StyledProperty<bool> IconOnlyProperty =
        AvaloniaProperty.Register<FloatingToolbar, bool>(nameof(IconOnly));

    /// <summary>固定在宿主右上角，纵向排列并向左展开纯图标溢出面板。</summary>
    public static readonly StyledProperty<bool> IsFixedVerticalProperty =
        AvaloniaProperty.Register<FloatingToolbar, bool>(nameof(IsFixedVertical));

    /// <summary>固定纵向工具栏上方的独立入口。</summary>
    public static readonly StyledProperty<Control?> LeadingContentProperty =
        AvaloniaProperty.Register<FloatingToolbar, Control?>(nameof(LeadingContent));

    /// <summary>溢出面板的展开状态。</summary>
    public static readonly StyledProperty<bool> IsOverflowOpenProperty =
        AvaloniaProperty.Register<FloatingToolbar, bool>(nameof(IsOverflowOpen));

    /// <summary>由使用者或主题资源提供的本地化更多按钮名称。</summary>
    public static readonly StyledProperty<string> MoreTextProperty =
        AvaloniaProperty.Register<FloatingToolbar, string>(nameof(MoreText), string.Empty);

    /// <summary>按优先顺序排列的操作。</summary>
    public IReadOnlyList<FloatingToolbarAction> Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }

    /// <summary>目标在覆盖层中的矩形；位置更新不关闭已展开的面板。</summary>
    public Rect AnchorRect
    {
        get => GetValue(AnchorRectProperty);
        set => SetValue(AnchorRectProperty, value);
    }

    /// <summary>主面板只显示图标。</summary>
    public bool IconOnly
    {
        get => GetValue(IconOnlyProperty);
        set => SetValue(IconOnlyProperty, value);
    }

    /// <summary>使用固定的纵向布局，保留锚点布局作为默认行为。</summary>
    public bool IsFixedVertical
    {
        get => GetValue(IsFixedVerticalProperty);
        set => SetValue(IsFixedVerticalProperty, value);
    }

    /// <summary>独立于上下文操作集合的固定入口。</summary>
    public Control? LeadingContent
    {
        get => GetValue(LeadingContentProperty);
        set => SetValue(LeadingContentProperty, value);
    }

    /// <summary>展开或收起溢出面板。</summary>
    public bool IsOverflowOpen
    {
        get => GetValue(IsOverflowOpenProperty);
        set => SetValue(IsOverflowOpenProperty, value);
    }

    /// <summary>更多按钮的提示与无障碍名称。</summary>
    public string MoreText
    {
        get => GetValue(MoreTextProperty);
        set => SetValue(MoreTextProperty, value);
    }

    private Border? _mainPanel;
    private Border _leadingPanel = null!;
    private StackPanel _mainLayout = null!;
    private Border _overflowPanel = null!;
    private StackPanel _mainItems = null!;
    private StackPanel _overflowItems = null!;
    private ToggleButton _more = null!;
    private TopLevel? _inputRoot;
    private bool _expandUp;
    private double _verticalInset = FloatingToolbarTokens.ViewportInset;

    /// <summary>绑定模板中的两个面板，具体交互状态由基础按钮主题负责。</summary>
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _mainPanel = e.NameScope.Get<Border>("PART_MainPanel");
        _leadingPanel = e.NameScope.Get<Border>("PART_LeadingPanel");
        _mainLayout = e.NameScope.Get<StackPanel>("PART_MainLayout");
        _overflowPanel = e.NameScope.Get<Border>("PART_OverflowPanel");
        _mainItems = e.NameScope.Get<StackPanel>("PART_MainItems");
        _overflowItems = e.NameScope.Get<StackPanel>("PART_OverflowItems");
        _more = e.NameScope.Get<ToggleButton>("PART_More");
        RebuildActions();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (_mainPanel == null) return;
        if (change.Property == ActionsProperty || change.Property == IconOnlyProperty || change.Property == IsFixedVerticalProperty)
        {
            SetCurrentValue(IsOverflowOpenProperty, false);
            RebuildActions();
        }
        if (change.Property == AnchorRectProperty || change.Property == IsOverflowOpenProperty || change.Property == LeadingContentProperty)
        {
            InvalidateMeasure();
        }
        if (change.Property == IsVisibleProperty && !IsVisible)
        {
            SetCurrentValue(IsOverflowOpenProperty, false);
        }
    }

    /// <summary>生成主面板和溢出面板共用命令的按钮，随后由测量分配可见项。</summary>
    private void RebuildActions()
    {
        _mainLayout.Orientation = _mainItems.Orientation = IsFixedVertical ? Orientation.Vertical : Orientation.Horizontal;
        _mainItems.Children.Clear();
        _overflowItems.Children.Clear();
        foreach (FloatingToolbarAction action in Actions)
        {
            _mainItems.Children.Add(CreateActionButton(action, false));
            _overflowItems.Children.Add(CreateActionButton(action, true));
        }
        InvalidateMeasure();
    }

    /// <summary>按钮直接使用 ICommand，因此两个面板同步响应 CanExecute。</summary>
    private Button CreateActionButton(FloatingToolbarAction action, bool overflow)
    {
        PackIconPhosphorIcons icon = new()
        {
            Kind = action.Icon,
            VerticalAlignment = VerticalAlignment.Center
        };
        Button button = new()
        {
            Command = action.Command,
            CommandParameter = action.CommandParameter,
            Theme = (ControlTheme)this.FindResource(overflow && !IsFixedVertical
                ? "OpumFloatingToolbarOverflowAction" : "OpumFloatingToolbarAction")!
        };
        if (action.IsDanger) button.Classes.Add("danger");
        if (IconOnly && (!overflow || IsFixedVertical))
        {
            button.Classes.Add("toolbarIcon");
            button.Content = icon;
        }
        else
        {
            Grid content = new()
            {
                ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star)],
                ColumnSpacing = overflow ? 16 : 12
            };
            TextBlock text = new()
            {
                Text = action.Text,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(text, 1);
            content.Children.Add(icon);
            content.Children.Add(text);
            button.Content = content;
        }
        ToolTip.SetTip(button, action.Text);
        AutomationProperties.SetName(button, action.Text);
        button.Click += OnActionClick;
        return button;
    }

    /// <summary>执行操作后收起溢出面板，保持选区及主面板由业务方控制。</summary>
    private void OnActionClick(object? sender, RoutedEventArgs e)
    {
        bool restoreFocus = IsOverflowOpen;
        SetCurrentValue(IsOverflowOpenProperty, false);
        if (restoreFocus) _more.Focus();
    }

    /// <summary>按排列方向的可用长度分配操作；预留更多按钮后收纳尾部操作。</summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        if (_mainPanel == null) return base.MeasureOverride(availableSize);
        double width = Math.Max(0, availableSize.Width - FloatingToolbarTokens.ViewportInset * 2);
        _leadingPanel.IsVisible = IsFixedVertical && LeadingContent != null;
        // 狭矮宿主仅压缩外侧留白，保留两个 40 DIP 入口的完整命中区。
        _verticalInset = _leadingPanel.IsVisible && Actions.Count > 0
            ? Math.Clamp((availableSize.Height - FloatingToolbarTokens.ItemSize * 2) / 3,
                0, FloatingToolbarTokens.ViewportInset)
            : FloatingToolbarTokens.ViewportInset;
        _leadingPanel.Measure(availableSize);
        double leadingHeight = _leadingPanel.IsVisible
            ? _leadingPanel.DesiredSize.Height + _verticalInset : 0;
        double height = Math.Max(0, availableSize.Height - _verticalInset * 2 - leadingHeight);
        double availableLength = IsFixedVertical ? height : width;
        double requestedLength = 0;
        bool hasOverflow = false;
        for (int i = 0; i < Actions.Count; i++)
        {
            Control button = _mainItems.Children[i];
            button.IsVisible = true;
            button.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            if (Actions[i].AlwaysInOverflow) hasOverflow = true;
            else requestedLength += IsFixedVertical ? button.DesiredSize.Height : button.DesiredSize.Width;
        }
        hasOverflow |= requestedLength > availableLength;
        if (!hasOverflow) SetCurrentValue(IsOverflowOpenProperty, false);
        double remaining = availableLength - (hasOverflow ? FloatingToolbarTokens.ItemSize : 0);
        bool tailOverflow = false;
        for (int i = 0; i < Actions.Count; i++)
        {
            Control button = _mainItems.Children[i];
            double length = IsFixedVertical ? button.DesiredSize.Height : button.DesiredSize.Width;
            bool show = !Actions[i].AlwaysInOverflow && !tailOverflow && length <= remaining;
            if (show) remaining -= length;
            else if (!Actions[i].AlwaysInOverflow) tailOverflow = true;
            button.IsVisible = show;
            _overflowItems.Children[i].IsVisible = !show;
        }
        _more.IsVisible = hasOverflow;
        _mainPanel.IsVisible = Actions.Count > 0;
        _mainPanel.MaxWidth = width;
        _mainPanel.MaxHeight = IsFixedVertical ? height : double.PositiveInfinity;
        _mainPanel.Measure(availableSize);
        _overflowPanel.IsVisible = hasOverflow && IsOverflowOpen;
        if (_overflowPanel.IsVisible)
        {
            if (IsFixedVertical)
            {
                _overflowPanel.Width = Math.Min(width, FloatingToolbarTokens.ItemSize);
                _overflowPanel.MaxHeight = Math.Max(0, availableSize.Height - FloatingToolbarTokens.ViewportInset * 2);
            }
            else
            {
                double menuWidth = Math.Min(width, FloatingToolbarTokens.OverflowMaxWidth);
                _overflowItems.Measure(new Size(menuWidth, double.PositiveInfinity));
                _overflowPanel.Width = Math.Min(menuWidth,
                    Math.Max(FloatingToolbarTokens.OverflowMinWidth, _overflowItems.DesiredSize.Width));
                Point main = GetMainPosition(availableSize);
                double above = Math.Max(0, main.Y - FloatingToolbarTokens.AnchorGap - FloatingToolbarTokens.ViewportInset);
                double below = Math.Max(0, availableSize.Height - FloatingToolbarTokens.ViewportInset -
                    main.Y - _mainPanel.DesiredSize.Height - FloatingToolbarTokens.AnchorGap);
                double requestedHeight = _overflowItems.DesiredSize.Height;
                _expandUp = above >= requestedHeight || above > below;
                _overflowPanel.MaxHeight = _expandUp ? above : below;
            }
        }
        base.MeasureOverride(availableSize);
        // 覆盖层本身不向父容器请求额外空间，由宿主拉伸到编辑页大小。
        return default;
    }

    /// <summary>优先放在锚点上方，不足时移至下方，最后限制在整个宿主视口内。</summary>
    private Point GetMainPosition(Size viewport)
    {
        Size panel = _mainPanel!.DesiredSize;
        const double inset = FloatingToolbarTokens.ViewportInset;
        if (IsFixedVertical)
        {
            double leadingHeight = _leadingPanel.IsVisible
                ? _leadingPanel.DesiredSize.Height + _verticalInset : 0;
            return new Point(Math.Max(inset, viewport.Width - inset - panel.Width), _verticalInset + leadingHeight);
        }
        double x = AnchorRect.Center.X - panel.Width / 2;
        double y = AnchorRect.Top - FloatingToolbarTokens.AnchorGap - panel.Height;
        if (y < inset) y = AnchorRect.Bottom + FloatingToolbarTokens.AnchorGap;
        return new Point(
            Math.Clamp(x, inset, Math.Max(inset, viewport.Width - inset - panel.Width)),
            Math.Clamp(y, inset, Math.Max(inset, viewport.Height - inset - panel.Height)));
    }

    /// <summary>两个面板在同一个页内覆盖层中排布，不建立平台窗口或原生弹出层。</summary>
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_mainPanel == null) return base.ArrangeOverride(finalSize);
        Point main = GetMainPosition(finalSize);
        Canvas.SetLeft(_leadingPanel, Math.Max(FloatingToolbarTokens.ViewportInset,
            finalSize.Width - FloatingToolbarTokens.ViewportInset - _leadingPanel.DesiredSize.Width));
        Canvas.SetTop(_leadingPanel, _verticalInset);
        Canvas.SetLeft(_mainPanel, main.X);
        Canvas.SetTop(_mainPanel, main.Y);
        if (!_overflowPanel.IsVisible) return base.ArrangeOverride(finalSize);
        if (IsFixedVertical)
        {
            Canvas.SetLeft(_overflowPanel, Math.Max(FloatingToolbarTokens.ViewportInset,
                main.X - FloatingToolbarTokens.AnchorGap - _overflowPanel.DesiredSize.Width));
            Canvas.SetTop(_overflowPanel, Math.Clamp(main.Y, FloatingToolbarTokens.ViewportInset,
                Math.Max(FloatingToolbarTokens.ViewportInset, finalSize.Height - FloatingToolbarTokens.ViewportInset - _overflowPanel.DesiredSize.Height)));
            return base.ArrangeOverride(finalSize);
        }
        double x = main.X + _mainPanel.DesiredSize.Width - _overflowPanel.DesiredSize.Width;
        Canvas.SetLeft(_overflowPanel, Math.Clamp(x, FloatingToolbarTokens.ViewportInset,
            Math.Max(FloatingToolbarTokens.ViewportInset, finalSize.Width -
                FloatingToolbarTokens.ViewportInset - _overflowPanel.DesiredSize.Width)));
        Canvas.SetTop(_overflowPanel, _expandUp
            ? main.Y - FloatingToolbarTokens.AnchorGap - _overflowPanel.DesiredSize.Height
            : main.Y + _mainPanel.DesiredSize.Height + FloatingToolbarTokens.AnchorGap);
        return base.ArrangeOverride(finalSize);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _inputRoot = TopLevel.GetTopLevel(this)!;
        _inputRoot.AddHandler(PointerPressedEvent, OnRootPointerPressed, RoutingStrategies.Tunnel, true);
        _inputRoot.AddHandler(KeyDownEvent, OnRootKeyDown, RoutingStrategies.Tunnel, true);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _inputRoot!.RemoveHandler(PointerPressedEvent, OnRootPointerPressed);
        _inputRoot.RemoveHandler(KeyDownEvent, OnRootKeyDown);
        _inputRoot = null;
        SetCurrentValue(IsOverflowOpenProperty, false);
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>外部按下只收起溢出面板，不拦截原有编辑手势。</summary>
    private void OnRootPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (IsOverflowOpen && e.Source is Visual source &&
            (!source.GetSelfAndVisualAncestors().Contains(this) || source.GetSelfAndVisualAncestors().Contains(_leadingPanel)))
        {
            SetCurrentValue(IsOverflowOpenProperty, false);
        }
    }

    /// <summary>Escape 收起菜单并将焦点还给更多按钮。</summary>
    private void OnRootKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || !IsOverflowOpen) return;
        SetCurrentValue(IsOverflowOpenProperty, false);
        _more.Focus();
        e.Handled = true;
    }

    /// <summary>方向键在当前面板的可用操作间移动，Tab 沿用框架的焦点导航。</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down)) return;
        Button[] buttons = IsOverflowOpen
            ? [.. _overflowItems.Children.OfType<Button>().Where(button => button is { IsVisible: true, IsEnabled: true })]
            : [.. _mainItems.Children.OfType<Button>().Append(_more).Where(button => button is { IsVisible: true, IsEnabled: true })];
        int index = Array.FindIndex(buttons, button => button.IsKeyboardFocusWithin);
        int step = e.Key is Key.Left or Key.Up ? -1 : 1;
        if (buttons.Length > 0)
        {
            int target = index < 0 ? (step > 0 ? 0 : buttons.Length - 1) : (index + step + buttons.Length) % buttons.Length;
            buttons[target].Focus();
        }
        e.Handled = true;
    }
}
