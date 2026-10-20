using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using OpenUtauMobile.Controls.Tokens;
using OpenUtauMobile.Services.Dialogs;
using OpenUtauMobile.Themes.OpenUtauMobile.Tokens.Foundation;

namespace OpenUtauMobile.Controls;

public partial class ToastOverlay : UserControl
{
    private readonly TranslateTransform _translate = new TranslateTransform(0, 0);

    /// <summary>功能模态显示时暂停普通提示的消费及驻留计时，不丢弃队列。</summary>
    public bool IsPaused { get; set; }

    public ToastOverlay()
    {
        InitializeComponent();
        ToastBorder.RenderTransform = _translate;

        // Avalonia Transitions: Opacity + RenderTransform both animated via property transitions
        ToastBorder.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = OpacityProperty,
                Duration = MotionTokens.DurationMedium1,
                Easing = new CubicEaseOut()
            },
            new TransformOperationsTransition
            {
                Property = Border.RenderTransformProperty,
                Duration = MotionTokens.DurationMedium1,
                Easing = new CubicEaseOut()
            }
        };
    }

    /// <summary>
    /// 消费循环：由 ToastService 在 UI 线程上调用。
    /// 持续从队列取消息显示，直到队列为空。
    /// </summary>
    public async Task ConsumeAsync()
    {
        while (true)
        {
            while (IsPaused) await Task.Delay(50);
            (string message, double durationMs)? item = ToastService.Dequeue();
            if (item == null)
                break;

            ToastText.Text = item.Value.message;
            await ShowAsync();
            await WaitVisibleTimeAsync(item.Value.durationMs);
            await HideAsync();

            // 两条消息之间短暂间隔，避免闪烁
            await Task.Delay(80);
        }
    }

    /// <summary>只累计可见驻留时间，告警中心退出后继续显示尚未读完的提示。</summary>
    private async Task WaitVisibleTimeAsync(double milliseconds)
    {
        double elapsed = 0;
        Stopwatch clock = Stopwatch.StartNew();
        while (elapsed < milliseconds)
        {
            await Task.Delay(50);
            double interval = clock.Elapsed.TotalMilliseconds;
            clock.Restart();
            if (!IsPaused) elapsed += interval;
        }
    }

    private async Task ShowAsync()
    {
        // Disable transitions for instant pre-position
        ToastBorder.Transitions = null;
        _translate.Y = ToastTokens.EnterOffset;
        ToastBorder.Opacity = 0;
        ToastBorder.IsVisible = true;

        // Re-enable and trigger enter transition
        ToastBorder.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = OpacityProperty,
                Duration = MotionTokens.DurationMedium1,
                Easing = new CubicEaseOut()
            }
        };

        _translate.Y = 0;
        ToastBorder.Opacity = 1;

        await Task.Delay(MotionTokens.DurationMedium1);
    }

    private async Task HideAsync()
    {
        ToastBorder.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = OpacityProperty,
                Duration = MotionTokens.DurationExit,
                Easing = new LinearEasing()
            }
        };
        ToastBorder.Opacity = 0;
        await Task.Delay(MotionTokens.DurationExit);
        ToastBorder.IsVisible = false;
    }
}
