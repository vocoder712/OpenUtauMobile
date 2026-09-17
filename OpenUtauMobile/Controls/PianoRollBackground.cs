using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using OpenUtau.Core;
using OpenUtauMobile.Themes.OpenUtauMobile.Runtime;
using ReactiveUI;

namespace OpenUtauMobile.Controls;

/// <summary>
/// 钢琴卷帘背景：按可见音高绘制黑/白键条带，用于区分键色。
/// 不接收输入事件。
/// </summary>
public class PianoRollBackground : Control
{
    public static readonly StyledProperty<double> KeyHeightProperty =
        AvaloniaProperty.Register<PianoRollBackground, double>(nameof(KeyHeight));

    public static readonly StyledProperty<double> KeyOffsetProperty =
        AvaloniaProperty.Register<PianoRollBackground, double>(nameof(KeyOffset));

    public static readonly StyledProperty<int> ProjectKeyProperty =
        AvaloniaProperty.Register<PianoRollBackground, int>(nameof(ProjectKey));

    public double KeyHeight
    {
        get => GetValue(KeyHeightProperty);
        set => SetValue(KeyHeightProperty, value);
    }

    public double KeyOffset
    {
        get => GetValue(KeyOffsetProperty);
        set => SetValue(KeyOffsetProperty, value);
    }

    public int ProjectKey
    {
        get => GetValue(ProjectKeyProperty);
        set => SetValue(ProjectKeyProperty, value);
    }

    public PianoRollBackground()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;

        MessageBus.Current.Listen<ThemeChangedEvent>()
            .Subscribe(_ => InvalidateVisual());
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == KeyHeightProperty ||
            change.Property == KeyOffsetProperty ||
            change.Property == ProjectKeyProperty)
        {
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        double height = Bounds.Height;
        double width = Bounds.Width;
        double keyHeight = KeyHeight;

        context.DrawRectangle(ThemeResources.GetBrush("Sem.Color.WhiteKey.Background"), null,
            new Rect(0, 0, width, height)); // 先绘制白键

        if (height <= 0 || width <= 0 || keyHeight <= 0)
        {
            return;
        }

        int toneMin = Math.Max(0, (int)Math.Floor(ViewConstants.MaxTone - 1 - height / keyHeight - KeyOffset));
        int toneMax = Math.Min(ViewConstants.MaxTone - 1, (int)Math.Ceiling(ViewConstants.MaxTone - 1 - KeyOffset));

        for (int tone = toneMax; tone >= toneMin; tone--)
        {
            double y = (ViewConstants.MaxTone - 1 - tone - KeyOffset) * keyHeight;
            bool isBlack = MusicMath.IsBlackKey(tone);
            bool isTonic = PianoKeyLabelFormatter.NormalizePitchClass(tone) ==
                PianoKeyLabelFormatter.NormalizePitchClass(ProjectKey);

            // 背景填充与分隔线独立处理，避免普通白键提前跳过其边界。
            if (isBlack || isTonic)
            {
                IBrush brush = isTonic
                    ? ThemeResources.GetBrush("Sem.Color.CenterKey") // 工程主音
                    : ThemeResources.GetBrush("Sem.Color.BlackKey.Background"); // 黑键

                using (context.PushOpacity(isTonic ? 0.5 : 1))
                {
                    context.DrawRectangle(brush, null, new Rect(0, y, width, keyHeight));
                }
            }

            // 仅绘制 B–C 和 E–F 两类相邻白键边界。
            bool hasWhiteKeyBelow = tone > 0 && !isBlack && !MusicMath.IsBlackKey(tone - 1);
            if (hasWhiteKeyBelow)
            {
                double lineY = Math.Round(y + keyHeight) + 0.5;
                context.DrawLine(ThemeResources.GetPen("Sem.Color.OutlineVariant"), new Point(0, lineY),
                    new Point(width, lineY));
            }
        }
    }
}
