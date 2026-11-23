using System;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using OpenUtauMobile.Controls.Tokens;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services;
using OpenUtauMobile.Services.Dialogs;
using OpenUtauMobile.Themes.OpenUtauMobile.Tokens.Foundation;

namespace OpenUtauMobile.Controls
{
    /// <summary>普通提示和可点击错误提示的局部浮层，不在提示之外拦截编辑输入。</summary>
    public partial class ToastOverlay : UserControl
    {
        /// <summary>单次展示独立保存处理状态，旧宿主的取消不能清除新展示。</summary>
        private sealed class Presentation(ToastMessage message)
        {
            private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly long _startedAt = Stopwatch.GetTimestamp();

            /// <summary>当前展示持有的消息快照。</summary>
            public ToastMessage Message { get; } = message;
            /// <summary>到期、点击或关闭后不再归还消息。</summary>
            public bool Handled => _completed.Task.IsCompleted;
            /// <summary>无需轮询的用户处理信号。</summary>
            public Task Completion => _completed.Task;
            /// <summary>入场与驻留共用截止时间，后台延迟恢复不会重新开始完整驻留。</summary>
            public TimeSpan RemainingTime => MotionTokens.DurationMedium1 + TimeSpan.FromMilliseconds(Message.DurationMs) -
                Stopwatch.GetElapsedTime(_startedAt);

            /// <summary>点击、关闭和到期共用同一个完成信号。</summary>
            public void Complete() => _completed.TrySetResult();
        }

        private readonly TranslateTransform _translate = new(0, 0);
        private CancellationTokenSource? _lifetime;
        private IDisposable? _shadowBinding;
        private Presentation? _current;

        /// <summary>初始化共享按钮反馈与提示局部输入。</summary>
        public ToastOverlay()
        {
            InitializeComponent();
            ToastBorder.RenderTransform = _translate;
            ToastActionButton.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Escape || _current == null) return;
                _current.Complete();
                RefreshVisibility();
                e.Handled = true;
            };
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _lifetime = new CancellationTokenSource();
            _shadowBinding = ToastBorder.GetResourceObservable("Sem.Value.Shadow").Subscribe(value =>
            {
                if (value is Color color) ToastBorder.BoxShadow = ToastTokens.Elevation(color);
            });
            ToastService.Register(ConsumeAsync);
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            ToastService.Unregister();
            _shadowBinding?.Dispose();
            _shadowBinding = null;
            _lifetime?.Cancel();
            _lifetime?.Dispose();
            _lifetime = null;
            ToastBorder.IsVisible = false;
            ToastBorder.IsHitTestVisible = false;
            base.OnDetachedFromVisualTree(e);
        }

        /// <summary>提示只由自身展示会话控制，不依赖弹窗、告警中心或窗口激活状态。</summary>
        private void RefreshVisibility()
        {
            if (ToastBorder == null) return;
            bool visible = _lifetime != null && _current is { Handled: false };
            ToastBorder.IsVisible = visible;
            ToastBorder.IsHitTestVisible = visible && _current?.Message.Error != null;
        }

        /// <summary>顺序消费；卸载时归还未处理消息，重建宿主不会丢失错误详情。</summary>
        public async Task ConsumeAsync()
        {
            if (_lifetime == null) return;
            CancellationToken token = _lifetime.Token;
            Presentation? owned = null;
            try
            {
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    ToastMessage? message = ToastService.Dequeue();
                    if (message == null) return;
                    owned = new Presentation(message);
                    if (message.Error == null && ServiceHub.DesktopWindowContext is { } context)
                    {
                        context.ShowMessage(message.Message, message.DurationMs);
                        owned.Complete();
                        owned = null;
                        continue;
                    }
                    _current = owned;
                    ToastText.Text = message.Message;
                    bool interactive = message.Error != null;
                    ToastActionIcon.IsVisible = interactive;
                    ToastActionButton.IsVisible = interactive;
                    AutomationProperties.SetName(ToastActionButton, message.Message + ". " + L.S("Common.Details"));
                    AutomationProperties.SetLiveSetting(ToastText, AutomationLiveSetting.Polite);
                    ToolTip.SetTip(ToastActionButton, message.Message + "\n" + L.S("Common.Details"));
                    ToastBorder.Transitions = null;
                    ToastBorder.Opacity = 0;
                    _translate.Y = ToastTokens.EnterOffset;
                    RefreshVisibility();
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
                    await Task.Delay(MotionTokens.DurationMedium1, token);
                    token.ThrowIfCancellationRequested();
                    await WaitForCompletionAsync(owned, token);
                    token.ThrowIfCancellationRequested();
                    owned.Complete();
                    ToastBorder.IsHitTestVisible = false;
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
                    await Task.Delay(MotionTokens.DurationExit, token);
                    token.ThrowIfCancellationRequested();
                    if (ReferenceEquals(_current, owned))
                    {
                        _current = null;
                        ToastBorder.IsVisible = false;
                    }
                    owned = null;
                    await Task.Delay(80, token);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            finally
            {
                if (owned != null)
                {
                    if (!owned.Handled) ToastService.Return(owned.Message);
                    if (ReferenceEquals(_current, owned))
                    {
                        _current = null;
                        ToastBorder.IsVisible = false;
                        ToastBorder.IsHitTestVisible = false;
                    }
                }
            }
        }

        /// <summary>只等待一次到期或用户处理信号，失焦、后台和其他浮层均不暂停计时。</summary>
        private static async Task WaitForCompletionAsync(Presentation presentation, CancellationToken token)
        {
            double duration = presentation.Message.DurationMs;
            if (duration <= 0)
            {
                await presentation.Completion.WaitAsync(token);
                return;
            }
            TimeSpan remaining = presentation.RemainingTime;
            if (remaining <= TimeSpan.Zero) return;
            try { await presentation.Completion.WaitAsync(remaining, token); }
            catch (TimeoutException) { }
        }

        /// <summary>一次点击只处理当前错误快照，先停止命中，再打开既有错误弹窗。</summary>
        private void OnToastClicked(object? sender, RoutedEventArgs e)
        {
            if (_current is not { Handled: false } current || current.Message.Error == null) return;
            current.Complete();
            RefreshVisibility();
            ErrorDialogService.Show(current.Message.Error);
            e.Handled = true;
        }
    }
}
