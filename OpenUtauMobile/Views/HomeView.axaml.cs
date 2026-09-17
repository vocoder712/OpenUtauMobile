using System;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Threading;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Interactivity;
using OpenUtauMobile.Services.Dialogs;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Views;

public partial class HomeView : UserControl
{
    private bool _layoutInitialized;
    public HomeView()
    {
        InitializeComponent();
        _newProjectHoldTimer.Tick += NewProjectHoldElapsed;
        NewProjectButton.AddHandler(PointerPressedEvent, NewProjectPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        NewProjectButton.AddHandler(PointerMovedEvent, NewProjectPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        NewProjectButton.AddHandler(PointerReleasedEvent, (_, _) => CancelNewProjectHold(), RoutingStrategies.Tunnel, handledEventsToo: true);
        NewProjectButton.PointerCaptureLost += (_, _) => CancelNewProjectHold();
        NewProjectButton.AddHandler(KeyDownEvent, (_, _) => _templateHold = false, RoutingStrategies.Tunnel);
    }

    private readonly DispatcherTimer _newProjectHoldTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private IPointer? _newProjectPointer;
    private Point _newProjectPressPosition;
    private bool _templateHold;

    private void NewProjectPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        CancelNewProjectHold();
        _templateHold = false;
        if (!e.GetCurrentPoint(NewProjectButton).Properties.IsLeftButtonPressed) return;
        _newProjectPointer = e.Pointer;
        _newProjectPressPosition = e.GetPosition(NewProjectButton);
        _newProjectHoldTimer.Start();
    }

    private void NewProjectPointerMoved(object? sender, PointerEventArgs e)
    {
        if (e.Pointer != _newProjectPointer) return;
        Point position = e.GetPosition(NewProjectButton);
        Point delta = position - _newProjectPressPosition;
        if (delta.X * delta.X + delta.Y * delta.Y > 64 ||
            !new Rect(NewProjectButton.Bounds.Size).Contains(position))
        {
            CancelNewProjectHold();
        }
    }

    private void CancelNewProjectHold()
    {
        _newProjectHoldTimer.Stop();
        _newProjectPointer = null;
    }

    private async void NewProjectHoldElapsed(object? sender, EventArgs e)
    {
        IPointer? pointer = _newProjectPointer;
        CancelNewProjectHold();
        if (pointer == null || !NewProjectButton.IsEffectivelyEnabled) return;
        // 先阻止本次按下产生点击，再释放捕获，让弹窗接管后续输入。
        _templateHold = true;
        pointer.Capture(null);
        if (DataContext is HomeViewModel vm) await vm.OpenTemplatesAsync();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CancelNewProjectHold();
        base.OnDetachedFromVisualTree(e);
    }

    private void NewProjectClick(object? sender, RoutedEventArgs e)
    {
        if (!_templateHold && DataContext is HomeViewModel vm)
        {
            vm.NewCommand.Execute().Subscribe();
        }
        e.Handled = true;
    }

    private bool _isLandscape;

    private void FooterLogotypePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount != 3)
        {
            return;
        }

        string? lyrics = typeof(HomeView).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "VersionLyrics")?
            .Value;
        if (!string.IsNullOrWhiteSpace(lyrics))
        {
            ToastService.Enqueue(lyrics);
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);

        if (e.NewSize.Width <= 0 || e.NewSize.Height <= 0)
        {
            return;
        }

        bool newIsLandscape = e.NewSize.Width > e.NewSize.Height;
        if (!_layoutInitialized)
        {
            _isLandscape = newIsLandscape;
            ApplyResponsiveLayout();
            _layoutInitialized = true;
            return;
        }

        if (newIsLandscape == _isLandscape)
        {
            return;
        }

        _isLandscape = newIsLandscape;
        ApplyResponsiveLayout();
    }

    private void ApplyResponsiveLayout()
    {
        if (_isLandscape)
        {
            MainLayoutGrid.RowDefinitions = [with("Auto, *, Auto")];
            MainLayoutGrid.ColumnDefinitions = [with("Auto, *")];

            Grid.SetRow(RecoveryHost, 0);
            Grid.SetColumn(RecoveryHost, 0);
            Grid.SetColumnSpan(RecoveryHost, 2);

            Grid.SetRow(SectionA, 1);
            Grid.SetColumn(SectionA, 0);
            Grid.SetColumnSpan(SectionA, 1);

            Grid.SetRow(SectionB, 1);
            Grid.SetColumn(SectionB, 1);
            Grid.SetColumnSpan(SectionB, 1);

            Grid.SetRow(FooterBrand, 2);
            Grid.SetColumn(FooterBrand, 0);
            Grid.SetColumnSpan(FooterBrand, 2);

            ActionButtonsPanel.Orientation = Orientation.Vertical;
            ActionButtonsScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            ActionButtonsScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
            return;
        }

        MainLayoutGrid.RowDefinitions = new RowDefinitions("Auto, Auto, *, Auto");
        MainLayoutGrid.ColumnDefinitions = new ColumnDefinitions("*");

        Grid.SetRow(RecoveryHost, 0);
        Grid.SetColumn(RecoveryHost, 0);
        Grid.SetColumnSpan(RecoveryHost, 1);

        Grid.SetRow(SectionA, 1);
        Grid.SetColumn(SectionA, 0);
        Grid.SetColumnSpan(SectionA, 1);

        Grid.SetRow(SectionB, 2);
        Grid.SetColumn(SectionB, 0);
        Grid.SetColumnSpan(SectionB, 1);

        Grid.SetRow(FooterBrand, 3);
        Grid.SetColumn(FooterBrand, 0);
        Grid.SetColumnSpan(FooterBrand, 1);

        ActionButtonsPanel.Orientation = Orientation.Horizontal;
        ActionButtonsScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
        ActionButtonsScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
    }
}
