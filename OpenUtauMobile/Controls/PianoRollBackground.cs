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

    // TODO: 未来支持按项目设置高亮1，而非C
    public static readonly StyledProperty<bool> HighlightCProperty =
        AvaloniaProperty.Register<PianoRollBackground, bool>(nameof(HighlightC), true);

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

    public bool HighlightC
    {
        get => GetValue(HighlightCProperty);
        set => SetValue(HighlightCProperty, value);
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
            change.Property == HighlightCProperty)
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
            bool isC = MusicMath.IsCenterKey(tone);

            if (!isBlack && !isC)
            {
                continue;
            }

            IBrush brush = isBlack
                ? ThemeResources.GetBrush("Sem.Color.BlackKey.Background") // 黑键
                : HighlightC && isC
                    ? ThemeResources.GetBrush("Sem.Color.CenterKey") // C
                    : ThemeResources.GetBrush("Sem.Color.WhiteKey.Background"); // 白键

            using (context.PushOpacity(isC ? 0.1 : 0.9))
            {
                context.DrawRectangle(brush, null, new Rect(0, y, width, keyHeight));
            }


            // 键分隔线：仅在白键底部绘制，减少噪声
            if (!isBlack)
            {
                double lineY = Math.Round(y + keyHeight) + 0.5;
                context.DrawLine(ThemeResources.GetPen("Sem.Color.OutlineVariant", 1), new Point(0, lineY),
                    new Point(width, lineY));
            }

            // 八度分隔：C 行上方
            if (isC)
            {
                double lineY = Math.Round(y) + 0.5;
                context.DrawLine(ThemeResources.GetPen("Sem.Color.OutlineVariant", 1.5), new Point(0, lineY),
                    new Point(width, lineY));
            }
        }
    }
}