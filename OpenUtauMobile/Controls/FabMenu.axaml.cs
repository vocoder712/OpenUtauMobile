using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Disposables;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using IconPacks.Avalonia.PhosphorIcons;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Themes.OpenUtauMobile.Controls;
using OpenUtauMobile.Themes.OpenUtauMobile.Tokens.Components;

namespace OpenUtauMobile.Controls;

public sealed record FabMenuAction(string Id, string Label, PackIconPhosphorIconsKind Icon);

/// <summary>MD3 FAB menu：底部向上逐项展开、形状变换、可中断弹簧及键盘导航。</summary>
public partial class FabMenu : UserControl
{
    public static readonly StyledProperty<IReadOnlyList<FabMenuAction>?> ActionsProperty =
        AvaloniaProperty.Register<FabMenu, IReadOnlyList<FabMenuAction>?>(nameof(Actions));
    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<FabMenu, bool>(nameof(IsExpanded), defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<string> OpenLabelProperty =
        AvaloniaProperty.Register<FabMenu, string>(nameof(OpenLabel), string.Empty);
    public IReadOnlyList<FabMenuAction>? Actions { get => GetValue(ActionsProperty); set => SetValue(ActionsProperty, value); }
    public bool IsExpanded { get => GetValue(IsExpandedProperty); set => SetValue(IsExpandedProperty, value); }
    public string OpenLabel { get => GetValue(OpenLabelProperty); set => SetValue(OpenLabelProperty, value); }
    public event Action<FabMenuAction>? ActionInvoked;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly MenuSpring _shape = new(FabMenuTokens.SpatialDamping, FabMenuTokens.SpatialStiffness);
    private readonly MenuSpring _color = new(FabMenuTokens.EffectsDamping, FabMenuTokens.EffectsStiffness);
    private readonly MenuSpring _stagger = new(FabMenuTokens.EffectsDamping, FabMenuTokens.StaggerStiffness);
    private readonly List<ItemVisual> _items = new();
    private readonly CompositeDisposable _resources = new();
    private Color _container;
    private Color _onContainer;
    private Color _primary;
    private Color _onPrimary;
    private BoxShadows _elevation;
    private long _lastFrame;
    private bool _ready;

    public FabMenu()
    {
        InitializeComponent();
        _ready = true;
        _timer.Tick += OnFrame;
        KeyDown += OnMenuKeyDown;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        BindColor("Sem.Color.PrimaryContainer", color => _container = color);
        BindColor("Sem.Color.OnPrimaryContainer", color => _onContainer = color);
        BindColor("Sem.Color.Primary", color => _primary = color);
        BindColor("Sem.Color.OnPrimary", color => _onPrimary = color);
        _resources.Add(this.GetResourceObservable("Sem.Value.Shadow").Subscribe(value =>
        {
            if (value is Color shadow)
            {
                _elevation = new(new BoxShadow { OffsetY = 1, Blur = 3, Color = Color.FromArgb(77, shadow.R, shadow.G, shadow.B) },
                    [new BoxShadow { OffsetY = 4, Blur = 8, Spread = 3, Color = Color.FromArgb(38, shadow.R, shadow.G, shadow.B) }]);
                ButtonChrome.SetBoxShadow(Toggle, _elevation);
                foreach (ItemVisual item in _items) ButtonChrome.SetBoxShadow(item.Button, _elevation);
            }
        }));
        RebuildItems();
        ApplyVisuals();
        if (IsExpanded) StartAnimation();
    }

    private void BindColor(string key, Action<Color> assign) => _resources.Add(this.GetResourceObservable(key).Subscribe(value =>
    {
        if (value is ISolidColorBrush brush) { assign(brush.Color); ApplyVisuals(); }
    }));

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        _resources.Clear();
        SetCurrentValue(IsExpandedProperty, false);
        _timer.Stop();
        _shape.Reset(); _color.Reset(); _stagger.Reset();
        foreach (ItemVisual item in _items) { item.Width.Reset(); item.Alpha.Reset(); }
        ApplyVisuals();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (!_ready) return;
        if (change.Property == ActionsProperty) RebuildItems();
        if (change.Property == IsExpandedProperty)
        {
            Toggle.IsChecked = IsExpanded;
            DismissLayer.IsVisible = IsExpanded;
            MenuScroll.IsHitTestVisible = IsExpanded;
            if (IsExpanded) MenuScroll.IsVisible = true;
            StartAnimation();
            UpdateLabel();
            Toggle.Focus();
        }
        if (change.Property == OpenLabelProperty) UpdateLabel();
    }

    private void RebuildItems()
    {
        if (Actions?.Count > FabMenuTokens.MaximumActions)
            throw new ArgumentException("MD3 FAB menus support at most six actions.", nameof(Actions));
        MenuItems.Children.Clear();
        _items.Clear();
        foreach (FabMenuAction action in Actions ?? [])
        {
            StackPanel content = new()
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = FabMenuTokens.IconLabelGap,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Children =
                {
                    new PackIconPhosphorIcons { Kind = action.Icon, Width = 24, Height = 24 },
                    new TextBlock { Text = action.Label, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center },
                },
            };
            Border contentClip = new() { Child = content, ClipToBounds = true };
            Button button = new() { Content = contentClip, IsHitTestVisible = false, Focusable = false };
            ButtonChrome.SetBoxShadow(button, _elevation);
            button.Classes.Add("FabMenuItem");
            AutomationProperties.SetName(button, action.Label);
            button.Click += (_, _) =>
            {
                if (!IsExpanded) return;
                SetCurrentValue(IsExpandedProperty, false);
                ActionInvoked?.Invoke(action);
            };
            Border shadow = new()
            {
                Child = button,
                CornerRadius = new CornerRadius(FabMenuTokens.OpenRadius),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                Height = FabMenuTokens.ItemHeight,
            };
            MenuItems.Children.Add(shadow);
            // 只收缩按钮宽度并裁剪内容，不缩放文字、图标或纵向命中区。
            button.Measure(Size.Infinity);
            double width = Math.Max(FabMenuTokens.ItemHeight, button.DesiredSize.Width);
            content.Width = Math.Max(0, width - 2 * FabMenuTokens.ItemInset);
            content.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
            ItemVisual item = new(button, shadow, contentClip, width);
            if (IsExpanded) { item.Width.Reset(1); item.Alpha.Reset(1); }
            _items.Add(item);
        }
        _stagger.Reset(IsExpanded ? _items.Count : 0);
        ApplyVisuals();
    }

    private void ToggleClicked(object? sender, RoutedEventArgs e) => SetCurrentValue(IsExpandedProperty, !IsExpanded);
    private void DismissPressed(object? sender, PointerPressedEventArgs e)
    {
        SetCurrentValue(IsExpandedProperty, false);
        e.Handled = true;
    }

    private void UpdateLabel()
    {
        string label = IsExpanded ? L.S("FabMenu.Close") : OpenLabel;
        AutomationProperties.SetName(Toggle, label);
        AutomationProperties.SetHelpText(Toggle, L.S(IsExpanded ? "FabMenu.Expanded" : "FabMenu.Collapsed"));
        ToolTip.SetTip(Toggle, label);
    }

    private void StartAnimation()
    {
        _shape.Target = _color.Target = IsExpanded ? 1 : 0;
        _stagger.Target = IsExpanded ? _items.Count : 0;
        _lastFrame = Stopwatch.GetTimestamp();
        _timer.Start();
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        long now = Stopwatch.GetTimestamp();
        double dt = Math.Clamp((now - _lastFrame) / (double)Stopwatch.Frequency, 0, 0.064);
        _lastFrame = now;
        bool settled = _shape.Step(dt) & _color.Step(dt) & _stagger.Step(dt);
        int shown = (int)Math.Round(_stagger.Value);
        for (int i = 0; i < _items.Count; i++)
        {
            ItemVisual item = _items[i];
            item.Width.Target = item.Alpha.Target = shown >= _items.Count - i ? 1 : 0;
            settled &= item.Width.Step(dt) & item.Alpha.Step(dt);
        }
        ApplyVisuals();
        if (settled)
        {
            _timer.Stop();
            if (!IsExpanded) MenuScroll.IsVisible = false;
        }
    }

    private void ApplyVisuals()
    {
        double shape = _shape.Value;
        double color = Math.Clamp(_color.Value, 0, 1);
        Toggle.CornerRadius = ToggleShadow.CornerRadius = new CornerRadius(FabMenuTokens.ClosedRadius
            + (FabMenuTokens.OpenRadius - FabMenuTokens.ClosedRadius) * shape);
        Toggle.Background = new SolidColorBrush(Mix(_container, _primary, color));
        Toggle.Foreground = new SolidColorBrush(Mix(_onContainer, _onPrimary, color));
        ToggleIcon.Width = ToggleIcon.Height = Math.Max(1, FabMenuTokens.ClosedIconSize
            + (FabMenuTokens.OpenIconSize - FabMenuTokens.ClosedIconSize) * shape);
        ToggleIcon.Kind = shape > 0.5 ? PackIconPhosphorIconsKind.X : PackIconPhosphorIconsKind.Plus;
        foreach (ItemVisual item in _items)
        {
            double alpha = Math.Clamp(item.Alpha.Value, 0, 1);
            item.Shadow.Opacity = alpha;
            item.Button.Width = Math.Max(0, item.NaturalWidth * item.Width.Value);
            item.ContentClip.Width = Math.Max(0, item.Button.Width - 2 * FabMenuTokens.ItemInset);
            item.Button.IsHitTestVisible = item.Button.Focusable = IsExpanded && alpha > 0.5;
            item.Shadow.IsVisible = alpha > 0 || item.Alpha.Target > 0;
        }
        UpdateLabel();
    }

    private static Color Mix(Color a, Color b, double progress) => Color.FromArgb(
        (byte)Math.Round(a.A + (b.A - a.A) * progress), (byte)Math.Round(a.R + (b.R - a.R) * progress),
        (byte)Math.Round(a.G + (b.G - a.G) * progress), (byte)Math.Round(a.B + (b.B - a.B) * progress));

    private void OnMenuKeyDown(object? sender, KeyEventArgs e)
    {
        if (!IsExpanded) return;
        if (e.Key == Key.Escape) { SetCurrentValue(IsExpandedProperty, false); e.Handled = true; return; }
        if (e.Key is not (Key.Tab or Key.Down or Key.Up)) return;
        Button[] order = new[] { Toggle }.Concat(_items.Where(item => item.Button.Focusable).Select(item => item.Button)).ToArray();
        int current = Array.FindIndex(order, button => button.IsFocused);
        bool previous = e.Key == Key.Up || e.Key == Key.Tab && e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        Button focused = order[(current + (previous ? order.Length - 1 : 1) + order.Length) % order.Length];
        focused.Focus();
        focused.BringIntoView();
        e.Handled = true;
    }

    private sealed record ItemVisual(Button Button, Border Shadow, Border ContentClip, double NaturalWidth)
    {
        public MenuSpring Width { get; } = new(FabMenuTokens.SpatialDamping, FabMenuTokens.SpatialStiffness);
        public MenuSpring Alpha { get; } = new(FabMenuTokens.EffectsDamping, FabMenuTokens.EffectsStiffness);
    }

    /// <summary>单位质量的解析弹簧，反向时保留速度；不以固定延迟拼接开关动画。</summary>
    private sealed class MenuSpring(double damping, double stiffness)
    {
        public double Value { get; private set; }
        public double Target { get; set; }
        private double _velocity;

        public void Reset(double value = 0) { Value = Target = value; _velocity = 0; }

        public bool Step(double dt)
        {
            double omega = Math.Sqrt(stiffness);
            double offset = Value - Target;
            double decay = Math.Exp(-damping * omega * dt);
            if (damping < 1)
            {
                double frequency = omega * Math.Sqrt(1 - damping * damping);
                double c = Math.Cos(frequency * dt);
                double s = Math.Sin(frequency * dt);
                double coefficient = (_velocity + damping * omega * offset) / frequency;
                Value = Target + decay * (offset * c + coefficient * s);
                _velocity = decay * (_velocity * c - (damping * omega * coefficient + offset * frequency) * s);
            }
            else
            {
                double coefficient = _velocity + omega * offset;
                Value = Target + decay * (offset + coefficient * dt);
                _velocity = decay * (_velocity - omega * coefficient * dt);
            }
            if (Math.Abs(Value - Target) >= 0.001 || Math.Abs(_velocity) >= 0.001) return false;
            Value = Target; _velocity = 0;
            return true;
        }
    }
}