using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using OpenUtau.Core;
using OpenUtauMobile.Themes.OpenUtauMobile.Runtime;
using OpenUtauMobile.Themes.OpenUtauMobile.Runtime.Resources;
using OpenUtauMobile.ViewModels;
using ReactiveUI;

namespace OpenUtauMobile.Controls;

/// <summary>
/// 走带编曲区横向背景，深浅交错，用于区分轨道
/// </summary>
public class TrackBackground : Control, ICmdSubscriber
{
    private EditorViewModel? _boundViewModel;
    private IDisposable? _themeSubscription;
    private bool _isAttached;

    public static readonly StyledProperty<double> TrackHeightProperty =
        AvaloniaProperty.Register<TrackBackground, double>(nameof(TrackHeight));

    public static readonly StyledProperty<double> TrackOffsetProperty =
        AvaloniaProperty.Register<TrackBackground, double>(nameof(TrackOffset));

    public double TrackHeight
    {
        get => GetValue(TrackHeightProperty);
        set => SetValue(TrackHeightProperty, value);
    }

    public double TrackOffset
    {
        get => GetValue(TrackOffsetProperty);
        set => SetValue(TrackOffsetProperty, value);
    }

    public TrackBackground()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        UpdateViewModelSubscription();
        _themeSubscription = MessageBus.Current.Listen<ThemeChangedEvent>()
            .Subscribe(_ => InvalidateVisual());
        DocManager.Inst.AddSubscriber(this);
        InvalidateVisual();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        UpdateViewModelSubscription();
        _themeSubscription?.Dispose();
        _themeSubscription = null;
        DocManager.Inst.RemoveSubscriber(this);
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        UpdateViewModelSubscription();
        InvalidateVisual();
    }

    /// <summary>
    /// 仅在画布挂载时订阅当前编辑器，切换上下文或卸载时解除旧订阅。
    /// </summary>
    private void UpdateViewModelSubscription()
    {
        if (_boundViewModel != null)
        {
            _boundViewModel.RequestInvalidateVisual -= InvalidateVisual;
        }

        _boundViewModel = _isAttached ? DataContext as EditorViewModel : null;
        if (_boundViewModel != null)
        {
            _boundViewModel.RequestInvalidateVisual += InvalidateVisual;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TrackHeightProperty ||
            change.Property == TrackOffsetProperty)
        {
            InvalidateVisual();
        }
    }

    public void OnNext(UCommand cmd, bool isUndo)
    {
        switch (cmd)
        {
            case TrackCommand:
                InvalidateVisual();
                break;
        }
    }

    public override void Render(DrawingContext context)
    {
        int trackCount = DocManager.Inst.Project.tracks.Count;

        double trackHeight = TrackHeight;
        double trackOffset = TrackOffset;

        if (trackHeight <= 0) return;

        double width = Bounds.Width;
        double height = Bounds.Height;

        int firstIdx = (int)Math.Floor(trackOffset / trackHeight);
        int lastIdx = Math.Min((int)Math.Ceiling((trackOffset + height) / trackHeight),
            trackCount);

        // Ensure we handle negative offsets gracefully if they occur (though trackOffset usually >= 0)
        if (firstIdx < 0) firstIdx = 0;

        IPen pen = ThemeResources.GetPen("Sem.Color.Primary", 1.5);

        for (int i = firstIdx; i <= lastIdx; i++)
        {
            if (i == 0) continue;
            double y = Math.Round(i * trackHeight - trackOffset) + 0.5;
            context.DrawLine(pen, new Point(0, y), new Point(width, y));
        }
    }
}