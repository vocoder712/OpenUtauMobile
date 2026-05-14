using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
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

    private readonly DispatcherTimer _hideHudTimer = new();

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
        ViewModel = viewModel;
        DataContext = viewModel;

        _hideHudTimer.Interval = TimeSpan.FromMilliseconds(HudReleaseDelayMs);
        _hideHudTimer.Tick += OnHideHudTimerTick;

        SubscribeKnobEvents(VolumeKnob);
        SubscribeKnobEvents(PanKnob);
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
        UpdateHud(e);
        BeginHideHud();
    }

    private void OnKnobAdjustCancelled(object? sender, DawKnob.DawKnobAdjustEventArgs e)
    {
        ForceHideHud();
    }

    private void ShowHud()
    {
        _hideHudTimer.Stop();
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
        HudOverlay.Opacity = 0;
        HudOverlay.IsVisible = false;
        BackgroundLayer.Effect = null;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        ForceHideHud();
    }

    public void Dispose()
    {
        _hideHudTimer.Stop();
        _hideHudTimer.Tick -= OnHideHudTimerTick;
        UnsubscribeKnobEvents(VolumeKnob);
        UnsubscribeKnobEvents(PanKnob);
        ViewModel.Dispose();
        GC.SuppressFinalize(this);
    }
}