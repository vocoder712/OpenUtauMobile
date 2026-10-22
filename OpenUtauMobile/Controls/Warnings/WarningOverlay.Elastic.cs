using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace OpenUtauMobile.Controls.Warnings
{
    public partial class WarningOverlay
    {
        /// <summary>记录触摸会话；范围内的捕获、阈值及惯性由原生识别器负责，边界外才接续。</summary>
        private void ElasticPressed(object? sender, PointerPressedEventArgs e)
        {
            if (_clearing || _closing || _elasticPointer != null || e.Pointer.Type != PointerType.Touch) return;
            _elasticPointer = e.Pointer;
            _elasticStartX = e.GetPosition(WarningItems).X;
            _elasticStart = _elasticLastPoint = e.GetPosition(WarningItems).Y;
            _elasticCaptured = false;
        }

        /// <summary>原生识别器在边界外不发出滚动事件，仅在此接续触摸，并把位移翻译为滚动量。</summary>
        private void ElasticMoved(object? sender, PointerEventArgs e)
        {
            if (e.Pointer != _elasticPointer || _clearing || _closing) return;
            if (_scroll == null) SetupScroll();
            if (_scroll == null) return;
            double point = e.GetPosition(WarningItems).Y;
            double delta = _elasticLastPoint - point;
            _elasticLastPoint = point;
            if (!_elasticCaptured)
            {
                delta = _elasticStart - point;
                // 横向卡片滑除先判方向，不能被纵向边界反馈抢走。
                if (Math.Abs(e.GetPosition(WarningItems).X - _elasticStartX) > Math.Abs(delta) * 1.5) return;
                double maxOffset = Math.Max(0, _scroll.Extent.Height - _scroll.Viewport.Height);
                bool outward = (_scroll.Offset.Y <= 0.5 && delta < 0) || (_scroll.Offset.Y >= maxOffset - 0.5 && delta > 0);
                if (!outward) { _elasticStart = point; return; }
                if (Math.Abs(delta) < 8) return;
                _elasticCaptured = true;
                e.Pointer.Capture(WarningItems);
            }
            e.PreventGestureRecognition();
            double requested = _scroll.Offset.Y - _elasticPull + delta;
            double offset = Math.Clamp(requested, 0, Math.Max(0, _scroll.Extent.Height - _scroll.Viewport.Height));
            _scroll.Offset = new Vector(_scroll.Offset.X, offset);
            SetElasticPull(offset - requested);
            e.Handled = true;
        }

        private void ElasticReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (e.Pointer != _elasticPointer) return;
            _elasticPointer = null;
            if (_elasticCaptured)
            {
                _elasticCaptured = false;
                e.Pointer.Capture(null);
                _elasticLastInput = 0;
                e.Handled = true;
            }
        }

        private void ElasticCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        {
            if (e.Pointer == _elasticPointer && e.Pointer.Captured == null)
            {
                _elasticPointer = null;
                _elasticCaptured = false;
                _elasticLastInput = 0;
            }
        }

        /// <summary>触摸已由原生识别器翻译成滚动量；和滚轮共用滚动及越界反馈，不接管鼠标拖动。</summary>
        private void ElasticGesture(object? sender, ScrollGestureEventArgs e)
        {
            if (_clearing || _closing)
            {
                e.Handled = true;
                e.ShouldEndScrollGesture = true;
                return;
            }
            if (_elasticCaptured) { e.Handled = true; return; }
            if (Math.Abs(e.Delta.Y) < 0.001) return;
            if (e.Handled) { ReturnElasticToBounds(); return; }
            ScrollWarnings(e.Delta.Y);
            e.Handled = true;
        }

        private void ElasticGestureEnded(object? sender, ScrollGestureEndedEventArgs e)
        {
            if (_elasticCaptured) return;
            _elasticPointer = null;
            _elasticLastInput = 0;
        }

        private void ElasticWheel(object? sender, PointerWheelEventArgs e)
        {
            if (_clearing || _closing) { e.Handled = true; return; }
            if (Math.Abs(e.Delta.Y) < 0.001) return;
            if (e.Handled) { ReturnElasticToBounds(); return; }
            SetupScroll();
            if (_scroll == null) return;
            ScrollWarnings(-e.Delta.Y * Math.Max(1, _scroll.SmallChange.Height));
            e.Handled = true;
        }

        /// <summary>卡片正文和列表先按原生规则滚动，仅把未消费的边界滚动量转为统一的阻尼拉伸。</summary>
        private void ScrollWarnings(double delta)
        {
            SetupScroll();
            if (_scroll == null) return;
            double maxOffset = Math.Max(0, _scroll.Extent.Height - _scroll.Viewport.Height);
            if (!((_scroll.Offset.Y <= 0.5 && delta < 0) || (_scroll.Offset.Y >= maxOffset - 0.5 && delta > 0)))
            {
                ReturnElasticToBounds();
                return;
            }

            bool continuing = _elasticPointer != null || IsRecentElasticInput;
            if (!continuing)
            {
                // 回弹途中重新输入时从当前显示位置接续，不跳回上次输入的目标。
                _elasticPull = Math.Sign(_stretch) * -140 * Math.Log(1 - Math.Min(0.999, Math.Abs(_stretch) / 64));
            }
            SetElasticPull(_elasticPull - delta);
        }

        private void SetElasticPull(double pull)
        {
            _elasticPull = Math.Clamp(pull, -600, 600);
            _elasticLastInput = Stopwatch.GetTimestamp();
            if (Math.Abs(_elasticPull) > 0.01 || Math.Abs(_stretch) > 0.01) EnsureElasticAnimation();
        }

        private void ReturnElasticToBounds()
        {
            _elasticPull = 0;
            _elasticLastInput = 0;
        }

        private bool IsRecentElasticInput => _elasticLastInput != 0 && Stopwatch.GetElapsedTime(_elasticLastInput).TotalMilliseconds < 140;

        private void EnsureElasticAnimation()
        {
            if (_elasticAnimation != null) return;
            CancellationTokenSource session = _elasticAnimation = new();
            _ = AnimateElasticAsync(session);
        }

        /// <summary>整个输入序列共用一条连续阻尼动画；滚轮每格只更新目标，不重新启动回弹。</summary>
        private async Task AnimateElasticAsync(CancellationTokenSource session)
        {
            Stopwatch clock = Stopwatch.StartNew();
            try
            {
                while (true)
                {
                    await Task.Delay(16, session.Token);
                    double dt = Math.Min(0.05, clock.Elapsed.TotalSeconds);
                    clock.Restart();
                    bool held = _elasticPointer != null || IsRecentElasticInput;
                    double target = held ? Math.Sign(_elasticPull) * 64 * (1 - Math.Exp(-Math.Abs(_elasticPull) / 140)) : 0;
                    double omega = held ? 30 : 18;
                    double displacement = _stretch - target;
                    double impulse = _elasticVelocity + omega * displacement;
                    double decay = Math.Exp(-omega * dt);
                    double next = target + (displacement + impulse * dt) * decay;
                    _elasticVelocity = (_elasticVelocity - omega * impulse * dt) * decay;
                    ApplyStretch(next);
                    if (Math.Abs(target) < 0.01 && Math.Abs(next) < 0.1 && Math.Abs(_elasticVelocity) < 1)
                    {
                        _elasticPull = _stretch = _elasticVelocity = 0;
                        ClearElasticTransforms();
                        return;
                    }
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                if (_elasticAnimation == session) Cancel(ref _elasticAnimation);
            }
        }

        /// <summary>按未变换的布局位置固定可见项和顺序，避免回弹时卡片反复退出、进入计算范围。</summary>
        private void ApplyStretch(double distance)
        {
            _stretch = distance;
            Control[] realized = WarningItems.GetRealizedContainers().ToArray();
            Control[] cards = realized.Where(card =>
            {
                double y = LayoutCardY(card);
                return y + card.Bounds.Height >= 0 && y <= WarningItems.Bounds.Height;
            }).OrderBy(LayoutCardY).ToArray();
            foreach (Control card in realized.Except(cards)) card.RenderTransform = null;
            for (int i = 0; i < cards.Length; i++)
            {
                double rank = distance >= 0 ? i : cards.Length - 1 - i;
                double gap = Math.Sign(distance) * Math.Min(10, Math.Abs(distance) / 6) * rank;
                cards[i].RenderTransform = new TranslateTransform(0, distance + gap);
            }
        }

        private double LayoutCardY(Control card) => CardY(card) - ((card.RenderTransform as TranslateTransform)?.Y ?? 0);

        private void ClearElasticTransforms()
        {
            if (!_clearing) foreach (Control card in WarningItems.GetRealizedContainers()) card.RenderTransform = null;
        }

        private void ResetElastic()
        {
            Cancel(ref _elasticAnimation);
            IPointer? pointer = _elasticPointer;
            _elasticPointer = null;
            bool captured = _elasticCaptured;
            _elasticCaptured = false;
            if (captured && pointer?.Captured == WarningItems) pointer.Capture(null);
            _elasticLastInput = 0;
            _elasticPull = _stretch = _elasticVelocity = 0;
            ClearElasticTransforms();
        }
    }
}
