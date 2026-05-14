using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using OpenUtau.Core;
using OpenUtauMobile.Themes.OpenUtauMobile.Runtime;
using ReactiveUI;

namespace OpenUtauMobile.Controls;

/// <summary>
/// 走带编曲区横向背景，深浅交错，用于区分轨道
/// </summary>
public class TrackBackground : Control, ICmdSubscriber
{
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

        MessageBus.Current.Listen<ThemeChangedEvent>()
            .Subscribe(_ => InvalidateVisual());
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        DocManager.Inst.AddSubscriber(this);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        DocManager.Inst.RemoveSubscriber(this);
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