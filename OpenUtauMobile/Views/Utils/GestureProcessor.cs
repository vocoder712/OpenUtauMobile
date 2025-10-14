using Serilog;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using System.Diagnostics;

namespace OpenUtauMobile.Views.Utils;
public class GestureProcessor
{
    private readonly Transformer _transformer;

    // 手势参数配置
    private const float ClickThreshold = 5f;   // 点击移动阈值（平方）
    private const float DoubleTapDistanceThreshold = 20f; // 双击距离阈值
    private const int DoubleTapTimeThresholdMs = 300;     // 双击时间阈值（毫秒）

    // 触摸点管理
    private readonly Dictionary<long, TouchPoint> _activePoints = new();
    private readonly Queue<long> _pointQueue = new(); // 用于多点触摸时的点替换

    // 手势状态
    private enum GestureState { None, Tap, DoubleTap, Pan, Zoom, XZoom, YZoom }
    // 当前手势状态
    private GestureState _currentState = GestureState.None;

    // 异步操作控制
    private CancellationTokenSource _cts = new CancellationTokenSource(); // 取消令牌源，用于取消异步操作
    private SKPoint _lastSinglePoint;

    // 用于标记是否已经开始拖动
    private bool _hasPanStarted = false;

    // 双击检测相关
    private SKPoint? _lastTapPosition = null;
    private DateTime _lastTapTime = DateTime.MinValue;

    public GestureProcessor(Transformer transformer)
    {
        _transformer = transformer; // 初始化变换器
    }

    /// <summary>
    /// 主处理函数
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    public void ProcessTouch(object sender, SKTouchEventArgs e)
    {
        switch (e.ActionType)
        {
            case SKTouchAction.Pressed:
                HandleTouchDown(e);
                break;

            case SKTouchAction.Moved:
                HandleTouchMove(e);
                break;

            case SKTouchAction.Released:
                HandleTouchUp(e);
                break;

            case SKTouchAction.Cancelled:
                HandleTouchCancel(e);
                break;
        }

        e.Handled = true;
    }

    /// <summary>
    /// 处理触摸取消事件
    /// </summary>
    /// <param name="e"></param>
    private void HandleTouchCancel(SKTouchEventArgs e)
    {
        Debug.WriteLine($"Touch cancelled: {e.Id}");
    }

    /// <summary>
    /// 处理触摸按下事件
    /// </summary>
    /// <param name="e"></param>
    private void HandleTouchDown(SKTouchEventArgs e)
    {
        // 维护触摸点队列
        if (_activePoints.Count >= 2)
        {
            _pointQueue.Enqueue(e.Id);
            return;
        }

        var point = new TouchPoint(e.Id, e.Location, DateTime.UtcNow);
        try {
            _activePoints.Add(e.Id, point); 
        } catch(Exception err) { 
            Debug.WriteLine($"系统卡顿{err}"); 
            Log.Error(err, "系统卡顿，无法添加触摸点");
        }

        // 重置拖动标记
        _hasPanStarted = false;

        // 单点触摸逻辑
        if (_activePoints.Count == 1)
        {
            _lastSinglePoint = e.Location;
            StartGestureDetection(point);
        }
        // 切换到双点触摸
        else if (_activePoints.Count == 2)
        {
            CancelCurrentGesture();
            InitializeZoomGesture();
        }
    }

    /// <summary>
    /// 处理触摸移动事件
    /// </summary>
    /// <param name="e"></param>
    private void HandleTouchMove(SKTouchEventArgs e)
    {
        if (!_activePoints.TryGetValue(e.Id, out var point)) return;

        point.Update(e.Location, DateTime.UtcNow);

        // 当前有效触摸点数
        var validPoints = _activePoints.Count;
        if (validPoints == 1)
        {
            HandleSingleTouchMove(point);
        }
        else if (validPoints == 2)
        {
            HandleZoomGestureMove();
        }
    }

    /// <summary>
    /// 处理触摸抬起事件
    /// </summary>
    /// <param name="e"></param>
    private void HandleTouchUp(SKTouchEventArgs e)
    {
        if (_activePoints.TryGetValue(e.Id, out var point))
        {
            // 如果抬起时未开始拖动且移动距离很小，触发单击
            if (!_hasPanStarted && CheckMoveThreshold(point) && _currentState == GestureState.None)
            {
                var touchDuration = (DateTime.UtcNow - point.StartTime).TotalMilliseconds;
                if (touchDuration < 300) // 适当增大点击时间窗口
                {
                    // 检查是否是双击
                    if (_lastTapPosition != null &&
                        (DateTime.UtcNow - _lastTapTime).TotalMilliseconds <= DoubleTapTimeThresholdMs)
                    {
                        // 检查两次点击的距离是否足够接近
                        float distance = SKPoint.Distance(_lastTapPosition.Value, point.LastPosition);

                        if (distance <= DoubleTapDistanceThreshold)
                        {
                            // 触发双击事件
                            _currentState = GestureState.DoubleTap;
                            DoubleTap?.Invoke(this, new TapEventArgs(point.LastPosition));
                            // 重置最后点击状态
                            _lastTapPosition = null;
                            _lastTapTime = DateTime.MinValue;
                        }
                        else
                        {
                            // 距离过远，当作新的单击处理
                            _currentState = GestureState.Tap;
                            Tap?.Invoke(this, new TapEventArgs(point.LastPosition));
                            // 更新最后点击状态
                            _lastTapPosition = point.LastPosition;
                            _lastTapTime = DateTime.UtcNow;
                        }
                    }
                    else
                    {
                        // 普通单击
                        _currentState = GestureState.Tap;
                        Tap?.Invoke(this, new TapEventArgs(point.LastPosition));
                        // 记录这次点击，为双击检测做准备
                        _lastTapPosition = point.LastPosition;
                        _lastTapTime = DateTime.UtcNow;
                    }
                }
            }

            // 从活动点中移除
            _activePoints.Remove(e.Id);

            // 从队列中补充新点
            if (_pointQueue.Count > 0 && _activePoints.Count < 2)
            {
                var newId = _pointQueue.Dequeue();
                _activePoints.Add(newId, new TouchPoint(newId, point.LastPosition, DateTime.UtcNow));
            }

            // 如果当前状态是平移，触发平移结束事件
            if (_currentState == GestureState.Pan)
            {
                PanEnd?.Invoke(this, new PanEndEventArgs(point.LastPosition));
            }
        }

        // 状态转换逻辑
        switch (_activePoints.Count)
        {
            case 0:
                FinalizeGesture();
                break;

            case 1 when _currentState == GestureState.XZoom || _currentState == GestureState.YZoom:
                SwitchToPanFromZoom();
                break;
        }
    }

    #region 手势核心逻辑
    private void StartGestureDetection(TouchPoint point)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
    }

    private void HandleSingleTouchMove(TouchPoint point)
    {
        // 检测移动阈值
        if (!CheckMoveThreshold(point))  // 如果移动超过阈值
        {
            // 一旦超过移动阈值，标记已开始拖动
            _hasPanStarted = true;

            if (_currentState == GestureState.None)
            {
                CancelCurrentGesture();
                _currentState = GestureState.Pan;
                PanStart?.Invoke(this, new PanStartEventArgs(point.LastPosition));
            }

            if (_currentState == GestureState.Pan)
            {
                PanUpdate?.Invoke(this, new PanUpdateEventArgs(point.LastPosition));
            }
        }
    }

    private void InitializeZoomGesture()
    {
        var points = _activePoints.Values.Take(2).ToArray();

        // 分情况讨论
        // 如果X距离过近，禁用X方向缩放
        // 如果Y距离过近，禁用Y方向缩放
        // 如果both，仅平移
        float deltaX = Math.Abs(points[0].LastPosition.X - points[1].LastPosition.X);
        float deltaY = Math.Abs(points[0].LastPosition.Y - points[1].LastPosition.Y);
        Debug.WriteLine($"DeltaX: {deltaX}, DeltaY: {deltaY}");
        const float deltaThreshold = 200f;
        if (deltaX < deltaThreshold && deltaY < deltaThreshold)
        {
            // 两点过近，视为平移
            //CancelCurrentGesture();
            //_currentState = GestureState.Pan;
            //PanStart?.Invoke(this, new PanStartEventArgs((points[0].LastPosition + points[1].LastPosition) / 2));
            return;
        }
        else if (deltaX < deltaThreshold)
        {
            // X方向距离过近，仅Y缩放
            Debug.WriteLine("X方向距离过近，仅Y缩放");
            YZoomStart?.Invoke(this, new ZoomStartEventArgs(points[0].LastPosition, points[1].LastPosition));
            _currentState = GestureState.YZoom;
        }
        else if (deltaY < deltaThreshold)
        {
            // Y方向距离过近，仅X缩放
            Debug.WriteLine("Y方向距离过近，仅X缩放");
            XZoomStart?.Invoke(this, new ZoomStartEventArgs(points[0].LastPosition, points[1].LastPosition));
            _currentState = GestureState.XZoom;
        }
        else
        {
            // 正常双指缩放
            ZoomStart?.Invoke(this, new ZoomStartEventArgs(points[0].LastPosition, points[1].LastPosition));
            _currentState = GestureState.Zoom;
        }
    }

    private void HandleZoomGestureMove()
    {
        var points = _activePoints.Values.Take(2).ToArray();
        if (_currentState == GestureState.XZoom)
            XZoomUpdate?.Invoke(this, new ZoomUpdateEventArgs(points[0].LastPosition, points[1].LastPosition));
        else if (_currentState == GestureState.YZoom)
            YZoomUpdate?.Invoke(this, new ZoomUpdateEventArgs(points[0].LastPosition, points[1].LastPosition));
        else if (_currentState == GestureState.Zoom)
            ZoomUpdate?.Invoke(this, new ZoomUpdateEventArgs(points[0].LastPosition, points[1].LastPosition));
    }

    private void SwitchToPanFromZoom()
    {
        var remainingPoint = _activePoints.Values.First();
        PanStart?.Invoke(this, new PanStartEventArgs(remainingPoint.LastPosition));
        _currentState = GestureState.Pan;
    }
    #endregion

    #region 辅助方法
    private bool CheckMoveThreshold(TouchPoint point)
    {
        var delta = point.LastPosition - point.StartPosition;
        return delta.LengthSquared < ClickThreshold * ClickThreshold;
    }

    private void CancelCurrentGesture()
    {
        _cts?.Cancel();
        _currentState = GestureState.None;
    }

    private void FinalizeGesture()
    {
        switch (_currentState)
        {
            case GestureState.Pan:
                _transformer.EndPan();
                break;
            case GestureState.XZoom:
                // _transformer.EndZoom();
                break;
            case GestureState.YZoom:
                break;
        }
        _currentState = GestureState.None;
        _hasPanStarted = false;
    }
    #endregion

    #region 事件
    // 点击事件
    public event EventHandler<TapEventArgs>? Tap;
    public event EventHandler<TapEventArgs>? DoubleTap;
    // 平移事件
    public event EventHandler<PanStartEventArgs>? PanStart;
    public event EventHandler<PanUpdateEventArgs>? PanUpdate;
    public event EventHandler<PanEndEventArgs>? PanEnd;
    // 缩放事件
    public event EventHandler<ZoomStartEventArgs>? ZoomStart;
    public event EventHandler<ZoomUpdateEventArgs>? ZoomUpdate;
    public event EventHandler<ZoomStartEventArgs>? XZoomStart;
    public event EventHandler<ZoomUpdateEventArgs>? XZoomUpdate;
    public event EventHandler<ZoomStartEventArgs>? YZoomStart;
    public event EventHandler<ZoomUpdateEventArgs>? YZoomUpdate;
    #endregion
}

// 触摸点跟踪类
public class TouchPoint
{
    public long Id { get; }
    public SKPoint StartPosition { get; }
    public SKPoint LastPosition { get; private set; }
    public DateTime StartTime { get; }

    public TouchPoint(long id, SKPoint start, DateTime time)
    {
        Id = id;
        StartPosition = start;
        LastPosition = start;
        StartTime = time;
    }

    public void Update(SKPoint newPosition, DateTime time)
    {
        LastPosition = newPosition;
    }
}
