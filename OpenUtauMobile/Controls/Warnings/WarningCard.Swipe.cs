using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using OpenUtauMobile.Services.Warnings;

namespace OpenUtauMobile.Controls.Warnings
{
    public partial class WarningCard
    {
        private IPointer? _swipePointer;
        private Point _swipeOrigin;
        private double _swipeX;
        private double _swipeVelocity;
        private long _swipeLastMove;
        private bool _swiping;
        private bool _dismissing;
        private CancellationTokenSource? _swipeAnimation;

        private void InitializeSwipe()
        {
            AddHandler(PointerPressedEvent, SwipePressed, RoutingStrategies.Tunnel, true);
            AddHandler(PointerMovedEvent, SwipeMoved, RoutingStrategies.Tunnel, true);
            AddHandler(PointerReleasedEvent, SwipeReleased, RoutingStrategies.Tunnel, true);
            AddHandler(PointerCaptureLostEvent, SwipeCaptureLost, RoutingStrategies.Bubble, true);
            AddHandler(KeyDownEvent, SwipeKeyDown, RoutingStrategies.Tunnel);
            DataContextChanged += (_, _) => CancelSwipe();
            SizeChanged += (_, _) => CancelSwipe();
        }

        private void SwipePressed(object? sender, PointerPressedEventArgs e)
        {
            if (!CanSwipeDismiss || !IsEffectivelyEnabled || _dismissing || _swipePointer != null ||
                (e.Pointer.Type == PointerType.Mouse && !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)) return;
            StopSwipeAnimation();
            _swipePointer = e.Pointer;
            _swipeOrigin = e.GetPosition(this) - new Vector(_swipeX, 0);
            _swipeLastMove = Stopwatch.GetTimestamp();
            _swipeVelocity = 0;
        }

        /// <summary>先锁定水平意图，再独占该指针；垂直手势留给列表和卡片正文滚动。</summary>
        private void SwipeMoved(object? sender, PointerEventArgs e)
        {
            if (e.Pointer != _swipePointer || _dismissing) return;
            Vector delta = e.GetPosition(this) - _swipeOrigin;
            if (!_swiping)
            {
                if (Math.Abs(delta.Y) > 8 && Math.Abs(delta.X) < Math.Abs(delta.Y) * 1.5)
                {
                    CancelSwipe();
                    return;
                }
                if (Math.Abs(delta.X) < 12 || Math.Abs(delta.X) < Math.Abs(delta.Y) * 1.5) return;
                _swiping = true;
                SwipeStarted?.Invoke(this, EventArgs.Empty);
                e.Pointer.Capture(this);
                UpdateSwipeElevation();
            }
            e.PreventGestureRecognition();
            double interval = Math.Max(0.001, Stopwatch.GetElapsedTime(_swipeLastMove).TotalSeconds);
            _swipeVelocity = (delta.X - _swipeX) / interval;
            _swipeLastMove = Stopwatch.GetTimestamp();
            SetSwipeOffset(delta.X);
            e.Handled = true;
        }

        private void SwipeReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (e.Pointer != _swipePointer) return;
            _swipePointer = null;
            if (!_swiping)
            {
                if (_swipeX != 0) _ = AnimateSwipeAsync(false);
                return;
            }
            _swiping = false;
            e.Pointer.Capture(null);
            double threshold = Math.Clamp(Bounds.Width * 0.35, 64, 160);
            bool fling = Math.Abs(_swipeX) >= 32 && Math.Abs(_swipeVelocity) > 900 &&
                Math.Sign(_swipeVelocity) == Math.Sign(_swipeX) && Stopwatch.GetElapsedTime(_swipeLastMove).TotalMilliseconds < 120;
            if (Math.Abs(_swipeX) >= threshold || fling) RequestDismiss();
            else _ = AnimateSwipeAsync(false);
            e.Handled = true;
        }

        private void SwipeCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        {
            if (e.Pointer == _swipePointer && e.Pointer.Captured != this) CancelSwipe();
        }

        private void SwipeKeyDown(object? sender, KeyEventArgs e)
        {
            if (CanSwipeDismiss && e.Key == Key.Delete && e.KeyModifiers == KeyModifiers.None)
            {
                RequestDismiss();
                e.Handled = true;
            }
        }

        /// <summary>滑动、键盘和上下文菜单共用单条删除动画，结束后才提交当前代次的清除。</summary>
        public void RequestDismiss()
        {
            if (!CanSwipeDismiss || !IsEffectivelyEnabled || _dismissing || DataContext is not WarningItem) return;
            _ = AnimateSwipeAsync(true);
        }

        private async Task AnimateSwipeAsync(bool dismiss)
        {
            StopSwipeAnimation();
            CancellationTokenSource session = _swipeAnimation = new();
            WarningItem? item = DataContext as WarningItem;
            _dismissing = dismiss;
            CardSurface.IsHitTestVisible = !dismiss;
            UpdateSwipeElevation();
            double from = _swipeX;
            double target = dismiss ? (from < 0 ? -1 : 1) * (Bounds.Width + 32) : 0;
            try
            {
                await WarningMotion.Run(dismiss ? 220 : 240, t => SetSwipeOffset(WarningMotion.Lerp(from, target,
                    dismiss ? WarningMotion.Ease(t) : Math.Clamp(WarningMotion.Spring(t), 0, 1))), session.Token);
                if (dismiss && item != null && ReferenceEquals(DataContext, item)) DismissRequested?.Invoke(this, item);
            }
            catch (OperationCanceledException) { }
            finally
            {
                if (_swipeAnimation == session)
                {
                    StopSwipeAnimation();
                    _dismissing = false;
                    CardSurface.IsHitTestVisible = true;
                    SetSwipeOffset(0);
                    UpdateSwipeElevation();
                }
            }
        }

        /// <summary>横向位移只作用于卡片表面，与列表弹性的纵向容器变换彼此独立。</summary>
        private void SetSwipeOffset(double x)
        {
            _swipeX = x;
            CardSurface.RenderTransform = x == 0 ? null : new TranslateTransform(x, 0);
            CardSurface.Opacity = 1 - 0.7 * Math.Clamp(Math.Abs(x) / Math.Max(1, Bounds.Width), 0, 1);
        }

        private void UpdateSwipeElevation() => CardSurface.BoxShadow = _swiping || _dismissing
            ? WarningElevation.Level3(_shadowColor) : default;

        private void StopSwipeAnimation()
        {
            CancellationTokenSource? old = _swipeAnimation;
            _swipeAnimation = null;
            old?.Cancel();
            old?.Dispose();
        }

        /// <summary>布局、复用、关闭和后台切换时取消手势，不清除告警，不让旧动画删除新条目。</summary>
        public void CancelSwipe()
        {
            StopSwipeAnimation();
            IPointer? pointer = _swipePointer;
            _swipePointer = null;
            _swiping = _dismissing = false;
            if (pointer?.Captured == this) pointer.Capture(null);
            ContextMenu?.Close();
            CardSurface.IsHitTestVisible = true;
            SetSwipeOffset(0);
            UpdateSwipeElevation();
        }
    }
}
