using System;
using System.Collections.Generic;
using System.Reactive.Disposables;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;
using IconPacks.Avalonia.PhosphorIcons;
using OpenUtauMobile.Services.Dialogs;
using OpenUtauMobile.Themes.OpenUtauMobile.Tokens.Semantic;

namespace OpenUtauMobile.Controls;

/// <summary>编辑模式选项的值、现有图标和本地化资源。</summary>
public sealed record EditorModeOption(int Value, PackIconPhosphorIconsKind Icon, string ResourceKey);

/// <summary>三个编辑区共用的贴边模式入口及自动换行选择窗口。</summary>
public partial class EditorModePicker : UserControl
{
    /// <summary>当前选择的模式值。</summary>
    public static readonly StyledProperty<int> SelectedValueProperty =
        AvaloniaProperty.Register<EditorModePicker, int>(nameof(SelectedValue), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>模式窗口标题。</summary>
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<EditorModePicker, string>(nameof(Title), string.Empty);

    /// <summary>窗口优先展开的方向。</summary>
    public static readonly StyledProperty<PlacementMode> PlacementProperty =
        AvaloniaProperty.Register<EditorModePicker, PlacementMode>(nameof(Placement), PlacementMode.Left);

    private static WeakReference<EditorModePicker>? _openPicker;
    private readonly EditorModeLayer _layer = new();
    private IReadOnlyList<EditorModeOption> _options = [];
    private readonly List<IDisposable> _resourceBindings = [];
    private CompositeDisposable? _visibilitySubscriptions;

    /// <summary>当前选择的模式值，由各编辑区适配其强类型枚举。</summary>
    public int SelectedValue
    {
        get => GetValue(SelectedValueProperty);
        set => SetValue(SelectedValueProperty, value);
    }

    /// <summary>窗口标题及入口的辅助说明。</summary>
    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>窗口优先展开的方向。</summary>
    public PlacementMode Placement
    {
        get => GetValue(PlacementProperty);
        set => SetValue(PlacementProperty, value);
    }

    /// <summary>用户选择一个模式，包括再次选择当前模式。</summary>
    public event Action<int>? ModeSelected;

    /// <summary>初始化共用入口，离开控件树时关闭弹出层。</summary>
    public EditorModePicker()
    {
        InitializeComponent();
        _layer.ToggleRequested += TogglePicker;
        _layer.Closed += OnLayerClosed;
        AttachedToVisualTree += (_, _) =>
        {
            _layer.Mount(this, SwitchButton);
            PopupService.Opening += HidePickerImmediately;
        };
        DetachedFromVisualTree += (_, _) =>
        {
            PopupService.Opening -= HidePickerImmediately;
            _layer.Unmount();
        };
    }

    /// <summary>设置当前编辑区允许的模式。</summary>
    public void SetOptions(IReadOnlyList<EditorModeOption> items)
    {
        HidePickerImmediately();
        foreach (IDisposable binding in _resourceBindings) binding.Dispose();
        _resourceBindings.Clear();
        _options = items;
        _layer.Items.Children.Clear();
        foreach (EditorModeOption option in _options)
        {
            TextBlock label = new()
            {
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                TextAlignment = Avalonia.Media.TextAlignment.Center,
                FontSize = TypographyTokens.LabelSSize,
            };
            _resourceBindings.Add(label.Bind(TextBlock.TextProperty, this.GetResourceObservable(option.ResourceKey)));
            Button button = new()
            {
                Classes = { "EditModeOption" },
                Tag = option.Value,
                Content = new StackPanel
                {
                    Spacing = 4,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Children =
                    {
                        new PackIconPhosphorIcons { Kind = option.Icon, Width = IconTokens.Small, Height = IconTokens.Small, HorizontalAlignment = HorizontalAlignment.Center },
                        label,
                    },
                },
            };
            _resourceBindings.Add(button.Bind(AutomationProperties.NameProperty, this.GetResourceObservable(option.ResourceKey)));
            button.Click += (_, _) =>
            {
                SetCurrentValue(SelectedValueProperty, option.Value);
                ModeSelected?.Invoke(option.Value);
                HidePicker();
            };
            _layer.Items.Children.Add(button);
        }
        UpdateSelection();
    }

    /// <summary>关闭当前展开的编辑模式窗口。</summary>
    public static void CloseOpenPicker()
    {
        if (_openPicker?.TryGetTarget(out EditorModePicker? picker) == true) picker.HidePickerImmediately();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (SwitchButton == null) return;
        if (change.Property == SelectedValueProperty)
        {
            UpdateSelection();
            HidePicker();
        }
        if (change.Property == TitleProperty)
        {
            HidePickerImmediately();
            _layer.TitleLabel.Text = Title;
            ToolTip.SetTip(SwitchButton, Title);
            AutomationProperties.SetName(SwitchButton, Title);
        }
        if (change.Property == PlacementProperty) HidePickerImmediately();
    }

    /// <summary>同步入口图标与窗口中的选中项，颜色由局部样式动态解析。</summary>
    private void UpdateSelection()
    {
        foreach (EditorModeOption option in _options)
        {
            if (option.Value == SelectedValue)
                SwitchButton.Content = new PackIconPhosphorIcons { Kind = option.Icon, Width = IconTokens.Small, Height = IconTokens.Small };
        }
        foreach (Control item in _layer.Items.Children)
            item.Classes.Set("Selected", Equals(item.Tag, SelectedValue));
    }

    private void OnSwitchClick(object? sender, RoutedEventArgs e) => TogglePicker();

    /// <summary>同一入口在收回途中再次点击时反向展开，保留本次的矩形和父容器。</summary>
    private void TogglePicker()
    {
        if (_layer.IsOpen)
        {
            if (_layer.IsClosing) _layer.Show(Title, Placement);
            else HidePicker();
            return;
        }
        if (!IsEffectivelyVisible) return;
        CloseOpenPicker();
        _openPicker = new WeakReference<EditorModePicker>(this);
        _visibilitySubscriptions?.Dispose();
        _visibilitySubscriptions = new CompositeDisposable();
        // 仅在窗口展开期间监听所在编辑区的显隐，关闭后立即释放祖先订阅。
        foreach (Visual ancestor in this.GetSelfAndVisualAncestors())
            ancestor.GetObservable(IsVisibleProperty).Subscribe(visible =>
            {
                if (!visible) HidePickerImmediately();
            }).DisposeWith(_visibilitySubscriptions);
        _layer.Show(Title, Placement);
        if (!_layer.IsOpen) OnLayerClosed();
    }

    /// <summary>释放本次显隐监听，入口和独立层保留在各自的固定父容器内。</summary>
    private void OnLayerClosed()
    {
        _visibilitySubscriptions?.Dispose();
        _visibilitySubscriptions = null;
        if (_openPicker?.TryGetTarget(out EditorModePicker? picker) == true && ReferenceEquals(picker, this)) _openPicker = null;
    }

    private void HidePicker() => _layer.Hide();

    private void HidePickerImmediately() => _layer.Hide(immediate: true);
}
