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
using DialogHostAvalonia;
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
            public ToastMessage Message { get; } = message;
            public bool Handled { get; set; }
        }

        private readonly TranslateTransform _translate = new(0, 0);
        private CancellationTokenSource? _lifetime;
        private IDisposable? _shadowBinding;
        private Window? _window;
        private Presentation? _current;
        private bool _paused;
        private bool _inactive;
        private bool _modalOpening;
        private bool _pressed;
        private int _modalGeneration;

        /// <summary>功能模态显示时暂停可见驻留并隐藏消息，保留未处理的内容。</summary>
        public bool IsPaused
        {
            get => _paused;
            set
            {
                _paused = value;
                RefreshVisibility();
            }
        }

        /// <summary>初始化共享按钮反馈与提示局部输入。</summary>
        public ToastOverlay()
        {
            InitializeComponent();
            ToastBorder.RenderTransform = _translate;
            ToastActionButton.AddHandler(PointerPressedEvent, (_, _) => _pressed = true, RoutingStrategies.Tunnel, true);
            ToastActionButton.AddHandler(PointerReleasedEvent, (_, _) => _pressed = false, RoutingStrategies.Tunnel, true);
            ToastActionButton.PointerCaptureLost += (_, _) => _pressed = false;
            ToastActionButton.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Escape || _current == null) return;
                _current.Handled = true;
                RefreshVisibility();
                e.Handled = true;
            };
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _lifetime = new CancellationTokenSource();
            _inactive = false;
            _shadowBinding = ToastBorder.GetResourceObservable("Sem.Value.Shadow").Subscribe(value =>
            {
                if (value is Color color) ToastBorder.BoxShadow = ToastTokens.Elevation(color);
            });
            _window = TopLevel.GetTopLevel(this) as Window;
            if (_window != null)
            {
                _window.Activated += WindowActivated;
                _window.Deactivated += WindowDeactivated;
            }
            PopupService.Opening += ModalOpening;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            PopupService.Opening -= ModalOpening;
            if (_window != null)
            {
                _window.Activated -= WindowActivated;
                _window.Deactivated -= WindowDeactivated;
            }
            _window = null;
            _shadowBinding?.Dispose();
            _shadowBinding = null;
            _modalGeneration++;
            _modalOpening = false;
            _pressed = false;
            _lifetime?.Cancel();
            _lifetime?.Dispose();
            _lifetime = null;
            ToastBorder.IsVisible = false;
            ToastBorder.IsHitTestVisible = false;
            base.OnDetachedFromVisualTree(e);
        }

        private void WindowActivated(object? sender, EventArgs e)
        {
            _inactive = false;
            RefreshVisibility();
        }

        private void WindowDeactivated(object? sender, EventArgs e)
        {
            _inactive = true;
            _pressed = false;
            RefreshVisibility();
        }

        /// <summary>宿主模态和局部暂停共同决定是否能展示及激活，避免按钮跨越模态。</summary>
        private bool IsBlocked => _paused || _inactive || _modalOpening || !IsEffectivelyVisible ||
            ServiceHub.DesktopWindowContext?.IsModalOpen == true || DialogHost.IsDialogOpen("MainDialogHost");

        private void RefreshVisibility()
        {
            if (ToastBorder == null) return;
            bool visible = _lifetime != null && _current is { Handled: false } && !IsBlocked;
            ToastBorder.IsVisible = visible;
            ToastBorder.IsHitTestVisible = visible && _current?.Message.Error != null;
        }

        private async void ModalOpening()
        {
            _modalOpening = true;
            RefreshVisibility();
            int generation = ++_modalGeneration;
            CancellationToken token = _lifetime?.Token ?? new CancellationToken(true);
            try
            {
                // Opening 先于实际 Show；之后以真实模态状态持续限制输入。
                await Task.Delay(100, token);
                if (generation != _modalGeneration) return;
                _modalOpening = false;
                RefreshVisibility();
            }
            catch (OperationCanceledException) { }
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
                    while (IsBlocked) await Task.Delay(50, token);
                    token.ThrowIfCancellationRequested();
                    ToastMessage? message = ToastService.Dequeue();
                    if (message == null) return;
                    owned = new Presentation(message);
                    if (message.Error == null && ServiceHub.DesktopWindowContext is { } context)
                    {
                        context.ShowMessage(message.Message, message.DurationMs);
                        owned.Handled = true;
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
                    await WaitVisibleTimeAsync(owned, token);
                    owned.Handled = true;
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

        /// <summary>仅累计可见驻留；错误消息被按住、悬停或聚焦时恢复完整阅读时间。</summary>
        private async Task WaitVisibleTimeAsync(Presentation presentation, CancellationToken token)
        {
            double elapsed = 0;
            Stopwatch clock = Stopwatch.StartNew();
            double duration = presentation.Message.DurationMs;
            while (!presentation.Handled && (duration <= 0 || elapsed < duration))
            {
                await Task.Delay(50, token);
                double interval = clock.Elapsed.TotalMilliseconds;
                clock.Restart();
                RefreshVisibility();
                if (IsBlocked) continue;
                if (presentation.Message.Error != null &&
                    (_pressed || ToastActionButton.IsPointerOver || ToastActionButton.IsKeyboardFocusWithin))
                    elapsed = 0;
                else elapsed += interval;
            }
        }

        /// <summary>一次点击只处理当前错误快照，先停止命中，再打开既有错误弹窗。</summary>
        private void OnToastClicked(object? sender, RoutedEventArgs e)
        {
            if (IsBlocked || _current is not { Handled: false } current || current.Message.Error == null) return;
            current.Handled = true;
            RefreshVisibility();
            ErrorDialogService.Show(current.Message.Error);
            e.Handled = true;
        }
    }
}