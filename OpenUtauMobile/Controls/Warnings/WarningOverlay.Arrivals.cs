using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using OpenUtauMobile.Services.Warnings;
using Serilog;

namespace OpenUtauMobile.Controls.Warnings
{
    public partial class WarningOverlay
    {
        private const int ArrivalDwellDuration = 4000;
        private long _arrivalDeadline;

        /// <summary>点击当前顶部卡片后重新给予完整阅读时间，不重建展示或动画会话。</summary>
        private void ResetArrivalCountdown(object? sender, PointerPressedEventArgs e)
        {
            if (_arriving == null || !ArrivalCard.IsVisible || !ArrivalCard.IsHitTestVisible) return;
            _arrivalDeadline = Environment.TickCount64 + ArrivalDwellDuration;
            ArrivalCard.RemainingFraction = 1;
        }

        private void StartNextArrival()
        {
            if (!_attached || !_presentationReady || IsCenterOpen || _arrivalAnimation != null || _arriving != null || _inbox == null || Bounds.Width < 1 || Bounds.Height < 1) return;
            foreach (WarningItem item in _inbox.Items)
            {
                if (item.Presented) continue;
                _arriving = item;
                _ = PresentArrivalAsync(item);
                return;
            }
        }

        private async Task PresentArrivalAsync(WarningItem item)
        {
            Cancel(ref _arrivalAnimation);
            CancellationTokenSource session = _arrivalAnimation = new();
            CancellationToken token = session.Token;
            const int enterDuration = 230;
            _arrivalDeadline = Environment.TickCount64 + enterDuration + ArrivalDwellDuration;
            bool completed = false;
            try
            {
                ArrivalCard.DataContext = item;
                ArrivalCard.RemainingFraction = 1;
                ArrivalCard.IsVisible = true;
                ArrivalCard.IsHitTestVisible = true;
                ArrivalCard.Opacity = 0;
                TranslateTransform enter = new(0, -24);
                ArrivalCard.RenderTransform = enter;
                AutomationProperties.SetLiveSetting(ArrivalCard, AutomationLiveSetting.Polite);
                AutomationProperties.SetName(ArrivalCard, item.Title);
                await WarningMotion.Run(enterDuration, t =>
                {
                    ArrivalCard.Opacity = WarningMotion.Ease(t);
                    enter.Y = -24 * (1 - WarningMotion.Ease(t));
                }, token);
                // 完成驻留和汇入后才算已展示，中断、宿主重建和布局变化不能吞掉提示。
                // 同键内容更新不重建任务，不重置已经过去的驻留时间。
                // 只有点击卡片才延长截止时间；焦点、后台及其他浮层不改变计时。
                while (true)
                {
                    long remaining = _arrivalDeadline - Environment.TickCount64;
                    if (remaining <= 0) break;
                    await WarningMotion.Run((int)remaining, _ => ArrivalCard.RemainingFraction =
                        Math.Clamp((double)(_arrivalDeadline - Environment.TickCount64) / ArrivalDwellDuration, 0, 1), token);
                }
                token.ThrowIfCancellationRequested();
                ArrivalCard.RemainingFraction = 0;
                ArrivalCard.IsHitTestVisible = false;
                await MergeArrivalAsync(token);
                token.ThrowIfCancellationRequested();
                item.Presented = true;
                completed = true;
                _dockedVisible = true;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log.Warning(ex, "告警顶部卡片动画不可用");
                item.Presented = true;
                _dockedVisible = true;
                completed = true;
            }
            finally
            {
                if (_arrivalAnimation == session)
                {
                    StopArrival();
                    UpdateOrb();
                    if (completed) StartNextArrival();
                }
            }
        }

        /// <summary>同一卡片外轮廓连续缩为圆再贴边，正文先淡出，图标不缩放。</summary>
        private async Task MergeArrivalAsync(CancellationToken token)
        {
            Point from = new(Canvas.GetLeft(ArrivalCard), Canvas.GetTop(ArrivalCard));
            double width = ArrivalCard.Bounds.Width;
            double height = ArrivalCard.Bounds.Height;
            Color sourceColor = (ArrivalCard.SurfaceBackground as ISolidColorBrush)?.Color ?? default;
            Color targetColor = (Orb.Background as ISolidColorBrush)?.Color ?? sourceColor;
            Color sourceForeground = (ArrivalCard.FindControl<TextBlock>("TitleText")?.Foreground as ISolidColorBrush)?.Color ?? default;
            Color targetForeground = (Orb.Foreground as ISolidColorBrush)?.Color ?? sourceForeground;
            Flight.IsVisible = true;
            Flight.Opacity = 0;
            Flight.GlyphOpacity = 0;
            Flight.Dock = 0;
            Flight.Roundness = 0;
            await WarningMotion.Run(600, t =>
            {
                // 已有球在飞行最后一段交给同一图标，避免长时间重叠两枚图标。
                Orb.Opacity = 1 - Math.Clamp((t - 0.75) / 0.2, 0, 1);
                Orb.IsHitTestVisible = Orb.Opacity > 0.05;
                double position = WarningMotion.Ease(t);
                Point target = DockPoint();
                Flight.Width = WarningMotion.Lerp(width, 56, position);
                Flight.Background = new SolidColorBrush(WarningMotion.MixColor(sourceColor, targetColor, position));
                Flight.Foreground = new SolidColorBrush(WarningMotion.MixColor(sourceForeground, targetForeground, position));
                Flight.Height = WarningMotion.Lerp(height, 56, position);
                Canvas.SetLeft(Flight, WarningMotion.Lerp(from.X, target.X, position));
                Canvas.SetTop(Flight, WarningMotion.Lerp(from.Y, target.Y, position));
                Flight.Roundness = Math.Min(1, t * 2);
                Flight.Dock = (_right ? 1 : -1) * WarningMotion.Ease(Math.Clamp((t - 0.72) / 0.28, 0, 1));
                Flight.GlyphOpacity = Math.Clamp((t - 0.15) / 0.25, 0, 1);
                Flight.Opacity = Math.Min(1, t / 0.18);
                ArrivalCard.Opacity = 1 - Math.Min(1, t / 0.18);
            }, token);
        }

        private void StopArrival()
        {
            Cancel(ref _arrivalAnimation);
            _arriving = null;
            ArrivalCard.IsVisible = false;
            ArrivalCard.RemainingFraction = 1;
            ArrivalCard.IsHitTestVisible = true;
            ArrivalCard.RenderTransform = null;
            ArrivalCard.DataContext = null;
            Flight.IsVisible = false;
            Orb.Opacity = 1;
            Orb.IsHitTestVisible = true;
        }

        /// <summary>立即收起当前顶部卡片，保留告警并恢复球入口，队列继续处理下一条。</summary>
        private void CloseArrival(WarningItem item)
        {
            if (_arriving != item) return;
            bool restoreFocus = ArrivalCard.IsKeyboardFocusWithin;
            item.Presented = true;
            _dockedVisible = true;
            StopArrival();
            UpdateOrb();
            if (restoreFocus && Orb.IsVisible) Orb.Focus();
            StartNextArrival();
        }

        private async void IgnoreArrival(WarningItem item)
        {
            if (_arriving != item || _inbox == null) return;
            // 先从当前动画会话退出，忽略只清这一代；业务变化不会截断退出帧。
            Cancel(ref _arrivalAnimation);
            _arriving = null;
            CancellationTokenSource session = _arrivalAnimation = new();
            ArrivalCard.IsHitTestVisible = false;
            _inbox.Dismiss(item);
            try
            {
                await WarningMotion.Run(150, t => ArrivalCard.Opacity = 1 - t, session.Token);
            }
            catch (OperationCanceledException) { }
            finally
            {
                if (_arrivalAnimation == session)
                {
                    StopArrival();
                    UpdateOrb();
                    StartNextArrival();
                }
            }
        }
    }
}
