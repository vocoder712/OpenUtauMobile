using System;
using System.Linq;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUtau.Core;
using OpenUtauMobile.Services;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Controls;

/// <summary>
/// 单条轨道头控件。由 TrackHeaderCanvas 创建和管理生命周期。
/// </summary>
public partial class TrackHeader : UserControl, IDisposable
{
    private const int HudReleaseDelayMs = 1000;
    private const double HudVolumeWidth = 144;
    private const double HudPanWidth = 144;
    private const int SettingsHoldDurationMs = 600;
    private const double SettingsHoldMovementLimit = 8;

    private readonly DispatcherTimer _hideHudTimer = new();
    private readonly DispatcherTimer _settingsHoldTimer = new();
    private readonly System.Collections.Generic.HashSet<int> _settingsPointersDown = [];
    private TopLevel? _settingsHoldRoot;
    private IPointer? _settingsHoldPointer;
    private Point _settingsHoldOrigin;
    private bool _mobileSettingsGesture;
    private bool _settingsGestureFired;
    private bool _settingsGestureCancelled;
    private bool _settingsHoldIsRenameButton;

    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<TrackHeader, bool>(nameof(IsExpanded));

    public bool IsExpanded
    {
        get => GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsExpandedProperty)
        {
            ViewModel.IsExpanded = IsExpanded;
        }
    }

    public TrackHeaderViewModel ViewModel { get; }

    public TrackHeader(TrackHeaderViewModel viewModel)
    {
        InitializeComponent();
        _mobileSettingsGesture = ServiceHub.DesktopWindowFactory == null;
        Focusable = _mobileSettingsGesture;
        if (!_mobileSettingsGesture)
        {
            ToolTip.SetTip(this, null);
            AutomationProperties.SetHelpText(this, null);
        }
        else
        {
            ToolTip.SetTip(TrackNameSettingsButton, OpenUtauMobile.Helpers.L.S("TrackSettings.HoldHint"));
            AutomationProperties.SetHelpText(TrackNameSettingsButton, OpenUtauMobile.Helpers.L.S("TrackSettings.HoldHint"));
        }
        if (!_mobileSettingsGesture) Classes.Add("DesktopTrackHeader");
        ViewModel = viewModel;
        DataContext = viewModel;

        _hideHudTimer.Interval = TimeSpan.FromMilliseconds(HudReleaseDelayMs);
        _hideHudTimer.Tick += OnHideHudTimerTick;
        _settingsHoldTimer.Interval = TimeSpan.FromMilliseconds(SettingsHoldDurationMs);
        _settingsHoldTimer.Tick += OnSettingsHoldTimerTick;
        AddHandler(PointerPressedEvent, OnSettingsPointerPressed, RoutingStrategies.Tunnel, true);
        AddHandler(PointerReleasedEvent, OnSettingsPointerReleased, RoutingStrategies.Tunnel, true);
        AddHandler(KeyDownEvent, OnSettingsKeyDown, RoutingStrategies.Tunnel, true);

        SubscribeKnobEvents(VolumeKnob);
        SubscribeKnobEvents(PanKnob);
    }

    private void OnSettingsPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_mobileSettingsGesture || _settingsGestureFired) return;
        if (_settingsGestureCancelled) return;
        if (_settingsHoldPointer != null || _settingsPointersDown.Count > 0)
        {
            _settingsPointersDown.Add(e.Pointer.Id);
            _settingsGestureCancelled = true;
            CancelSettingsHoldRecognition();
            return;
        }
        if (e.Pointer.Type == PointerType.Mouse && !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.Source is not Visual source || !source.GetSelfAndVisualAncestors().Contains(this)) return;
        if (source.GetSelfAndVisualAncestors().TakeWhile(visual => visual != this)
            .Any(visual => visual is Button button && button != TrackNameSettingsButton ||
                visual is ToggleButton or RangeBase or DawKnob or TextBox or ComboBox)) return;

        _settingsHoldIsRenameButton = source.GetSelfAndVisualAncestors().Contains(TrackNameSettingsButton);
        _settingsHoldPointer = e.Pointer;
        _settingsPointersDown.Add(e.Pointer.Id);
        _settingsHoldOrigin = e.GetPosition(this);
        _settingsHoldRoot = TopLevel.GetTopLevel(this);
        if (_settingsHoldRoot != null)
        {
            _settingsHoldRoot.AddHandler(PointerPressedEvent, OnSettingsAdditionalPointerPressed, RoutingStrategies.Tunnel, true);
            _settingsHoldRoot.AddHandler(PointerMovedEvent, OnSettingsPointerMoved, RoutingStrategies.Tunnel, true);
            _settingsHoldRoot.AddHandler(PointerReleasedEvent, OnSettingsPointerReleased, RoutingStrategies.Tunnel, true);
            _settingsHoldRoot.AddHandler(PointerCaptureLostEvent, OnSettingsPointerCaptureLost, RoutingStrategies.Tunnel, true);
        }
        _settingsHoldTimer.Start();
    }

    private void OnSettingsAdditionalPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_settingsPointersDown.Count > 0 && !_settingsPointersDown.Contains(e.Pointer.Id))
        {
            _settingsPointersDown.Add(e.Pointer.Id);
            _settingsGestureCancelled = true;
            CancelSettingsHoldRecognition();
        }
    }

    private void OnSettingsPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_settingsHoldPointer?.Id != e.Pointer.Id) return;
        Point position = e.GetPosition(this);
        Vector delta = new(position.X - _settingsHoldOrigin.X, position.Y - _settingsHoldOrigin.Y);
        if (delta.Length > SettingsHoldMovementLimit)
        {
            _settingsGestureCancelled = true;
            CancelSettingsHoldRecognition();
        }
    }

    private void OnSettingsPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        FinishSettingsPointer(e.Pointer.Id);
    }

    private void OnSettingsPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        FinishSettingsPointer(e.Pointer.Id);
    }

    private void OnSettingsHoldTimerTick(object? sender, EventArgs e)
    {
        _settingsHoldTimer.Stop();
        _settingsGestureFired = true;
        if (_settingsHoldIsRenameButton) TrackNameSettingsButton.IsEnabled = false;
        _settingsPointersDown.Clear();
        _settingsGestureCancelled = false;
        _settingsHoldPointer = null;
        DetachSettingsHoldHandlers();
        OpenSettings();
    }

    private void OnSettingsKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_mobileSettingsGesture || e.Key != Key.Apps && !(e.Key == Key.F10 && e.KeyModifiers.HasFlag(KeyModifiers.Shift))) return;
        e.Handled = true;
        OpenSettings();
    }

    private void OpenSettings()
    {
        ViewModel.ShowSettingsCommand.Execute().Subscribe(
            _ => CompleteSettingsGesture(),
            _ => CompleteSettingsGesture(),
            CompleteSettingsGesture);
    }

    private void CompleteSettingsGesture()
    {
        _settingsGestureFired = false;
        if (_settingsHoldIsRenameButton) TrackNameSettingsButton.IsEnabled = true;
        _settingsHoldIsRenameButton = false;
    }

    private void StopSettingsHold()
    {
        _settingsHoldTimer.Stop();
        _settingsHoldPointer = null;
        _settingsPointersDown.Clear();
        _settingsGestureCancelled = false;
        DetachSettingsHoldHandlers();
    }

    private void CancelSettingsHoldRecognition()
    {
        _settingsHoldTimer.Stop();
        _settingsHoldPointer = null;
        if (_settingsPointersDown.Count == 0) DetachSettingsHoldHandlers();
    }

    private void FinishSettingsPointer(int pointerId)
    {
        _settingsPointersDown.Remove(pointerId);
        if (_settingsHoldPointer?.Id == pointerId) CancelSettingsHoldRecognition();
        if (_settingsPointersDown.Count == 0)
        {
            _settingsGestureCancelled = false;
            DetachSettingsHoldHandlers();
        }
    }

    private void DetachSettingsHoldHandlers()
    {
        if (_settingsHoldRoot is not { } root) return;
        root.RemoveHandler(PointerPressedEvent, OnSettingsAdditionalPointerPressed);
        root.RemoveHandler(PointerMovedEvent, OnSettingsPointerMoved);
        root.RemoveHandler(PointerReleasedEvent, OnSettingsPointerReleased);
        root.RemoveHandler(PointerCaptureLostEvent, OnSettingsPointerCaptureLost);
        _settingsHoldRoot = null;
    }

    private void SubscribeKnobEvents(DawKnob? knob)
    {
        if (knob == null)
            return;
        knob.AdjustStarted += OnKnobAdjustStarted;
        knob.AdjustChanged += OnKnobAdjustChanged;
        knob.AdjustCompleted += OnKnobAdjustCompleted;
        knob.AdjustCancelled += OnKnobAdjustCancelled;
    }

    private void UnsubscribeKnobEvents(DawKnob? knob)
    {
        if (knob == null)
            return;
        knob.AdjustStarted -= OnKnobAdjustStarted;
        knob.AdjustChanged -= OnKnobAdjustChanged;
        knob.AdjustCompleted -= OnKnobAdjustCompleted;
        knob.AdjustCancelled -= OnKnobAdjustCancelled;
    }

    private void OnKnobAdjustStarted(object? sender, DawKnob.DawKnobAdjustEventArgs e)
    {
        if (!DocManager.Inst.HasOpenUndoGroup)
        {
            DocManager.Inst.StartUndoGroup("调整混音参数");
            _ownsMixEdit = true;
        }
        ShowHud();
        UpdateHud(e);
    }

    private void OnKnobAdjustChanged(object? sender, DawKnob.DawKnobAdjustEventArgs e)
    {
        ShowHud();
        UpdateHud(e);
    }

    private void OnKnobAdjustCompleted(object? sender, DawKnob.DawKnobAdjustEventArgs e)
    {
        EndMixEdit();
        UpdateHud(e);
        BeginHideHud();
    }

    private void OnKnobAdjustCancelled(object? sender, DawKnob.DawKnobAdjustEventArgs e)
    {
        EndMixEdit();
        ForceHideHud();
    }

    private bool _ownsMixEdit;

    private void EndMixEdit()
    {
        if (!_ownsMixEdit) return;
        _ownsMixEdit = false;
        if (DocManager.Inst.HasOpenUndoGroup) DocManager.Inst.EndUndoGroup();
    }

    private void ShowHud()
    {
        _hideHudTimer.Stop();
        if (ServiceHub.DesktopPointerDragFactory != null)
        {
            DesktopKnobReadout.IsVisible = true;
            return;
        }
        if (HudOverlay == null)
            return;
        HudOverlay.IsVisible = true;
        HudOverlay.Opacity = 1;
        BackgroundLayer.Effect = new BlurEffect
        {
            Radius = 5
        };
    }

    private void BeginHideHud()
    {
        _hideHudTimer.Stop();
        _hideHudTimer.Start();
    }

    private void OnHideHudTimerTick(object? sender, EventArgs e)
    {
        _hideHudTimer.Stop();
        ForceHideHud();
    }

    private void UpdateHud(DawKnob.DawKnobAdjustEventArgs e)
    {
        if (ServiceHub.DesktopPointerDragFactory != null)
        {
            DesktopKnobValue.Text = FormatHudValue(e);
            return;
        }
        HudValue?.Text = FormatHudValue(e);

        bool isVolume = e.Role == DawKnob.DawKnobSemanticRole.Volume;
        VolumeHudVisual?.IsVisible = isVolume;
        PanHudVisual?.IsVisible = !isVolume;

        if (isVolume)
            UpdateVolumeVisual(e);
        else
            UpdatePanVisual(e);
    }

    private void UpdateVolumeVisual(DawKnob.DawKnobAdjustEventArgs e)
    {
        if (VolumeNegFill == null || VolumePosFill == null)
            return;
        if (e.Maximum <= e.Minimum)
            return;

        double zeroNormalized = Math.Clamp((0 - e.Minimum) / (e.Maximum - e.Minimum), 0, 1);
        double zeroX = zeroNormalized * HudVolumeWidth;
        double valueX = e.Normalized * HudVolumeWidth;

        if (valueX <= zeroX)
        {
            VolumeNegFill.Width = zeroX - valueX;
            Canvas.SetLeft(VolumeNegFill, valueX);
            VolumePosFill.Width = 0;
            Canvas.SetLeft(VolumePosFill, zeroX);
        }
        else
        {
            VolumePosFill.Width = valueX - zeroX;
            Canvas.SetLeft(VolumePosFill, zeroX);
            VolumeNegFill.Width = 0;
            Canvas.SetLeft(VolumeNegFill, valueX);
        }
    }

    private void UpdatePanVisual(DawKnob.DawKnobAdjustEventArgs e)
    {
        if (PanLeftFill == null || PanRightFill == null)
            return;

        const double center = HudPanWidth / 2;
        double valueX = e.Normalized * HudPanWidth;

        if (valueX <= center)
        {
            PanLeftFill.Width = center - valueX;
            Canvas.SetLeft(PanLeftFill, valueX);
            PanRightFill.Width = 0;
            Canvas.SetLeft(PanRightFill, center);
        }
        else
        {
            PanRightFill.Width = valueX - center;
            Canvas.SetLeft(PanRightFill, center);
            PanLeftFill.Width = 0;
            Canvas.SetLeft(PanLeftFill, center);
        }
    }

    private static string FormatHudValue(DawKnob.DawKnobAdjustEventArgs e)
    {
        if (e.Role == DawKnob.DawKnobSemanticRole.Volume)
            return $"{e.Value:+0.0;-0.0;0.0} dB";

        if (Math.Abs(e.Value) < 0.5)
            return "C";
        return e.Value < 0
            ? $"L{Math.Abs(e.Value):0}"
            : $"R{e.Value:0}";
    }

    private void ForceHideHud()
    {
        _hideHudTimer.Stop();
        if (HudOverlay == null)
            return;
        DesktopKnobReadout.IsVisible = false;
        HudOverlay.Opacity = 0;
        HudOverlay.IsVisible = false;
        BackgroundLayer.Effect = null;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        StopSettingsHold();
        EndMixEdit();
        base.OnDetachedFromVisualTree(e);
        ForceHideHud();
    }

    public void Dispose()
    {
        EndMixEdit();
        _hideHudTimer.Stop();
        _hideHudTimer.Tick -= OnHideHudTimerTick;
        StopSettingsHold();
        _settingsHoldTimer.Tick -= OnSettingsHoldTimerTick;
        RemoveHandler(PointerPressedEvent, OnSettingsPointerPressed);
        RemoveHandler(PointerReleasedEvent, OnSettingsPointerReleased);
        RemoveHandler(KeyDownEvent, OnSettingsKeyDown);
        UnsubscribeKnobEvents(VolumeKnob);
        UnsubscribeKnobEvents(PanKnob);
        ViewModel.Dispose();
        GC.SuppressFinalize(this);
    }
}
