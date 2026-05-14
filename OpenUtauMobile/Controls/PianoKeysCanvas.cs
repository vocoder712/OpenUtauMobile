using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using OpenUtau.Core;
using OpenUtauMobile.Audio;
using OpenUtauMobile.Themes.OpenUtauMobile.Runtime;

namespace OpenUtauMobile.Controls;

/// <summary>
/// 钢琴卷帘左侧琴键画布。
/// 与 NotesCanvas 共享 KeyHeight / KeyOffset，Y 轴严格对齐。
/// 多指触控：每个手指独立跟踪，可同时按下多个琴键发声。
/// 每个手指按下发声，滑动换键，松开停声，互不干扰。
/// 音频调用直接调用 PlaybackManager.PlayTone / EndTone。
/// </summary>
public class PianoKeysCanvas : Control
{
    #region 依赖属性

    public static readonly StyledProperty<double> KeyHeightProperty =
        AvaloniaProperty.Register<PianoKeysCanvas, double>(nameof(KeyHeight), 20.0);

    public static readonly StyledProperty<double> KeyOffsetProperty =
        AvaloniaProperty.Register<PianoKeysCanvas, double>(nameof(KeyOffset), 70.0);

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

    #endregion

    #region 内部状态

    /// <summary>
    /// 每个手指独立跟踪，支持同时按下多个琴键。
    /// </summary>
    private readonly Dictionary<int, int> _activePointers = [];

    /// <summary>
    /// 当前所有激活的音高集合（用于渲染高亮）。
    /// 由于多个手指可能按同一个键，这里存储的是去重后的音高。
    /// </summary>
    private readonly HashSet<int> _activeTones = [];

    #endregion

    #region 属性变更

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == KeyHeightProperty ||
            change.Property == KeyOffsetProperty)
        {
            InvalidateVisual();
        }
    }

    #endregion

    #region 坐标辅助

    /// <summary>
    /// 屏幕 Y → 整数音高
    /// </summary>
    private int PointYToToneInt(double y)
        => (int)Math.Floor(ViewConstants.MaxTone - y / KeyHeight - KeyOffset);

    #endregion

    #region 触控输入
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        int pointerId = e.Pointer.Id;

        // 如果该指针已经在跟踪中，忽略（不应发生，但防御性处理）
        if (_activePointers.ContainsKey(pointerId)) return;

        int tone = Math.Clamp(PointYToToneInt(e.GetPosition(this).Y), 0, ViewConstants.MaxTone - 1);

        // 捕获该指针
        e.Pointer.Capture(this);

        // 记录指针状态
        _activePointers[pointerId] = tone;

        // 如果该音高之前没有被按下，开始发声
        if (_activeTones.Add(tone))
        {
            InvalidateVisual();
            TonePlayer.PlayNoteOn(tone);
        }

        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        int pointerId = e.Pointer.Id;

        // 只处理已跟踪的指针
        if (!_activePointers.TryGetValue(pointerId, out int oldTone)) return;

        int newTone = Math.Clamp(PointYToToneInt(e.GetPosition(this).Y), 0, ViewConstants.MaxTone - 1);
        if (newTone == oldTone) return;

        // 更新指针对应的音高
        _activePointers[pointerId] = newTone;

        // 检查旧音高是否还有其他手指按着
        bool oldToneStillActive = false;
        foreach (KeyValuePair<int, int> kvp in _activePointers)
        {
            if (kvp.Key != pointerId && kvp.Value == oldTone)
            {
                oldToneStillActive = true;
                break;
            }
        }

        // 如果旧音高没有其他手指按着，停止发声
        if (!oldToneStillActive && _activeTones.Contains(oldTone))
        {
            _activeTones.Remove(oldTone);
            InvalidateVisual();
            TonePlayer.PlayNoteOff(oldTone);
        }

        // 如果新音高之前没有被按下，开始发声
        if (_activeTones.Add(newTone))
        {
            InvalidateVisual();
            TonePlayer.PlayNoteOn(newTone);
        }

        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        ReleasePointer(e.Pointer);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        ReleasePointer(e.Pointer);
    }

    /// <summary>
    /// 释放指定指针，停止其对应音高的发声（如果没有其他手指按着）。
    /// </summary>
    private void ReleasePointer(IPointer pointer)
    {
        int pointerId = pointer.Id;

        // 只处理已跟踪的指针
        if (!_activePointers.Remove(pointerId, out int tone)) return;

        // 移除指针跟踪
        pointer.Capture(null);

        // 检查该音高是否还有其他手指按着
        bool toneStillActive = false;
        foreach (KeyValuePair<int, int> kvp in _activePointers)
        {
            if (kvp.Value == tone)
            {
                toneStillActive = true;
                break;
            }
        }

        // 如果该音高没有其他手指按着，停止发声
        if (!toneStillActive && _activeTones.Contains(tone))
        {
            _activeTones.Remove(tone);
            InvalidateVisual();
            TonePlayer.PlayNoteOff(tone);
        }
    }

    #endregion

    #region 渲染

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        double h = Bounds.Height;
        double w = Bounds.Width;
        if (h <= 0 || w <= 0 || KeyHeight <= 0) return;

        // 透明背景——确保命中测试生效
        context.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));

        // 可见音高范围
        int toneMin = Math.Max(0,
            (int)Math.Floor(ViewConstants.MaxTone - 1 - h / KeyHeight - KeyOffset));
        int toneMax = Math.Min(ViewConstants.MaxTone - 1,
            (int)Math.Ceiling(ViewConstants.MaxTone - 1 - KeyOffset));

        for (int tone = toneMax; tone >= toneMin; tone--)
        {
            double y = (ViewConstants.MaxTone - 1 - tone - KeyOffset) * KeyHeight;
            bool isActive = _activeTones.Contains(tone);
            bool isBlack = MusicMath.IsBlackKey(tone);
            bool isC = MusicMath.IsCenterKey(tone); // tone % 12 == 0

            IBrush brush;

            if (isActive) // 按下
            {
                brush = ThemeResources.GetBrush("Sem.Color.Primary");
            }
            else if (isBlack) // 黑键
            {
                brush = ThemeResources.GetBrush("Sem.Color.BlackKey");
            }
            else // 白键
            {
                brush = ThemeResources.GetBrush(isC ? "Sem.Color.CenterKey" : "Sem.Color.WhiteKey");
            }

            context.DrawRectangle(brush, null, new Rect(0, y, w, KeyHeight));

            // 分隔线：仅白键底部（避免黑键区域视觉噪声）
            if (!isBlack)
            {
                context.DrawLine(ThemeResources.GetPen("Sem.Color.OutlineVariant", 1),
                    new Point(0, y + KeyHeight),
                    new Point(w, y + KeyHeight));
            }

            // C 键音名标注：KeyHeight >= NoteHeightMin(8) 时才绘制
            if (isC && !isActive && KeyHeight >= ViewConstants.NoteHeightMin)
            {
                string name = MusicMath.GetToneName(tone); // 如 "C4"
                TextLayout text = TextLayoutCache.Get(name, ThemeResources.GetBrush("Sem.Color.Outline"), 10);
                double textY = y + (KeyHeight - text.Height) / 2;
                double textX = w - text.Width - 2;
                if (textX >= 0)
                {
                    text.Draw(context, new Point(textX, textY));
                }
            }
        }
    }

    #endregion
}