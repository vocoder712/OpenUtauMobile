using System;
using OpenUtauMobile.Themes.OpenUtauMobile.Tokens.Foundation;
using System.Threading.Tasks;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using OpenUtauMobile.Services;
using OpenUtauMobile.Controls.Tokens;

namespace OpenUtauMobile.Controls;

public partial class ToastOverlay : UserControl
{
    private readonly TranslateTransform _translate = new TranslateTransform(0, 0);

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
            (string message, double durationMs)? item = ToastService.Dequeue();
            if (item == null)
                break;

            ToastText.Text = item.Value.message;
            await ShowAsync();
            await Task.Delay((int)item.Value.durationMs);
            await HideAsync();

            // 两条消息之间短暂间隔，避免闪烁
            await Task.Delay(80);
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