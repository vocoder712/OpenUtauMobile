
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace OpenUtauMobile.Controls.Gestures;

/// <summary>
/// 输出手势：
///   单击       (Tap)             — 单指按下后未超过拖拽阈值即抬起
///   双击       (DoubleTap)       — 两次单击在时间和距离阈值内
///   拖拽开始   (DragBegin)       — 单指移动首次超过 DragThreshold
///   拖拽更新   (DragUpdate)      — 单指拖拽中每帧触发
///   拖拽结束   (DragEnd)         — 单指拖拽中抬起时触发
///   捏合更新   (PinchUpdate)     — 双指移动时每帧触发，携带 scaleX/scaleY/center/panDelta
///   捏合结束   (PinchEnd)        — 双指降回单指或全抬起时触发
///   二指点击   (TwoFingerTap)    — 双指均未移动且在时间阈值内抬起（且期间未发生捏合）
///   三指点击   (ThreeFingerTap)  — 三指均未移动且在时间窗口内按下并全部抬起
/// </summary>
public class GestureInterpreter
{
    #region 内部类型
    /// <summary>
    /// 状态机
    /// </summary>
    private enum GestureState
    {
        Idle,
        SingleFinger,
        TwoFinger,
        Suspended
    }

    /// <summary>
    /// 捏合轴意图锁定状态。
    /// </summary>
    private enum PinchAxisMode
    {
        /// <summary>双轴初始间距都太小，放弃缩放（只保留平移）。</summary>
        None,

        /// <summary>X 方向间距为主，锁 Y（scaleY 强制 1.0）。</summary>
        LockY,

        /// <summary>Y 方向间距为主，锁 X（scaleX 强制 1.0）。</summary>
        LockX,

        /// <summary>双轴间距相近，双轴同时缩放。</summary>
        Both,
    }

    /// <summary>
    /// 单个活动触点的快照数据。
    /// </summary>
    private struct TouchPoint
    {
        public Point PressPoint; // 按下时坐标（控件本地）
        public Point LastPoint; // 上一帧坐标
        public ulong PressTimestamp; // 按下时时间戳（毫秒，来自 PointerEventArgs.Timestamp）
    }
    #endregion

    /// <summary>
    /// 创建手势识别器。
    /// </summary>
    /// <param name="enableAxisLock">
    /// 是否启用捏合轴意图锁定。
    /// 启用时，识别器在两指落下时即根据初始 X/Y 间距决定锁轴策略：
    /// X 间距远大于 Y → 锁 Y（只缩 X）；Y 间距远大于 X → 锁 X（只缩 Y）；
    /// 相近 → 双轴同步；双轴都太近 → 放弃缩放（只保留平移）。
    /// 策略在整轮捏合期间固定不变。
    /// </param>
    public GestureInterpreter(bool enableAxisLock = false)
    {
        EnableAxisLock = enableAxisLock;
    }

    #region 阈值（可由外部配置）

    /// <summary>
    /// 是否启用捏合轴意图锁定（由构造时传入，运行期只读）。
    /// </summary>
    public bool EnableAxisLock { get; }

    /// <summary>单指拖拽判定阈值（px）。移动超过此距离才进入拖拽状态。</summary>
    public double DragThreshold { get; set; } = 6.0;

    /// <summary>双击最大间隔（ms）。</summary>
    public double DoubleTapMaxMs { get; set; } = 350.0;

    /// <summary>双击最大位移（px）。两次点击位置距离上限。</summary>
    public double DoubleTapMaxDist { get; set; } = 20.0;

    /// <summary>点击判定最大位移（px）。手指从按下到抬起的最大允许移动量，用于多指点击检测。</summary>
    public double TapMoveThreshold { get; set; } = 15.0;

    /// <summary>二指点击最大持续时间（ms）。从第一指按下到最后一指抬起的时间上限。</summary>
    public double TwoFingerTapMaxMs { get; set; } = 350.0;

    /// <summary>三指点击时间窗口（ms）。三个触点全部按下的时间跨度上限。</summary>
    public double ThreeFingerTapWindowMs { get; set; } = 250.0;

    /// <summary>
    /// 捏合最小轴间距（px）。两指在某轴方向的距离低于此值时，该轴 scale 强制为 1.0，防止除零或极值。
    /// </summary>
    public double MinPinchDist { get; set; } = 10.0;

    /// <summary>
    /// 捏合轴意图：双轴初始绝对间距（px）下限。
    /// 仅在 <see cref="EnableAxisLock"/> 为 true 时生效。
    /// 两指落下时，若 X 和 Y 方向的初始间距均低于此值，则放弃缩放（只保留平移）。
    /// 默认 20 px。
    /// </summary>
    public double PinchAxisMinDist { get; set; } = 20.0;

    /// <summary>
    /// 轴意图判定比率。
    /// 仅在 <see cref="EnableAxisLock"/> 为 true 时生效。
    /// 初始 X 间距 / Y 间距超过此值时锁 Y 轴（反之锁 X 轴）；否则双轴同步缩放。
    /// 默认 2.5。
    /// </summary>
    public double PinchAxisLockRatio { get; set; } = 2.5;
    #endregion

    #region 输出插槽（Action）

    /// <summary>单击。参数：控件本地坐标。</summary>
    public Action<Point>? Tap;

    /// <summary>双击。参数：控件本地坐标。</summary>
    public Action<Point>? DoubleTap;

    /// <summary>拖拽开始（单指移动首次超过 DragThreshold）。参数：按下时坐标（起点）。</summary>
    public Action<Point>? DragBegin;

    /// <summary>
    /// 拖拽更新。参数：(起点, 本帧步进 delta, 累积 delta, 实时point, 事件时间戳)。
    /// </summary>
    public Action<Point, Vector, Vector, Point, ulong>? DragUpdate;

    /// <summary>
    /// 拖拽结束。参数：(起点, 最终累积 delta, 事件时间戳)。
    /// </summary>
    public Action<Point, Vector, ulong>? DragEnd;

    /// <summary>
    /// 捏合更新。参数：(scaleX, scaleY, center, panDelta)。
    /// scaleX/scaleY 为步进比（本帧相对上帧，1.0 = 无变化）；
    /// center 为双指中点（控件本地坐标）；
    /// panDelta 为中点本帧位移向量。
    /// </summary>
    public Action<double, double, Point, Vector>? PinchUpdate;

    /// <summary>捏合结束（双指降回单指或全部抬起时触发）。</summary>
    public Action? PinchEnd;

    /// <summary>二指点击（双指均未明显移动且在时间阈值内完成按下/抬起，且期间未发生捏合）。</summary>
    public Action? TwoFingerTap;

    /// <summary>三指点击（三指均未明显移动，在时间窗口内全部按下并抬起）。</summary>
    public Action? ThreeFingerTap;

    #endregion

    #region 内部状态字段

    private readonly Dictionary<IPointer, TouchPoint> _touches = [];
    private GestureState _state = GestureState.Idle;

    // SingleFinger 状态
    private bool _isDragging;
    private Point _pressPoint; // 单指按下时的起点
    private Point _lastPoint; // 单指上一帧位置

    // 双击检测
    private bool _waitingSecondTap;
    private Point _lastTapPoint;
    private ulong _lastTapTimestamp;

    // TwoFinger 状态
    private bool _isPinching;
    private double _pinchPrevDistX;
    private double _pinchPrevDistY;

    private Point _pinchPrevCenter;

    // 本轮 TwoFinger 期间是否曾触发过 PinchUpdate（用于区分捏合与二指点击）
    private bool _hadPinched;

    // 二指依次抬起时暂存第一根手指，等最后一根也抬起后再触发点击。
    // 避免撤销回调在仍有触点捕获时改变可视树，进而打断剩余触点的释放事件。
    private bool _pendingTwoFingerTap;
    private TouchPoint _firstReleasedTwoFingerTouch;

    // 轴意图锁定（仅 EnableAxisLock=true 时使用）
    private PinchAxisMode _pinchAxisMode; // 当前轴策略（在 InitTwoFingerState 时决定）

    // Suspended 状态
    // 保存所有进入过 Suspended 的触点快照，LastPoint 实时更新，用于三指点击判定
    private readonly Dictionary<IPointer, TouchPoint> _suspendedTouches = [];

    // 当前正在从 Suspended 退出（触点依次抬起降回）。
    // 置 true 后，经过的 TwoFinger/SingleFinger 中间状态禁止触发 TwoFingerTap/Tap，
    // 直到触点最终归零时统一做三指点击判定后清除。
    private bool _exitingSuspended;

    // 从 TwoFinger（捏合或二指点击）降回 SingleFinger 后，下一次单指释放不触发 Tap/DoubleTap。
    // 防止二指操作结束后剩余那根手指抬起时误触发单击。
    // 新指按下（case 1）时重置，确保后续独立的单指操作不受影响。
    private bool _suppressNextTap;
    #endregion

    #region 底层事件输入

    /// <summary>由控件 OnPointerPressed 调用。</summary>
    public void OnPointerPressed(PointerPressedEventArgs e, Control relativeTo)
    {
        Point pos = e.GetPosition(relativeTo);
        ulong ts = e.Timestamp;

        TouchPoint tp = new()
        {
            PressPoint = pos,
            LastPoint = pos,
            PressTimestamp = ts,
        };
        _touches[e.Pointer] = tp;
        e.Pointer.Capture(relativeTo);

        HandlePressedWithTouchCount(e.Pointer, pos, ts, tp);

        e.Handled = true;
    }

    /// <summary>由控件 OnPointerMoved 调用。</summary>
    public void OnPointerMoved(PointerEventArgs e, Control relativeTo)
    {
        if (!_touches.TryGetValue(e.Pointer, out TouchPoint tp))
        {
            return;
        }

        Point pos = e.GetPosition(relativeTo);
        tp.LastPoint = pos;
        _touches[e.Pointer] = tp;

        // Suspended（含 _exitingSuspended 期间）：同步更新快照的 LastPoint
        if (_state == GestureState.Suspended || _exitingSuspended)
        {
            UpdateSuspendedLastPoint(e.Pointer, pos);
        }

        switch (_state)
        {
            case GestureState.SingleFinger:
                // _exitingSuspended 或 _suppressNextTap 期间不识别拖拽，仅追踪位置
                if (!_exitingSuspended && !_suppressNextTap)
                    UpdateSingleFinger(pos, e);
                break;

            case GestureState.TwoFinger:
                // _exitingSuspended 期间不识别捏合，仅追踪位置
                if (!_exitingSuspended)
                    UpdateTwoFinger(e);
                break;

                // Suspended：不触发任何手势
        }
    }

    /// <summary>由控件 OnPointerReleased 调用。</summary>
    public void OnPointerReleased(PointerReleasedEventArgs e, Control relativeTo)
    {
        if (!_touches.TryGetValue(e.Pointer, out TouchPoint tp)) return;

        Point pos = e.GetPosition(relativeTo);
        ulong ts = e.Timestamp;
        tp.LastPoint = pos;
        _touches[e.Pointer] = tp;

        // Suspended（含 _exitingSuspended 期间）：同步更新快照的 LastPoint
        if (_state == GestureState.Suspended || _exitingSuspended)
            UpdateSuspendedLastPoint(e.Pointer, pos);

        _touches.Remove(e.Pointer);
        e.Pointer.Capture(null);

        switch (_state)
        {
            case GestureState.SingleFinger:
                HandleSingleFingerRelease(pos, ts, tp);
                break;

            case GestureState.TwoFinger:
                HandleTwoFingerRelease(ts, tp);
                break;

            case GestureState.Suspended:
                HandleSuspendedRelease();
                break;
        }

        e.Handled = true;
    }

    /// <summary>
    /// 由控件 OnPointerCaptureLost 调用。
    /// 清除对应触点，并根据当前状态触发相应的结束事件。
    /// </summary>
    public void OnPointerCancelled(PointerCaptureLostEventArgs e, Control relativeTo)
    {
        if (!_touches.ContainsKey(e.Pointer)) return;

        List<IPointer> cancelledPointers = [.. _touches.Keys];

        // 捕获丢失意味着平台不再保证其余触点仍会收到 Released。
        // 整轮作废并清空全部触点，防止残留触点污染下一轮单指操作。
        if (_state == GestureState.TwoFinger && _isPinching)
            CommitPinchEnd();
        else if (_state == GestureState.SingleFinger && _isDragging)
        {
            DragEnd?.Invoke(_pressPoint, _lastPoint - _pressPoint, 0);
            _isDragging = false;
        }

        _touches.Clear();
        foreach (IPointer pointer in cancelledPointers)
        {
            pointer.Capture(null);
        }

        _exitingSuspended = false;
        _suppressNextTap = false;
        _pendingTwoFingerTap = false;
        _suspendedTouches.Clear();
        _hadPinched = false;
        _isPinching = false;
        _isDragging = false;
        _state = GestureState.Idle;
    }

    #endregion

    // -------------------------------------------------------
    //  单指内部逻辑
    // -------------------------------------------------------

    private void UpdateSingleFinger(Point current, PointerEventArgs e)
    {
        if (!_isDragging && IsDistanceGreaterThan(_pressPoint, current, DragThreshold))
        {
            _isDragging = true;
            _waitingSecondTap = false;
            DragBegin?.Invoke(_pressPoint);
        }

        if (_isDragging)
        {
            Vector step = current - _lastPoint;
            Vector total = current - _pressPoint;
            DragUpdate?.Invoke(_pressPoint, step, total, current, e.Timestamp);
            _lastPoint = current;
            e.Handled = true;
        }
    }

    private void CommitSingleFingerRelease(Point releasePoint, ulong ts)
    {
        if (_isDragging)
            CommitDragEnd(releasePoint, ts);
        else
            TryFireTap(releasePoint, ts);
    }

    private void CommitDragEnd(Point releasePoint, ulong ts)
    {
        _isDragging = false;
        DragEnd?.Invoke(_pressPoint, releasePoint - _pressPoint, ts);
    }

    /// <summary>重新以当前唯一触点的位置作为单指模式起点（捏合/Suspended 结束后衔接用）。</summary>
    private void ReinitSingleFinger()
    {
        _isDragging = false;
        if (TryGetFirstTouch(out TouchPoint touch))
        {
            _pressPoint = touch.LastPoint;
            _lastPoint = _pressPoint;
        }
    }

    private void TryFireTap(Point pos, ulong ts)
    {
        if (_waitingSecondTap &&
            ts - _lastTapTimestamp <= (ulong)DoubleTapMaxMs &&
            IsDistanceWithin(pos, _lastTapPoint, DoubleTapMaxDist))
        {
            _waitingSecondTap = false;
            DoubleTap?.Invoke(pos);
        }
        else
        {
            Tap?.Invoke(pos);
            _lastTapPoint = pos;
            _lastTapTimestamp = ts;
            _waitingSecondTap = true;
        }
    }

    // -------------------------------------------------------
    //  双指内部逻辑
    // -------------------------------------------------------

    private void InitTwoFingerState()
    {
        _isPinching = false;
        _hadPinched = false;
        _pinchAxisMode = PinchAxisMode.None;
        if (!TryGetTwoTouchPoints(out Point p0, out Point p1)) return;

        double rawDistX = Math.Abs(p1.X - p0.X);
        double rawDistY = Math.Abs(p1.Y - p0.Y);

        _pinchPrevDistX = Math.Max(rawDistX, MinPinchDist);
        _pinchPrevDistY = Math.Max(rawDistY, MinPinchDist);
        _pinchPrevCenter = new Point((p0.X + p1.X) * 0.5, (p0.Y + p1.Y) * 0.5);

        if (EnableAxisLock)
        {
            // 双轴间距均低于下限 → 放弃缩放
            if (rawDistX < PinchAxisMinDist && rawDistY < PinchAxisMinDist)
                _pinchAxisMode = PinchAxisMode.None;
            else if (rawDistX > rawDistY * PinchAxisLockRatio)
                _pinchAxisMode = PinchAxisMode.LockY;
            else if (rawDistY > rawDistX * PinchAxisLockRatio)
                _pinchAxisMode = PinchAxisMode.LockX;
            else
                _pinchAxisMode = PinchAxisMode.Both;
        }
    }

    private void UpdateTwoFinger(PointerEventArgs e)
    {
        if (!TryGetTwoTouchPoints(out Point p0, out Point p1)) return;

        Point center = new((p0.X + p1.X) * 0.5, (p0.Y + p1.Y) * 0.5);
        double curDistX = Math.Max(Math.Abs(p1.X - p0.X), MinPinchDist);
        double curDistY = Math.Max(Math.Abs(p1.Y - p0.Y), MinPinchDist);

        double scaleX = curDistX / _pinchPrevDistX;
        double scaleY = curDistY / _pinchPrevDistY;
        Vector panDelta = center - _pinchPrevCenter;

        // 平台通常会在手指静止时仍发送零位移或轻微抖动的 Moved。
        // 在任一触点越过点击容差前只更新基线，不把二指点击候选误判为捏合。
        if (!_isPinching && !HasAnyTouchMovedBeyond(TapMoveThreshold))
        {
            _pinchPrevDistX = curDistX;
            _pinchPrevDistY = curDistY;
            _pinchPrevCenter = center;
            return;
        }

        if (EnableAxisLock)
        {
            switch (_pinchAxisMode)
            {
                case PinchAxisMode.None:
                    // 双轴间距太近，放弃缩放，仅保留平移
                    scaleX = 1.0;
                    scaleY = 1.0;
                    break;
                case PinchAxisMode.LockY:
                    scaleY = 1.0;
                    break;
                case PinchAxisMode.LockX:
                    scaleX = 1.0;
                    break;
                    // Both：双轴透传，不修改
            }
        }

        PinchUpdate?.Invoke(scaleX, scaleY, center, panDelta);
        _isPinching = true;
        _hadPinched = true;

        _pinchPrevDistX = curDistX;
        _pinchPrevDistY = curDistY;
        _pinchPrevCenter = center;

        e.Handled = true;
    }

    private void CommitPinchEnd()
    {
        if (_isPinching)
        {
            _isPinching = false;
            PinchEnd?.Invoke();
        }
    }

    // -------------------------------------------------------
    //  多指点击检测
    // -------------------------------------------------------

    /// <summary>
    /// 在正常 TwoFinger → SingleFinger/Idle 转换时调用（必须在 CommitPinchEnd 之后，
    /// 且 _exitingSuspended == false）。
    /// 若两指均未明显移动、整个过程在时间阈值内、且从未触发过 PinchUpdate，则触发 TwoFingerTap。
    /// </summary>
    private void TryFireTwoFingerTap(ulong releaseTs, TouchPoint firstReleasedTouch, TouchPoint lastReleasedTouch)
    {
        bool firstStill = IsDistanceWithin(
            firstReleasedTouch.PressPoint,
            firstReleasedTouch.LastPoint,
            TapMoveThreshold);
        bool lastStill = IsDistanceWithin(
            lastReleasedTouch.PressPoint,
            lastReleasedTouch.LastPoint,
            TapMoveThreshold);
        ulong firstPress = Math.Min(firstReleasedTouch.PressTimestamp, lastReleasedTouch.PressTimestamp);
        bool inTime = (releaseTs - firstPress) <= (ulong)TwoFingerTapMaxMs;

        if (firstStill && lastStill && inTime)
        {
            TwoFingerTap?.Invoke();
            _waitingSecondTap = false;
        }
    }

    /// <summary>
    /// 当触点最终归零且之前曾经历 Suspended 时调用。
    /// 检测三指点击：恰好 3 根手指、均未明显移动、按下时间窗口内。
    /// </summary>
    private void TryFireThreeFingerTap()
    {
        try
        {
            // 只处理恰好 3 根手指的情况（>3 不触发）
            if (_suspendedTouches.Count != 3) return;

            ulong firstPress = ulong.MaxValue;
            ulong lastPress = 0;
            bool allStill = true;
            foreach (KeyValuePair<IPointer, TouchPoint> kv in _suspendedTouches)
            {
                TouchPoint t = kv.Value;
                if (t.PressTimestamp < firstPress)
                {
                    firstPress = t.PressTimestamp;
                }

                if (t.PressTimestamp > lastPress)
                {
                    lastPress = t.PressTimestamp;
                }

                if (!IsDistanceWithin(t.PressPoint, t.LastPoint, TapMoveThreshold))
                {
                    allStill = false;
                    break;
                }
            }

            bool pressedQuickly = (lastPress - firstPress) <= (ulong)ThreeFingerTapWindowMs;

            if (pressedQuickly && allStill)
                ThreeFingerTap?.Invoke();
        }
        finally
        {
            _suspendedTouches.Clear();
        }
    }

    // -------------------------------------------------------
    //  Suspended 辅助
    // -------------------------------------------------------

    /// <summary>按触点身份更新 Suspended 快照中的最新位置。</summary>
    private void UpdateSuspendedLastPoint(IPointer pointer, Point newLastPoint)
    {
        if (_suspendedTouches.TryGetValue(pointer, out TouchPoint touch))
        {
            touch.LastPoint = newLastPoint;
            _suspendedTouches[pointer] = touch;
        }
    }

    // -------------------------------------------------------
    //  内部辅助
    // -------------------------------------------------------

    private void HandlePressedWithTouchCount(IPointer pointer, Point pos, ulong ts, TouchPoint newTouch)
    {
        switch (_touches.Count)
        {
            case 1:
                EnterSingleFinger(pos);
                break;
            case 2:
                EnterTwoFinger(ts);
                break;
            default:
                EnterSuspended(pointer, pos, ts, newTouch);
                break;
        }
    }

    private void EnterSingleFinger(Point pos)
    {
        _state = GestureState.SingleFinger;
        _pressPoint = pos;
        _lastPoint = pos;
        _isDragging = false;
        _exitingSuspended = false;
        _suppressNextTap = false;
        _pendingTwoFingerTap = false;
    }

    private void EnterTwoFinger(ulong ts)
    {
        // 第 2 指落下时，若正在单指拖拽则先收敛到 DragEnd。
        if (_state == GestureState.SingleFinger && _isDragging)
        {
            CommitDragEnd(_lastPoint, ts);
        }

        _state = GestureState.TwoFinger;
        _waitingSecondTap = false;
        _exitingSuspended = false;
        _suppressNextTap = false;
        _pendingTwoFingerTap = false;
        InitTwoFingerState();
    }

    private void EnterSuspended(IPointer pointer, Point pos, ulong ts, TouchPoint newTouch)
    {
        if (_state == GestureState.TwoFinger && _isPinching)
        {
            CommitPinchEnd();
        }
        else if (_state == GestureState.SingleFinger && _isDragging)
        {
            CommitDragEnd(pos, ts);
        }

        if (_state != GestureState.Suspended)
        {
            _suspendedTouches.Clear();
            _exitingSuspended = false;
            _pendingTwoFingerTap = false;
            foreach (KeyValuePair<IPointer, TouchPoint> kv in _touches)
            {
                _suspendedTouches[kv.Key] = kv.Value;
            }
        }
        else
        {
            _suspendedTouches[pointer] = newTouch;
        }

        _state = GestureState.Suspended;
    }

    private void HandleSingleFingerRelease(Point pos, ulong ts, TouchPoint releasedTouch)
    {
        if (_exitingSuspended)
        {
            TryFireThreeFingerTap();
            _exitingSuspended = false;
        }
        else if (_suppressNextTap)
        {
            if (_pendingTwoFingerTap)
            {
                TryFireTwoFingerTap(ts, _firstReleasedTwoFingerTouch, releasedTouch);
            }

            _suppressNextTap = false;
            _pendingTwoFingerTap = false;
        }
        else
        {
            CommitSingleFingerRelease(pos, ts);
        }

        _state = GestureState.Idle;
    }

    private void HandleTwoFingerRelease(ulong ts, TouchPoint releasedTouch)
    {
        CommitPinchEnd();

        if (_exitingSuspended)
        {
            if (_touches.Count == 1)
            {
                _state = GestureState.SingleFinger;
                ReinitSingleFinger();
            }
            else
            {
                TryFireThreeFingerTap();
                _exitingSuspended = false;
                _state = GestureState.Idle;
            }

            return;
        }

        if (_touches.Count == 1)
        {
            _pendingTwoFingerTap = !_hadPinched;
            _firstReleasedTwoFingerTouch = releasedTouch;
            _hadPinched = false;
            _suppressNextTap = true;
            _state = GestureState.SingleFinger;
            ReinitSingleFinger();
        }
        else
        {
            _pendingTwoFingerTap = false;
            _hadPinched = false;
            _state = GestureState.Idle;
        }
    }

    private void HandleSuspendedRelease()
    {
        if (_touches.Count == 2)
        {
            _exitingSuspended = true;
            _state = GestureState.TwoFinger;
            InitTwoFingerState();
        }
        else if (_touches.Count == 1)
        {
            _exitingSuspended = true;
            _state = GestureState.SingleFinger;
            ReinitSingleFinger();
        }
        else
        {
            TryFireThreeFingerTap();
            _exitingSuspended = false;
            _state = GestureState.Idle;
        }
    }

    private bool TryGetFirstTouch(out TouchPoint touch)
    {
        using Dictionary<IPointer, TouchPoint>.Enumerator it = _touches.GetEnumerator();
        if (it.MoveNext())
        {
            touch = it.Current.Value;
            return true;
        }

        touch = default;
        return false;
    }

    private bool TryGetTwoTouchPoints(out Point p0, out Point p1)
    {
        using Dictionary<IPointer, TouchPoint>.Enumerator it = _touches.GetEnumerator();
        if (!it.MoveNext())
        {
            p0 = default;
            p1 = default;
            return false;
        }

        p0 = it.Current.Value.LastPoint;
        if (!it.MoveNext())
        {
            p1 = default;
            return false;
        }

        p1 = it.Current.Value.LastPoint;
        return true;
    }

    private bool HasAnyTouchMovedBeyond(double threshold)
    {
        foreach (KeyValuePair<IPointer, TouchPoint> kv in _touches)
        {
            TouchPoint touch = kv.Value;
            if (IsDistanceGreaterThan(touch.PressPoint, touch.LastPoint, threshold))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDistanceWithin(Point a, Point b, double threshold)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        double dist2 = dx * dx + dy * dy;
        double threshold2 = threshold * threshold;
        return dist2 <= threshold2;
    }

    private static bool IsDistanceGreaterThan(Point a, Point b, double threshold)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        double dist2 = dx * dx + dy * dy;
        double threshold2 = threshold * threshold;
        return dist2 > threshold2;
    }
}
