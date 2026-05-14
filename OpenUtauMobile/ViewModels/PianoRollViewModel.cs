using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DynamicData;
using DynamicData.Binding;
using IconPacks.Avalonia.PhosphorIcons;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using OpenUtauMobile.Audio;
using OpenUtauMobile.Controls.Gestures;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;
using Point = Avalonia.Point;
using Size = Avalonia.Size;

namespace OpenUtauMobile.ViewModels;

/// <summary>
/// 钢琴卷帘编辑模式
/// </summary>
public enum PianoRollEditMode
{
    Hand, // 拖拽
    MultiSelect, // 多选
    Note, // 音符画笔
    PitchPen, // 音高线画笔
    Anchor, // 音高锚点
    Vibrato // 颤音
}

/// <summary>
/// 钢琴卷帘输入状态机
/// </summary>
public enum PianoRollInputState
{
    Idle, // 空闲
    Panning, // 平移/滚动
    MovingNotes, // 拖拽音符
    ResizingNotes, // 调整音符长度
    DrawingPitch, // 画音高线
    MovingAnchors, // 拖拽移动选中音高锚点
    EditingVibrato, // 拖拽颤音控件
}

public enum VibratoHandleKind
{
    Body,
    Start,
    FadeIn,
    FadeOut,
    Phase,
    Drift,
}

public readonly record struct VibratoOverlayLayout(
    UNote Note,
    bool IsEnabled,
    Rect NoteRect,
    Rect BodyRect,
    Rect StartHandleRect,
    Rect FadeInHandleRect,
    Rect FadeOutHandleRect,
    Rect PhaseTrackRect,
    Rect PhaseHandleRect,
    Rect DriftHandleRect,
    Point GuideStartPoint,
    Point FadeInPoint,
    Point FadeOutPoint,
    Point GuideEndPoint,
    Point DriftAnchorPoint,
    Point DriftHandlePoint,
    double WaveStartX,
    double WaveEndX,
    double WaveCenterY);

public readonly record struct VibratoOverlayHit(UNote Note, VibratoHandleKind HandleKind, VibratoOverlayLayout Layout);
public readonly record struct PitchPointHit(UNote Note, PitchPoint Point, int Index);
public readonly record struct PitchCurveHit(UNote Note, int InsertIndex, float XMs, float Y, PitchPointShape Shape);

public class PianoRollViewModel : ViewModelBase, IDisposable, ICmdSubscriber
{
    #region 绑定属性

    [Reactive] public UVoicePart? EditingVoicePart { get; set; }
    [Reactive] public UWavePart? EditingWavePart { get; set; }
    public bool IsVoiceMode => EditingVoicePart != null;
    public bool IsWaveMode => EditingWavePart != null;

    private readonly DispatcherTimer? _toneTimer;

    /// <summary>
    /// 显示在钢琴卷帘中上方的编辑提示标语
    /// </summary>
    [Reactive]
    public string EditingTip { get; set; } = string.Empty;

    /// <summary>
    /// 立绘
    /// </summary>
    [Reactive]
    public Bitmap? PortraitBitmap { get; set; }

    /// <summary>
    /// 立绘不透明度
    /// </summary>
    [Reactive]
    public double PortraitOpacity { get; set; }

    /// <summary>
    /// 立绘加载取消令牌源，用于在切换分片或关闭立绘时取消未完成的加载任务，避免过时的加载结果覆盖当前立绘。
    /// </summary>
    private CancellationTokenSource? _portraitCts;

    [Reactive] public PianoRollEditMode EditMode { get; set; } = PianoRollEditMode.Note;

    // 钢琴卷帘视口控制属性
    [Reactive] public double TickWidth { get; set; } = ViewConstants.PianoRollTickWidthDefault; // 缩放
    [Reactive] public double KeyHeight { get; set; } = 32; // 高度
    [Reactive] public double TickOffset { get; set; } // X 滚动
    [Reactive] public double KeyOffset { get; set; } = 56; // Y 滚动

    // ── 播放状态（直接由权威源驱动）─────
    /// <summary>
    /// 当前工程全局播放标记位置（Tick，绝对坐标，与走带编曲区共享同一命令流）。
    /// </summary>
    [Reactive]
    public int PlayPosTick { get; set; }

    /// <summary>
    /// 是否正在播放（由 PlaybackManager 权威状态推导）。
    /// </summary>
    [Reactive]
    public bool IsPlaying { get; set; }

    /// <summary>
    /// 是否正在等待渲染（StartingToPlay 或播放流 waitingRendering）。
    /// </summary>
    [Reactive]
    public bool IsWaitingRender { get; set; }

    /// <summary>
    /// 钢琴卷帘区吸附分度，仅作用于音符的创建和拖拽。
    /// 由 EditorViewModel.SnapDiv 统一驱动，不再独立设置。
    /// -1 = 自动（根据 TickWidth 动态推导）；0 = 关闭（自由移动）；正数 = 固定分度。
    /// </summary>
    [Reactive]
    public int PianoRollSnapDiv { get; set; } = -1;

    public bool WasPanMotionInterrupted { get; private set; }

    /// <summary>
    /// 选中的音符列表
    /// </summary>
    [Reactive]
    public ObservableCollectionExtended<UNote> SelectedNotes { get; init; } = [];

    /// <summary>
    /// 选中的音高锚点列表（Anchor 模式）
    /// </summary>
    [Reactive]
    public ObservableCollectionExtended<PitchPoint> SelectedAnchors { get; init; } = [];

    /// <summary>
    /// 音高线画笔是否处于橡皮擦模式（在 PitchPen 模式内切换）
    /// </summary>
    [Reactive]
    public bool IsPitchEraserMode { get; set; }

    /// <summary>
    /// 是否正在进行音高线绘制（包含橡皮擦模式）。
    /// 供视图层决定是否显示触点指示器。
    /// </summary>
    [Reactive]
    public bool IsPitchDrawingActive { get; private set; }

    /// <summary>
    /// 音高线绘制时的当前触点（画布坐标）。
    /// </summary>
    [Reactive]
    public Point? PitchDrawPointer { get; private set; }

    /// <summary>
    /// 钢琴卷帘当前编辑模式下的上下文操作列表。
    /// 由 RebuildPianoRollContextActions() 重新计算后推送给 ContextActionPanel.Actions。
    /// </summary>
    [Reactive]
    public IReadOnlyList<ContextActionItem> PianoRollContextActions { get; private set; } = [];

    /// <summary>
    /// 上下文菜单是否展开。
    /// 用于性能优化：收起时跳过 RebuildPianoRollContextActions。
    /// </summary>
    [Reactive]
    public bool IsContextMenuExpanded { get; set; } = true;

    /// <summary>
    /// 播放标记的屏幕 X 坐标（像素）。
    /// 始终等于音符画布宽度的一半（画布中央），仅在 OnNoteAreaSizeChanged 时更新。
    /// 绑定到 PianoRollPlayPosCanvas.PlayMarkerScreenX，控件直接在此坐标绘制竖线，
    /// 与 TickOffset 完全无关，从而实现「播放标记外观始终静止」。
    /// </summary>
    [Reactive]
    public double PlayMarkerScreenX { get; private set; }

    /// <summary>
    /// 手势解释器，供 NotesCanvas 等视图组件复用
    /// </summary>
    public GestureInterpreter Gesture { get; private set; } = new(enableAxisLock: true);

    /// <summary>
    /// 多选模式下选区起始 Tick（相对于 EditingVoicePart）。
    /// 仅在 IsSelecting=true 时有效。
    /// </summary>
    [Reactive]
    public int BeginSelectionTick { get; private set; }

    /// <summary>
    /// 是否正在进行多选范围选择。
    /// 仅在 EditMode=MultiSelect 时应为 true。
    /// </summary>
    [Reactive]
    public bool IsSelecting { get; private set; }

    #endregion

    // ── 内部字段 ──────────────────────────────────────────────────────
    private readonly CompositeDisposable _disposables = new();
    private readonly ViewportMotionController _panMotion = new();

    private double _noteAreaWidth;
    private double _noteAreaHeight;

    // 视口动态上限缓存（仿 EditorViewModel）
    private bool _maxOffsetsDirty = true;
    private double _cachedMaxTickOffset;
    private double _cachedMaxKeyOffset;
    public double ContentEndTick;

    /// <summary>
    /// 标记动态上限缓存失效。
    /// 应在 EditingVoicePart 内容变化、TickWidth、KeyHeight、画布尺寸变化时调用。
    /// </summary>
    public void InvalidateMaxOffsets()
    {
        _maxOffsetsDirty = true;
    }

    /// <summary>
    /// 若缓存有效直接返回，否则重算。
    /// MaxTickOffset：分片末尾 + 余量 - 可见宽度（Tick），以绝对 Tick 为单位。
    /// MaxKeyOffset：当 C0 恰好对齐屏幕底端时的偏移值。
    ///   计算公式：KeyOffset_max = MaxTone - 1 - visibleKeys
    ///   其中 visibleKeys = screenHeight / KeyHeight（屏幕能显示的键数）
    /// </summary>
    private void EnsureMaxOffsets()
    {
        if (!_maxOffsetsDirty) return;
        int resolution = DocManager.Inst.Project.resolution;
        double visibleTicks = TickWidth > 0 ? _noteAreaWidth / TickWidth : 0;
        _cachedMaxTickOffset = Math.Max( // 最大X滚动偏移
            MinTickOffset,
            ContentEndTick + ViewConstants.SpareQuarterCount * resolution - visibleTicks);

        // 计算 KeyOffset 的最大值：使 C0 恰好出现在屏幕底部
        // 当 tone=0 时，y = (MaxTone - 1 - 0 - KeyOffset) * KeyHeight = screenHeight
        // => KeyOffset = MaxTone - 1 - screenHeight / KeyHeight
        double visibleKeys = 0;
        if (_noteAreaHeight > 0)
        {
            visibleKeys = KeyHeight > 0 ? _noteAreaHeight / KeyHeight : 0;
        }

        _cachedMaxKeyOffset = Math.Max(0, ViewConstants.MaxTone - visibleKeys);
        _maxOffsetsDirty = false;
    }

    /// <summary>
    /// 播放标记的屏幕 X 坐标（始终固定在画布水平中央）。
    /// </summary>
    private double PlayMarkerX => _noteAreaWidth / 2.0;

    /// <summary>
    /// TickOffset 的最小值：使播放标记能恰好对齐绝对 Tick 0。
    /// 即 TickOffset_min = 0 - PlayMarkerX / TickWidth = -PlayMarkerX / TickWidth。
    /// 允许负值，意味着 Tick 0 可以出现在画布右半侧。
    /// </summary>
    private double MinTickOffset => TickWidth > 0 ? -PlayMarkerX / TickWidth : 0;

    /// <summary>
    /// 将所有视口控制量 Clamp 到合法范围。
    /// 所有修改 TickWidth / TickOffset / KeyHeight / KeyOffset 的入口赋值后都必须调用此方法。
    /// </summary>
    public void ApplyViewportLimits()
    {
        TickWidth = Math.Clamp(TickWidth, ViewConstants.PianoRollTickWidthMin, ViewConstants.PianoRollTickWidthMax);
        KeyHeight = Math.Clamp(KeyHeight, ViewConstants.NoteHeightMin, ViewConstants.NoteHeightMax);
        EnsureMaxOffsets();
        TickOffset = Math.Clamp(TickOffset, MinTickOffset, _cachedMaxTickOffset);
        KeyOffset = Math.Clamp(KeyOffset, 0, _cachedMaxKeyOffset);
    }

    /// <summary>
    /// 置脏并重新校验；同时保持「播放标记中心 Tick」不变，
    /// 以应对屏幕旋转等布局变化。
    /// </summary>
    public void OnNoteAreaSizeChanged(double width, double height)
    {
        // 在宽度/高度变化之前，记住当前播放标记对应的中心 Tick（即后端 PlayPosTick）。
        // 布局变化不改变播放位置，只需根据新宽度重算 TickOffset。
        _noteAreaWidth = width;
        _noteAreaHeight = height;
        PlayMarkerScreenX = PlayMarkerX;
        InvalidateMaxOffsets();
        // 重新定位：让 PlayPosTick 仍对齐新画布中央
        if (TickWidth > 0)
            TickOffset = PlayPosTick - PlayMarkerX / TickWidth;
        ApplyViewportLimits();
    }

    private async Task UpdatePortraitAsync()
    {
        // 1. 取消正在执行的旧任务（如果存在），不再让 CPU 做无用功
        _portraitCts?.Cancel();
        _portraitCts?.Dispose();
        _portraitCts = new CancellationTokenSource();
        var token = _portraitCts.Token;

        // 2. 捕获当前请求的 VoicePart
        UVoicePart? targetPart = EditingVoicePart;

        // 3. 内存优化：清空 UI 并立即释放上一张立绘的内存（防止频繁切换时 OOM）
        Bitmap? oldBitmap = PortraitBitmap;
        PortraitBitmap = null;
        oldBitmap?.Dispose();

        if (targetPart == null || !Preferences.Default.ShowPortrait)
        {
            return;
        }

        try
        {
            // 4. 线程安全优化：在切换到后台线程前，先在 UI 线程获取 Singer
            // 避免在 Task.Run 里读取 DocManager 导致多线程集合冲突
            USinger? singer = DocManager.Inst.Project.tracks[targetPart.trackNo].Singer;
            if (singer == null)
            {
                return;
            }

            // 5. 开启后台线程执行纯耗时操作（IO读写和解码）
            Bitmap? newBitmap = await Task.Run(() =>
            {
                // 加载二进制数据
                byte[]? data = singer.LoadPortrait(); // Core 项目又在乱写null了！！！

                // 如果已经被新操作取消，直接抛出异常终止，不进行后续昂贵的图片解码
                token.ThrowIfCancellationRequested();

                return ProcessPortraitData(data, singer.PortraitHeight);
            }, token);

            // 6. 状态二次校验：回到 UI 线程后，确认数据是否依然是最新的
            if (!token.IsCancellationRequested && EditingVoicePart == targetPart)
            {
                PortraitBitmap = newBitmap;
            }
            else
            {
                // 重点防御：如果在解码的这几百毫秒内，用户又切换了 Part，或者任务被取消
                // 此时 newBitmap 已经是废弃产物了，必须立刻销毁！
                newBitmap?.Dispose();
            }

            PortraitOpacity = singer.PortraitOpacity;
        }
        catch (OperationCanceledException)
        {
            // 任务被中断（比如用户切走了），这是预期的行为，直接忽略
        }
        catch (Exception e)
        {
            if (!token.IsCancellationRequested && EditingVoicePart == targetPart)
            {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(L.S("PianoRoll.PortraitLoadFailed"), e));
                Log.Error(e, "无法加载立绘");
            }
        }
    }

    // 优化后的图片处理核心方法：彻底杜绝隐式内存泄漏
    private static Bitmap? ProcessPortraitData(byte[]? data, int portraitHeight)
    {
        if (data == null || data.Length == 0)
        {
            return null;
        }

        using MemoryStream stream = new(data);

        // 1. 先加载原图（此时原图在底层 Skia/系统 中分配了内存）
        Bitmap originalBitmap = new(stream);

        int targetHeight;
        if (portraitHeight == 0)
        {
            if (originalBitmap.Size.Height > 800)
            {
                targetHeight = 800;
            }
            else
            {
                // 无需缩放，直接返回原图（交由外部统一管理生命周期）
                return originalBitmap;
            }
        }
        else
        {
            targetHeight = portraitHeight;
            // 补充优化：如果指定的高度和原图高度碰巧一样，直接返回，避免无意义的新建
            if (originalBitmap.Size.Height == targetHeight)
            {
                return originalBitmap;
            }
        }

        // 2. 计算目标宽度 (注意先强转 double 进行计算，防止整数除法导致严重比例失调)
        int targetWidth = (int)Math.Round(targetHeight * originalBitmap.Size.Width / originalBitmap.Size.Height);
        if (targetWidth == 0)
        {
            targetWidth = 1;
        }

        // 3. 创建缩放后的新图
        Bitmap scaledBitmap = originalBitmap.CreateScaledBitmap(new PixelSize(targetWidth, targetHeight));

        // 4. 【核心修复】立刻销毁未缩放的大尺寸原图！
        // 否则每次进行图像缩放，都会有一张未压缩的高清大图残留在内存中，GC回收不及时就会OOM闪退。
        originalBitmap.Dispose();

        return scaledBitmap;
    }

    /// <summary>
    /// 由 EditorViewModel 同步播放位置与状态到钢琴卷帘。
    /// </summary>
    public void SyncPlaybackState(int tick, bool isPlaying, bool isWaitingRender)
    {
        PlayPosTick = tick;
        IsPlaying = isPlaying;
        IsWaitingRender = isWaitingRender;

        if (TickWidth > 0)
            TickOffset = tick - PlayMarkerX / TickWidth;
        ApplyViewportLimits();
    }

    public event Action? RequestInvalidateVisual; // 请求视图重绘

    /// <summary>
    /// 请求打开歌词编辑弹窗。
    /// 参数：(EditingVoicePart, 当前音符在 Part.notes 中的索引)
    /// </summary>
    public event Action<UVoicePart, int>? RequestEditLyric;

    // 拆分放大镜事件
    public event Action<Point>? RequestMagnifierOpen;
    public event Action<Point>? RequestMagnifierUpdate;
    public event Action? RequestMagnifierClose;

    // ── 构造函数 ──────────────────────────────────────────────────────

    public PianoRollViewModel()
    {
        _toneTimer = new DispatcherTimer();
        _toneTimer.Tick += (_, _) => { StopPreviewTone(); };

        DocManager.Inst.AddSubscriber(this);

        PlayPosTick = DocManager.Inst.playPosTick;

        Gesture.Tap = OnGestureTap;
        Gesture.DoubleTap = OnGestureDoubleTap;
        Gesture.DragBegin = OnGestureDragBegin;
        Gesture.DragUpdate = (start, step, total, current, ts) =>
        {
            OnGestureDragUpdate(start, step, total, current, ts);
            RequestInvalidateVisual?.Invoke();
        };
        Gesture.DragEnd = (end, _, ts) => OnGestureDragEnd(end, ts);
        Gesture.PinchUpdate = (scaleX, scaleY, center, panDelta) =>
        {
            OnGesturePinch(scaleX, scaleY, center, panDelta);
            RequestInvalidateVisual?.Invoke();
        };
        Gesture.PinchEnd = SyncPlayPosFromViewportCenter;
        Gesture.TwoFingerTap = OnTwoFingerTap;
        Gesture.ThreeFingerTap = OnThreeFingerTap;

        _panMotion.PanDelta = ApplyPanDeltaFromMotion;
        _panMotion.MotionCompleted = interrupted =>
        {
            WasPanMotionInterrupted = interrupted;
            if (!interrupted && !IsPlaying)
            {
                SyncPlayPosFromViewportCenter();
            }
        };

        this.WhenAnyValue(x => x.EditingVoicePart)
            .Subscribe(_ =>
            {
                this.RaisePropertyChanged(nameof(IsVoiceMode));
                InvalidateMaxOffsets();
                ApplyViewportLimits();
                var __ = UpdatePortraitAsync(); // 更新立绘
            })
            .DisposeWith(_disposables);

        this.WhenAnyValue(x => x.EditingWavePart)
            .Subscribe(_ =>
            {
                this.RaisePropertyChanged(nameof(IsWaveMode));
                InvalidateMaxOffsets();
                ApplyViewportLimits();
            })
            .DisposeWith(_disposables);
        // 编辑模式和橡皮擦模式切换
        this.WhenAnyValue(x => x.EditMode, x => x.IsPitchEraserMode)
            .Subscribe(_ =>
            {
                if (EditMode != PianoRollEditMode.PitchPen)
                {
                    ResetPitchDrawPointerState();
                }

                RebuildPianoRollContextActions();
                RequestInvalidateVisual?.Invoke();
                switch (EditMode)
                {
                    case PianoRollEditMode.Vibrato:
                        ToastService.Enqueue(L.S("PianoRoll.Toast.Vibrato"));
                        break;
                    case PianoRollEditMode.PitchPen:
                        ToastService.Enqueue(IsPitchEraserMode
                            ? L.S("PianoRoll.Toast.PitchEraser")
                            : L.S("PianoRoll.Toast.PitchPen"));
                        break;
                    case PianoRollEditMode.Anchor:
                        ToastService.Enqueue(L.S("PianoRoll.Toast.AnchorEdit"));
                        break;
                    case PianoRollEditMode.Hand:
                        ToastService.Enqueue(L.S("PianoRoll.Toast.DragMode"));
                        break;
                    case PianoRollEditMode.MultiSelect:
                        ToastService.Enqueue(L.S("PianoRoll.Toast.MultiSelectMode"));
                        break;
                    case PianoRollEditMode.Note:
                        ToastService.Enqueue(L.S("PianoRoll.Toast.NoteMode"));
                        break;
                }

                EditingTip = string.Empty;
            })
            .DisposeWith(_disposables);

        this.WhenAnyValue(x => x.EditingVoicePart)
            .Subscribe(_ =>
            {
                InvalidateMaxOffsets();
            })
            .DisposeWith(_disposables);

        SelectedNotes.ObserveCollectionChanges()
            .Subscribe(_ =>
            {
                RebuildPianoRollContextActions();
                RequestInvalidateVisual?.Invoke();
            })
            .DisposeWith(_disposables);

        this.WhenAnyValue(x => x.EditMode)
            .Subscribe(_ =>
            {
                RebuildPianoRollContextActions();
            })
            .DisposeWith(_disposables);

        SelectedAnchors.ObserveCollectionChanges()
            .Subscribe(_ =>
            {
                RebuildPianoRollContextActions();
                RequestInvalidateVisual?.Invoke(); // SelectedAnchors 变化立即重绘高亮
            })
            .DisposeWith(_disposables);

        // 上下文菜单展开时触发重建
        this.WhenAnyValue(x => x.IsContextMenuExpanded)
            .Where(expanded => expanded) // 仅展开时触发
            .Subscribe(_ => RebuildPianoRollContextActions())
            .DisposeWith(_disposables);
    }

    private void StopPreviewTone()
    {
        _toneTimer?.Stop();
        TonePlayer.StopAll();
    }

    private void PlayCreatedNotePreview(UNote note)
    {
        if (_toneTimer == null || EditingVoicePart == null)
        {
            return;
        }

        StopPreviewTone();

        int startTick = EditingVoicePart.position + note.position;
        int endTick = EditingVoicePart.position + note.End;
        double durationMs = DocManager.Inst.Project.timeAxis.TickPosToMsPos(endTick) -
                            DocManager.Inst.Project.timeAxis.TickPosToMsPos(startTick);
        if (durationMs <= 0)
        {
            durationMs = 1;
        }

        TonePlayer.PlayNoteOn(note.tone);
        _toneTimer.Interval = TimeSpan.FromMilliseconds(durationMs);
        _toneTimer.Start();
    }

    #region 辅助方法（坐标转换、吸附计算、音高采样等）

    // —————— 数据转屏幕 ————————————————
    /// <summary>
    /// 将 Tick（相对于项目） 和 Pitch（double） 转换为屏幕坐标 Point
    /// </summary>
    public Point TickPitchToPoint(int tick, double pitch)
    {
        return new Point(
            (tick - TickOffset) * TickWidth,
            (ViewConstants.MaxTone - 1 - pitch - KeyOffset) * KeyHeight);
    }

    /// <summary>
    /// 将 Tick 转换为屏幕坐标 X
    /// </summary>
    /// <param name="tick"></param>
    /// <returns></returns>
    public double TickToPointX(int tick) => (tick - TickOffset) * TickWidth;

    /// <summary>
    /// 将 Tick 和 Tone 转换为屏幕坐标 Size
    /// </summary>
    public Size TickToneToSize(double ticks, double tone)
    {
        return new Size(ticks * TickWidth, tone * KeyHeight);
    }

    // —————— 屏幕转数据 ————————————————
    /// <summary>屏幕 X → Tick</summary>
    public int PointXToTick(double x) => (int)(x / TickWidth + TickOffset);

    /// <summary>屏幕 Y → Pitch (半音，含小数)</summary>
    public double PointYToPitch(double y) => Math.Max(0, ViewConstants.MaxTone - 0.5 - y / KeyHeight - KeyOffset);

    /// <summary>屏幕 Y → 最近整数 Tone</summary>
    public int PointYToToneInt(double y) => (int)Math.Round(PointYToPitch(y));

    // —————— 其他计算 ————————————————
    /// <summary>
    /// 将 Tick 吸附到最近的前一个格点，格点间距由 PianoRollSnapDiv 决定。
    /// </summary>
    public int SnapToFloor(int tick)
    {
        int snapUnit = ResolveSnapUnit();
        if (snapUnit <= 0) return tick;
        return (int)(Math.Floor((double)tick / snapUnit) * snapUnit);
    }

    /// <summary>
    /// 将 Tick 吸附到最近的格点，格点间距由 PianoRollSnapDiv 决定。
    /// </summary>
    public int SnapToRound(int tick)
    {
        int snapUnit = ResolveSnapUnit();
        if (snapUnit <= 0) return tick;
        return (int)(Math.Round((double)tick / snapUnit) * snapUnit);
    }

    /// <summary>
    /// 根据 PianoRollSnapDiv 计算实际吸附单元（Tick 数）。
    /// 返回 0 表示关闭吸附（PianoRollSnapDiv == 0）。
    /// </summary>
    public int ResolveSnapUnit()
    {
        if (PianoRollSnapDiv == 0) return 0; // 关闭吸附
        int resolution = DocManager.Inst.Project.resolution;
        if (PianoRollSnapDiv < 0) // 自动
        {
            double minTicks = TickWidth > 0 ? ViewConstants.PianoRollMinTicklineWidth / TickWidth : resolution;
            MusicMath.GetSnapUnit(resolution, minTicks, triplet: false, out int ticks, out _);
            return ticks;
        }

        return resolution * 4 / PianoRollSnapDiv; // 手动指定
    }

    /// <summary>
    /// 根据给定的 Tick（相对Part），采样该位置的音高（音分）。
    /// </summary>
    /// <param name="tick"></param>
    /// <returns></returns>
    public double? SamplePitchAtTick(int tick)
    {
        if (EditingVoicePart == null)
        {
            return null;
        }

        UNote? note = EditingVoicePart.notes.FirstOrDefault(n => n.End >= tick);
        if (note == null && EditingVoicePart.notes.Count > 0)
        {
            note = EditingVoicePart.notes.Last(); // 如果没有找到结束位置在 tick 之后的音符且有音符，取最后一个音符（可能在 tick 之前）
        }

        if (note == null)
        {
            return null;
        }

        double pitch = note.AdjustedTone * 100;
        pitch += note.pitch.Sample(DocManager.Inst.Project, EditingVoicePart, note, tick) ?? 0;
        if (note.Next != null && note.Next.position == note.End) // Core 项目又在乱写 null，加个保护
        {
            double? delta = note.Next.pitch.Sample(DocManager.Inst.Project, EditingVoicePart, note.Next, tick);
            if (delta != null)
            {
                pitch += delta.Value + note.Next.AdjustedTone * 100 - note.AdjustedTone * 100;
            }
        }

        return pitch;
    }

    #endregion

    #region 命中测试

    /// <summary>
    /// 哨兵音符：仅用于 SortedSet.GetViewBetween 的范围端点，不代表真实音符。
    /// GetHashCode() 返回 int.MinValue，确保同 position 时哨兵排在所有真实音符之前，
    /// 从而使 hiSentinel(?tick?) 作为右端点时能把所有 position == ?tick? 的真实音符纳入视图。
    /// </summary>
    private sealed class SentinelNote : UNote
    {
        public SentinelNote(int position)
        {
            this.position = position;
        }

        public override int GetHashCode() => int.MinValue;
    }

    // 预分配哨兵，避免每次命中测试触发 GC
    private readonly SentinelNote _hitTestLoSentinel = new(0);
    private readonly SentinelNote _hitTestHiSentinel = new(0);

    public UNote? HitTestNote(Point canvasPoint)
    {
        if (EditingVoicePart == null)
        {
            return null;
        }

        int tick = PointXToTick(canvasPoint.X) - EditingVoicePart.position;
        if (tick < 0)
        {
            return null;
        }

        int toneInt = PointYToToneInt(canvasPoint.Y);

        // 利用 SortedSet<UNote> 按 position 升序排列的特性，将候选集截断至 position ≤ tick。
        // position 为整数，position ≤ tick 等价于 position ≤ ?tick?。
        // 端点定位 O(log n)，随后遍历子视图 O(k)，k 为 position ≤ tick 的音符数。
        // x 轴可能存在重叠（OverlapError），需遍历子视图全部元素确保正确性。
        _hitTestHiSentinel.position = tick;
        foreach (UNote note in EditingVoicePart.notes.GetViewBetween(_hitTestLoSentinel, _hitTestHiSentinel))
        {
            if (note.tone == toneInt && tick < note.End)
                return note;
        }

        return null;
    }

    /// <summary>
    /// 对选中音符右侧外部手柄区域进行命中测试。
    /// 命中区域：音符右边缘 → 右边缘 + NoteResizeHandleHitWidth，完整 KeyHeight 高度。
    /// 返回命中的音符及其在 SelectedNotes 中的下标；未命中返回 null。
    /// </summary>
    public (UNote note, int index)? HitTestNoteResizeHandle(Point canvasPoint)
    {
        if (EditingVoicePart == null || SelectedNotes.Count == 0) return null;

        for (int i = 0; i < SelectedNotes.Count; i++)
        {
            UNote note = SelectedNotes[i];
            // 音符右边缘屏幕 X（绝对 tick = Part.position + note.End）
            double noteRight = TickToPointX(EditingVoicePart.position + note.End);
            // 手柄命中左边缘：紧贴音符右侧（gap 内也算命中，保证触摸友好）
            double hitLeft = noteRight;
            double hitRight = noteRight + ViewConstants.NoteResizeHandleHitWidth;

            // 音符顶部/底部（AdjustedTone 为半音整数）
            double noteTop = TickPitchToPoint(EditingVoicePart.position + note.position, note.AdjustedTone).Y;
            double noteBottom = noteTop + KeyHeight;

            if (canvasPoint.X >= hitLeft && canvasPoint.X <= hitRight &&
                canvasPoint.Y >= noteTop && canvasPoint.Y <= noteBottom)
            {
                return (note, i);
            }
        }

        return null;
    }

    private Point GetPitchPointCanvasPoint(UNote note, PitchPoint pitchPoint)
    {
        UProject project = DocManager.Inst.Project;
        int absTick = project.timeAxis.MsPosToTickPos(note.PositionMs + pitchPoint.X);
        double pitch = note.AdjustedTone + pitchPoint.Y / 10.0;
        return TickPitchToPoint(absTick, pitch - 0.5);
    }

    private PitchPointHit? HitTestPitchPoint(Point canvasPoint)
    {
        if (EditingVoicePart == null)
        {
            return null;
        }

        double hitRadius = Math.Clamp(KeyHeight * 0.60, 14, 20);
        double minDistanceSquared = hitRadius * hitRadius;
        PitchPointHit? bestHit = null;

        // Narrow candidates to notes whose tick range intersects canvasPoint.X ± hitRadius
        int loTick = PointXToTick(canvasPoint.X - hitRadius - _pitchPointHitMargin) - EditingVoicePart.position;
        _hitTestLoSentinel.position = loTick;
        _hitTestHiSentinel.position = PointXToTick(canvasPoint.X + hitRadius + _pitchPointHitMargin) - EditingVoicePart.position;
        foreach (UNote note in EditingVoicePart.notes.GetViewBetween(_hitTestLoSentinel, _hitTestHiSentinel))
        {
            if (note.End < loTick)
            {
                continue;
            }

            for (int i = 0; i < note.pitch.data.Count; i++)
            {
                PitchPoint pitchPoint = note.pitch.data[i];
                Point point = GetPitchPointCanvasPoint(note, pitchPoint);
                double deltaX = point.X - canvasPoint.X;
                double deltaY = point.Y - canvasPoint.Y;
                double distanceSquared = deltaX * deltaX + deltaY * deltaY;
                if (distanceSquared > minDistanceSquared)
                {
                    continue;
                }

                minDistanceSquared = distanceSquared;
                bestHit = new PitchPointHit(note, pitchPoint, i);
            }
        }

        return bestHit;
    }

    private PitchCurveHit? HitTestPitchCurve(Point canvasPoint)
    {
        if (EditingVoicePart == null)
        {
            return null;
        }

        const float minAnchorInsertGapMs = 1f;
        UProject project = DocManager.Inst.Project;
        double hitThreshold = Math.Clamp(KeyHeight * 0.52, 12, 18);
        double minDistanceSquared = hitThreshold * hitThreshold;
        PitchCurveHit? bestHit = null;

        // Narrow candidates to notes whose tick range intersects canvasPoint.X ± hitThreshold
        int loTick = PointXToTick(canvasPoint.X - hitThreshold - _pitchPointHitMargin) - EditingVoicePart.position;
        _hitTestLoSentinel.position = loTick;
        _hitTestHiSentinel.position = PointXToTick(canvasPoint.X + hitThreshold + _pitchPointHitMargin) - EditingVoicePart.position;
        foreach (UNote note in EditingVoicePart.notes.GetViewBetween(_hitTestLoSentinel, _hitTestHiSentinel))
        {
            if (note.End < loTick || note.pitch.data.Count < 2)
            {
                continue;
            }

            for (int i = 0; i < note.pitch.data.Count - 1; i++)
            {
                PitchPoint startPoint = note.pitch.data[i];
                PitchPoint endPoint = note.pitch.data[i + 1];
                Point startCanvas = GetPitchPointCanvasPoint(note, startPoint);
                Point endCanvas = GetPitchPointCanvasPoint(note, endPoint);

                // Cheap bounding-box rejection before sampling
                double segMinX = Math.Min(startCanvas.X, endCanvas.X);
                double segMaxX = Math.Max(startCanvas.X, endCanvas.X);
                if (segMaxX < canvasPoint.X - hitThreshold || segMinX > canvasPoint.X + hitThreshold)
                {
                    continue;
                }
                int sampleCount = Math.Clamp((int)Math.Ceiling(Math.Abs(endCanvas.X - startCanvas.X) / 4.0), 4, 96);
                Point previousSample = startCanvas;

                for (int sample = 1; sample <= sampleCount; sample++)
                {
                    double progress = (double)sample / sampleCount;
                    float sampleXMs = (float)(startPoint.X + (endPoint.X - startPoint.X) * progress);
                    double sampleY = MusicMath.InterpolateShape(
                        startPoint.X,
                        endPoint.X,
                        startPoint.Y,
                        endPoint.Y,
                        sampleXMs,
                        startPoint.shape);
                    int sampleTick = project.timeAxis.MsPosToTickPos(note.PositionMs + sampleXMs);
                    Point currentSample = TickPitchToPoint(sampleTick, note.AdjustedTone + sampleY / 10.0 - 0.5);
                    double distanceSquared = DistanceSquaredToSegment(canvasPoint, previousSample, currentSample);

                    if (distanceSquared < minDistanceSquared)
                    {
                        int targetTick = PointXToTick(canvasPoint.X);
                        double targetAbsMs = project.timeAxis.TickPosToMsPos(targetTick);
                        float targetRelX = (float)Math.Clamp(targetAbsMs - note.PositionMs, startPoint.X, endPoint.X);
                        if (targetRelX > startPoint.X + minAnchorInsertGapMs &&
                            targetRelX < endPoint.X - minAnchorInsertGapMs)
                        {
                            float targetY = (float)MusicMath.InterpolateShape(
                                startPoint.X,
                                endPoint.X,
                                startPoint.Y,
                                endPoint.Y,
                                targetRelX,
                                startPoint.shape);
                            minDistanceSquared = distanceSquared;
                            bestHit = new PitchCurveHit(note, i + 1, targetRelX, targetY, startPoint.shape);
                        }
                    }

                    previousSample = currentSample;
                }
            }
        }

        return bestHit;
    }

    private static double DistanceSquaredToSegment(Point point, Point start, Point end)
    {
        double deltaX = end.X - start.X;
        double deltaY = end.Y - start.Y;
        if (Math.Abs(deltaX) < 0.001 && Math.Abs(deltaY) < 0.001)
        {
            double offsetX = point.X - start.X;
            double offsetY = point.Y - start.Y;
            return offsetX * offsetX + offsetY * offsetY;
        }

        double t = ((point.X - start.X) * deltaX + (point.Y - start.Y) * deltaY) /
            (deltaX * deltaX + deltaY * deltaY);
        t = Math.Clamp(t, 0.0, 1.0);
        double nearestX = start.X + deltaX * t;
        double nearestY = start.Y + deltaY * t;
        double distX = point.X - nearestX;
        double distY = point.Y - nearestY;
        return distX * distX + distY * distY;
    }

    public VibratoOverlayLayout? GetActiveVibratoOverlayLayout()
    {
        if (EditMode != PianoRollEditMode.Vibrato || EditingVoicePart == null || SelectedNotes.Count == 0)
        {
            return null;
        }

        return BuildVibratoOverlayLayout(SelectedNotes[0]);
    }

    private VibratoOverlayLayout? BuildVibratoOverlayLayout(UNote note)
    {
        if (EditingVoicePart == null)
        {
            return null;
        }

        double noteLeft = TickToPointX(EditingVoicePart.position + note.position);
        double noteRight = TickToPointX(EditingVoicePart.position + note.End);
        double noteWidth = noteRight - noteLeft;
        if (noteWidth <= 8 || KeyHeight <= 6)
        {
            return null;
        }

        double noteTop = TickPitchToPoint(EditingVoicePart.position + note.position, note.AdjustedTone).Y;
        Rect noteRect = new(noteLeft, noteTop, noteWidth, KeyHeight);

        UVibrato visualVibrato;
        if (note.vibrato.length <= 0f)
        {
            visualVibrato = note.vibrato.Clone();
            visualVibrato.length = NotePresets.Default.DefaultVibrato.VibratoLength;
        }
        else
        {
            visualVibrato = note.vibrato;
        }

        float effectiveLength = visualVibrato.length;
        double waveEndX = noteRight;
        double waveWidth = Math.Max(noteWidth * effectiveLength / 100.0, Math.Min(noteWidth, 20));
        double waveStartX = Math.Max(noteLeft, waveEndX - waveWidth);
        waveWidth = waveEndX - waveStartX;

        double handleRadius = 5.0;
        double guideTopY = noteRect.Bottom + Math.Clamp(KeyHeight * 0.55, 16, 24);
        double guideBottomY = guideTopY + Math.Clamp(KeyHeight * 0.90, 28, 44);
        double fadeInX = waveStartX + waveWidth * visualVibrato.@in / 100.0;
        double fadeOutX = waveEndX - waveWidth * visualVibrato.@out / 100.0;
        fadeInX = Math.Clamp(fadeInX, waveStartX, waveEndX);
        fadeOutX = Math.Clamp(fadeOutX, fadeInX, waveEndX);

        double phaseTrackWidth = Math.Clamp(waveWidth * 0.42, 34, Math.Max(36, waveWidth - 12));
        phaseTrackWidth = Math.Min(phaseTrackWidth, waveWidth);
        double phaseTrackX = waveStartX + (waveWidth - phaseTrackWidth) * 0.5;
        double phaseTrackY = guideBottomY + handleRadius * 2.10;
        Rect phaseTrackRect = new(
            phaseTrackX,
            phaseTrackY,
            Math.Max(phaseTrackWidth, 24),
            Math.Clamp(handleRadius * 1.30, 8, 10));
        double phaseHandleX = phaseTrackRect.Left + phaseTrackRect.Width * visualVibrato.shift / 100.0;
        Point phaseHandlePoint = new(phaseHandleX, phaseTrackRect.Y + phaseTrackRect.Height * 0.5);

        float driftSemitone = visualVibrato.depth * visualVibrato.drift / 10000f;
        double waveCenterY = TickPitchToPoint(
            EditingVoicePart.position + note.position,
            note.AdjustedTone + driftSemitone - 0.5).Y;
        Point driftAnchorPoint = new(waveEndX, waveCenterY);
        Point driftHandlePoint = new(waveEndX + Math.Clamp(handleRadius * 1.45, 9, 13), waveCenterY);

        Rect startHitRect = new(
            waveStartX - Math.Clamp(handleRadius * 0.7, 5, 7),
            noteRect.Top - KeyHeight * 0.08,
            Math.Clamp(handleRadius * 1.4, 10, 14),
            guideBottomY - noteRect.Top + handleRadius * 1.60);

        double bodyTop = noteRect.Top - KeyHeight * 0.42;
        double bodyBottom = Math.Max(phaseTrackRect.Bottom + handleRadius * 1.60, noteRect.Bottom + KeyHeight * 2.0);

        Rect bodyRect = new(
            waveStartX - handleRadius * 0.9,
            bodyTop,
            waveWidth + handleRadius * 1.8,
            bodyBottom - bodyTop);

        return new VibratoOverlayLayout(
            note,
            note.vibrato.length > 0f,
            noteRect,
            bodyRect,
            startHitRect,
            CreateHandleRect(new Point(fadeInX, guideTopY), handleRadius),
            CreateHandleRect(new Point(fadeOutX, guideTopY), handleRadius),
            phaseTrackRect,
            CreateHandleRect(phaseHandlePoint, handleRadius),
            CreateHandleRect(driftHandlePoint, handleRadius),
            new Point(waveStartX, guideBottomY),
            new Point(fadeInX, guideTopY),
            new Point(fadeOutX, guideTopY),
            new Point(waveEndX, guideBottomY),
            driftAnchorPoint,
            driftHandlePoint,
            waveStartX,
            waveEndX,
            waveCenterY);
    }

    private VibratoOverlayHit? HitTestVibratoControl(Point point)
    {
        VibratoOverlayLayout? maybeLayout = GetActiveVibratoOverlayLayout();
        if (maybeLayout is not VibratoOverlayLayout layout)
        {
            return null;
        }

        if (ExpandRect(layout.PhaseHandleRect, 8, 8).Contains(point) ||
            ExpandRect(layout.PhaseTrackRect, 4, 8).Contains(point))
        {
            return new VibratoOverlayHit(layout.Note, VibratoHandleKind.Phase, layout);
        }

        if (ExpandRect(layout.DriftHandleRect, 8, 8).Contains(point))
        {
            return new VibratoOverlayHit(layout.Note, VibratoHandleKind.Drift, layout);
        }

        if (ExpandRect(layout.FadeInHandleRect, 8, 8).Contains(point))
        {
            return new VibratoOverlayHit(layout.Note, VibratoHandleKind.FadeIn, layout);
        }

        if (ExpandRect(layout.FadeOutHandleRect, 8, 8).Contains(point))
        {
            return new VibratoOverlayHit(layout.Note, VibratoHandleKind.FadeOut, layout);
        }

        if (ExpandRect(layout.StartHandleRect, 6, 4).Contains(point))
        {
            return new VibratoOverlayHit(layout.Note, VibratoHandleKind.Start, layout);
        }

        if (ExpandRect(layout.BodyRect, 6, 6).Contains(point))
        {
            return new VibratoOverlayHit(layout.Note, VibratoHandleKind.Body, layout);
        }

        return null;
    }

    private static Rect CreateHandleRect(Point center, double radius)
    {
        return new Rect(center.X - radius, center.Y - radius, radius * 2, radius * 2);
    }

    private static Rect ExpandRect(Rect rect, double horizontalPadding, double verticalPadding)
    {
        return new Rect(
            rect.X - horizontalPadding,
            rect.Y - verticalPadding,
            rect.Width + horizontalPadding * 2,
            rect.Height + verticalPadding * 2);
    }

    #endregion

    #region 手势响应 (由 NotesCanvas 视图层 调用)

    private PianoRollInputState _inputState = PianoRollInputState.Idle; // 当前输入状态机状态
    private (int tick, int tone)[] _movingNotesOrigins = []; // 拖拽开始时所有选中音符的位置快照，长度与 SelectedNotes 保持一致
    private double? _lastPitch; // 音高画笔上次采样的音高，用于计算增量
    private int _lastTick; // 音高画笔上次采样的 Tick，用于计算增量，相对 Part.position

    // ── ResizingNotes 专用状态 ───────────────────────────────────────────────────
    /// <summary>拖拽开始时各选中音符的 duration 快照，索引与 SelectedNotes 对齐。</summary>
    private int[] _resizingNotesOriginDurations = [];

    /// <summary>触发手柄命中的音符在 SelectedNotes 中的下标，用于吸附基准计算。</summary>
    private int _resizingReferenceNoteIndex;

    // ── Anchor 模式专用状态 ──────────────────────────────────────────────────────
    /// <summary>
    /// 拖拽开始时各选中锚点的初始 (X ms, Y centitone) 快照。
    /// 索引与 SelectedAnchors 保持一致。
    /// </summary>
    private (float X, float Y)[] _movingAnchorsOrigins = [];

    /// <summary>
    /// 拖拽开始时各选中锚点所属的音符快照。
    /// 需要音符才能计算首/末点的固定 Y 值及 X 相邻约束。
    /// </summary>
    private UNote[] _movingAnchorsNotes = [];

    /// <summary>
    /// 拖拽开始时各选中锚点在所属音符 pitch.data 中的索引快照。
    /// 用于判断首点（index == 0）和末点（index == data.Count - 1）。
    /// </summary>
    private int[] _movingAnchorsIndices = [];

    // ── Vibrato 模式专用状态 ─────────────────────────────────────────────────────
    private UNote? _editingVibratoNote;
    private VibratoHandleKind? _editingVibratoHandle;
    private float _editingVibratoOriginDepth;
    private float _editingVibratoOriginPeriod;
    private float _editingVibratoOriginLength;
    private float _editingVibratoOriginFadeIn;
    private float _editingVibratoOriginFadeOut;
    private float _editingVibratoOriginShift;
    private float _editingVibratoOriginDrift;
    private double _editingVibratoNoteWidth;
    private double _editingVibratoWaveWidth;
    private double _editingVibratoPhaseTrackWidth;
    private double _editingVibratoDurationMs;

    /// <summary>
    /// 单击事件
    /// </summary>
    /// <param name="point"></param>
    public void OnGestureTap(Point point)
    {
        InterruptPanMotionIfRunning();
        if (EditingVoicePart == null) return;
        UNote? hit = HitTestNote(point);

        switch (EditMode)
        {
            // 手型模式
            case PianoRollEditMode.Hand:
                break;
            // 多选模式
            case PianoRollEditMode.MultiSelect:
                if (hit != null)
                {
                    // 反选
                    if (!SelectedNotes.Remove(hit))
                    {
                        SelectedNotes.Add(hit);
                    }
                }
                else
                {
                    // 清空已选中的音符
                    SelectedNotes.Clear();
                }

                break;
            // 音符模式
            case PianoRollEditMode.Note:
                if (hit == null) // 空白处点击
                {
                    if (SelectedNotes.Count == 0)
                    {
                        SelectedNotes.Clear();
                        int tick = PointXToTick(point.X); // 绝对 Tick
                        tick = SnapToFloor(tick);

                        if (tick < EditingVoicePart.position)
                        {
                            return;
                        }

                        int tone = PointYToToneInt(point.Y);

                        int snapUnit = ResolveSnapUnit();
                        if (snapUnit <= 0) snapUnit = DocManager.Inst.Project.resolution; // 关闭吸附时默认四分音符
                        UNote newNote =
                            DocManager.Inst.Project.CreateNote(tone, tick - EditingVoicePart.position, snapUnit);
                        DocManager.Inst.StartUndoGroup();
                        DocManager.Inst.ExecuteCmd(new AddNoteCommand(EditingVoicePart, newNote));
                        DocManager.Inst.EndUndoGroup();
                        PlayCreatedNotePreview(newNote); // 播放测试音
                        SelectedNotes.Add(newNote);
                    }
                    else
                    {
                        SelectedNotes.Clear();
                    }
                }
                else if (!SelectedNotes.Contains(hit))
                {
                    SelectedNotes.Clear();
                    SelectedNotes.Add(hit);
                }

                break;
            case PianoRollEditMode.PitchPen:
                SelectedAnchors.Clear();
                SelectedNotes.Clear();
                if (hit != null)
                {
                    SelectedNotes.Add(hit);
                }

                break;
            case PianoRollEditMode.Anchor:
                if (HitTestPitchPoint(point) is PitchPointHit anchorHit)
                {
                    SelectSingleAnchor(anchorHit.Note, anchorHit.Point);
                }
                else if (HitTestPitchCurve(point) is PitchCurveHit curveHit)
                {
                    InsertAnchorAtCurveHit(curveHit);
                }
                else
                {
                    SelectedAnchors.Clear();
                    SelectedNotes.Clear();
                    if (hit != null)
                    {
                        SelectedNotes.Add(hit);
                    }
                }

                break;
            case PianoRollEditMode.Vibrato:
                SelectedNotes.Clear();
                if (hit != null)
                {
                    SelectedNotes.Add(hit);
                }

                break;
            default:
                SelectedNotes.Clear();
                if (hit != null) SelectedNotes.Add(hit);
                break;
        }
    }

    /// <summary>
    /// 双击
    /// </summary>
    /// <param name="point"></param>
    public void OnGestureDoubleTap(Point point)
    {
        InterruptPanMotionIfRunning();
        if (EditingVoicePart == null)
        {
            return;
        }

        UNote? hit = HitTestNote(point);
        if (hit == null) return;

        if (EditMode == PianoRollEditMode.Vibrato)
        {
            SelectedNotes.Clear();
            SelectedNotes.Add(hit);
            ToggleVibrato(hit);
            return;
        }

        if (EditMode == PianoRollEditMode.Anchor || EditMode == PianoRollEditMode.Hand)
        {
            return;
        }

        // 非 Anchor/Hand 模式下双击音符选中并编辑歌词
        SelectedNotes.Clear();
        SelectedNotes.Add(hit);

        // 触发歌词编辑弹窗：传入 EditingVoicePart 和当前音符的索引
        int noteIndex = EditingVoicePart.notes.IndexOf(hit);
        if (noteIndex >= 0)
        {
            RequestEditLyric?.Invoke(EditingVoicePart, noteIndex);
        }
    }

    /// <summary>
    /// 开始拖拽，决策进入哪种拖拽状态
    /// </summary>
    /// <param name="point"></param>
    public void OnGestureDragBegin(Point point)
    {
        // 新手势进入时先打断惯性。
        InterruptPanMotionIfRunning();
        if (WasPanMotionInterrupted)
        {
            WasPanMotionInterrupted = false;
        }

        UNote? hit = HitTestNote(point);

        switch (EditMode)
        {
            case PianoRollEditMode.Hand:
                _inputState = PianoRollInputState.Panning;
                break;

            // 多选模式 || 音符画笔模式
            case PianoRollEditMode.MultiSelect:
            case PianoRollEditMode.Note:
                // 优先检测 resize 手柄（手柄在音符外侧，不与音符本体重叠，但需优先处理）
                (UNote note, int index)? resizeHit = SelectedNotes.Count > 0 ? HitTestNoteResizeHandle(point) : null;
                if (resizeHit.HasValue)
                {
                    StartToResizeNotes(resizeHit.Value.index);
                    return;
                }

                if (hit != null && SelectedNotes.Contains(hit)) // 拖拽已选中的音符
                {
                    StartToMoveNotes();
                }
                else // 移动画布
                {
                    _inputState = PianoRollInputState.Panning;
                }

                break;
            case PianoRollEditMode.PitchPen: // 音高线画笔
                if (EditingVoicePart == null)
                {
                    break;
                }

                _inputState = PianoRollInputState.DrawingPitch;
                IsPitchDrawingActive = true;
                PitchDrawPointer = point;
                _lastTick = PointXToTick(point.X) - EditingVoicePart.position; // 记录初始 Tick（相对 EditingVoicePart）
                // 开启放大镜
                RequestMagnifierOpen?.Invoke(point);

                DocManager.Inst.StartUndoGroup();
                RequestInvalidateVisual?.Invoke();
                break;
            case PianoRollEditMode.Anchor:
                if (HitTestPitchPoint(point) is PitchPointHit anchorHit)
                {
                    SelectSingleAnchor(anchorHit.Note, anchorHit.Point);
                    StartToMoveAnchors();
                }
                else if (HitTestPitchCurve(point) is PitchCurveHit curveHit)
                {
                    StartToInsertAndMoveAnchor(curveHit);
                }
                else
                {
                    SelectedAnchors.Clear();
                    _inputState = PianoRollInputState.Panning;
                }

                break;
            case PianoRollEditMode.Vibrato:
                if (HitTestVibratoControl(point) is VibratoOverlayHit vibratoHit)
                {
                    StartToEditVibrato(vibratoHit);
                }
                else
                {
                    _inputState = PianoRollInputState.Panning;
                }

                break;
        }
    }

    /// <summary>
    /// 准备开始拖拽音符：记录初始位置，进入 MovingNotes 状态
    /// </summary>
    private void StartToMoveNotes()
    {
        // 对选中的音符排序，按 position 升序（与 Part.notes 中的顺序一致），确保拖拽过程中位置关系不变。
        UNote[] sorted = SelectedNotes.OrderBy(n => n.position).ToArray();
        SelectedNotes.Clear();
        SelectedNotes.AddRange(sorted);
        // 记录所有选中分片的初始位置
        _movingNotesOrigins = new (int, int)[SelectedNotes.Count];
        for (int i = 0; i < SelectedNotes.Count; i++)
        {
            _movingNotesOrigins[i] = (SelectedNotes[i].position, SelectedNotes[i].tone);
        }

        _inputState = PianoRollInputState.MovingNotes;
        DocManager.Inst.StartUndoGroup(deferValidate: true);
    }

    /// <summary>
    /// 准备开始调整音符时长：记录所有选中音符的 duration 快照，进入 ResizingNotes 状态。
    /// </summary>
    /// <param name="referenceNoteIndex">触发命中的音符在 SelectedNotes 中的下标，用作吸附基准。</param>
    private void StartToResizeNotes(int referenceNoteIndex)
    {
        _resizingReferenceNoteIndex = referenceNoteIndex;
        _resizingNotesOriginDurations = new int[SelectedNotes.Count];
        for (int i = 0; i < SelectedNotes.Count; i++)
            _resizingNotesOriginDurations[i] = SelectedNotes[i].duration;
        _inputState = PianoRollInputState.ResizingNotes;
        DocManager.Inst.StartUndoGroup(deferValidate: true);
    }

    /// <summary>
    /// 更新拖拽
    /// </summary>
    /// <param name="point"></param>
    /// <param name="delta"></param>
    /// <param name="totalOffset"></param>
    /// <param name="currentPoint"></param>
    /// <param name="timestamp"></param>
    public void OnGestureDragUpdate(Point point, Vector delta, Vector totalOffset, Point currentPoint, ulong timestamp)
    {
        switch (_inputState)
        {
            // 移动分片状态
            case PianoRollInputState.MovingNotes:
                if (EditingVoicePart == null || SelectedNotes.Count == 0)
                {
                    return;
                }

                int tickTotal = (int)(totalOffset.X / TickWidth);
                int toneTotal = (int)(-totalOffset.Y / KeyHeight);
                // 将第一个音符吸附到格点（SnapToRound 在关闭吸附时直通，无需额外判断）
                int newPos0 = _movingNotesOrigins[0].tick + tickTotal + EditingVoicePart.position;
                newPos0 = SnapToRound(newPos0);
                tickTotal = newPos0 - _movingNotesOrigins[0].tick - EditingVoicePart.position;
                const int toneMax = ViewConstants.MaxTone - 1;
                // 第一轮遍历：预计算所有目标位置，任一越界则放弃本次移动
                (int pos, int tone)[] targets = new (int pos, int tone)[SelectedNotes.Count];
                for (int i = 0; i < SelectedNotes.Count; i++)
                {
                    (int originPos, int originTone) = _movingNotesOrigins[i];
                    int newPos = originPos + tickTotal;
                    int newTone = originTone + toneTotal;
                    if (newTone < 0 || newTone > toneMax || newPos < 0 ||
                        (newPos == SelectedNotes[i].position && newTone == SelectedNotes[i].tone))
                    {
                        return;
                    }

                    targets[i] = (newPos, newTone);
                }

                // 第二轮遍历：执行移动
                for (int i = 0; i < SelectedNotes.Count; i++)
                {
                    DocManager.Inst.ExecuteCmd(new MoveNoteCommand(
                        EditingVoicePart, SelectedNotes[i], targets[i].pos - SelectedNotes[i].position,
                        targets[i].tone - SelectedNotes[i].tone));
                }

                break;
            case PianoRollInputState.ResizingNotes:
                UpdateResizeNotes(totalOffset);
                break;
            case PianoRollInputState.Panning:
                if (!_panMotion.IsRunning)
                {
                    _panMotion.BeginDirectManipulation(timestamp);
                }

                _panMotion.UpdateDirectManipulation(delta, timestamp);
                break;
            case PianoRollInputState.DrawingPitch:
                PitchDrawPointer = currentPoint;
                // 更新放大镜
                RequestMagnifierUpdate?.Invoke(currentPoint);

                if (IsPitchEraserMode)
                {
                    UpdateErasingPitch(currentPoint);
                }
                else
                {
                    UpdateDrawingPitch(currentPoint);
                }

                break;
            case PianoRollInputState.MovingAnchors:
                UpdateMovingAnchors(totalOffset);
                break;
            case PianoRollInputState.EditingVibrato:
                UpdateEditingVibrato(totalOffset);
                break;
        }
    }

    /// <summary>
    /// 更新音高线绘制
    /// </summary>
    /// <param name="point"></param>
    private void UpdateDrawingPitch(Point point)
    {
        if (EditingVoicePart == null)
        {
            return;
        }

        int tick = PointXToTick(point.X) - EditingVoicePart.position; // 转换为分片内相对 Tick
        int sampleTick = (int)Math.Round(tick / 5f) * 5; // 以 5 Tick 为步进采样
        double drawingPitch = PointYToPitch(point.Y) * 100; // 绘制目标音高，单位为音分
        double? pitch = SamplePitchAtTick(sampleTick);
        if (pitch == null)
        {
            return;
        }

        // Debug.WriteLine($"tick={tick}, sampleTick={sampleTick}, drawingPitch={drawingPitch}, pitch={pitch}");
        DocManager.Inst.ExecuteCmd(new SetCurveCommand(
            DocManager.Inst.Project,
            EditingVoicePart,
            OpenUtau.Core.Format.Ustx.PITD,
            tick,
            (int)Math.Round(drawingPitch - pitch.Value),
            _lastPitch == null ? tick : _lastTick,
            (int)Math.Round(drawingPitch - (_lastPitch ?? pitch.Value))));
        _lastPitch = pitch;
        _lastTick = tick;
    }

    /// <summary>
    /// 更新音符调整时长拖拽。
    /// 以命中手柄的音符（referenceNote）的原始结束 Tick 为基准做吸附，
    /// 所有选中音符共用相同 deltaDur（仿照 EditorViewModel.UpdateResizePart）。
    /// </summary>
    private void UpdateResizeNotes(Vector totalOffset)
    {
        if (EditingVoicePart == null || _resizingNotesOriginDurations.Length == 0) return;

        int tickTotal = (int)(totalOffset.X / TickWidth);

        // 以触发手柄的音符为吸附基准
        UNote refNote = SelectedNotes[_resizingReferenceNoteIndex];
        int refOriginDur = _resizingNotesOriginDurations[_resizingReferenceNoteIndex];
        // 基准音符原始结束 tick（分片内相对）
        int rawEndTick = refNote.position + refOriginDur + tickTotal;
        // 吸附到格点（绝对 tick = Part.position + rawEndTick）
        int snappedEnd = SnapToRound(EditingVoicePart.position + rawEndTick) - EditingVoicePart.position;
        // Δ = 吸附后结束位置 - 基准音符当前结束位置
        int deltaDur = snappedEnd - refNote.End;
        if (deltaDur == 0) return;

        int snapUnit = ResolveSnapUnit();
        int minDuration = snapUnit > 0 ? snapUnit : 1;

        // 第一轮：验证所有音符，任一过短则整体放弃
        for (int i = 0; i < SelectedNotes.Count; i++)
        {
            if (SelectedNotes[i].duration + deltaDur < minDuration) return;
        }

        // 第二轮：对所有选中音符执行 ResizeNoteCommand
        for (int i = 0; i < SelectedNotes.Count; i++)
        {
            DocManager.Inst.ExecuteCmd(new ResizeNoteCommand(EditingVoicePart, SelectedNotes[i], deltaDur));
        }
    }

    /// <summary>
    /// 更新音高线擦除
    /// </summary>
    /// <param name="point"></param>
    private void UpdateErasingPitch(Point point)
    {
        if (EditingVoicePart == null)
        {
            return;
        }

        int tick = PointXToTick(point.X) - EditingVoicePart.position; // 转换为分片内相对 Tick
        DocManager.Inst.ExecuteCmd(new SetCurveCommand(
            DocManager.Inst.Project,
            EditingVoicePart,
            OpenUtau.Core.Format.Ustx.PITD,
            tick,
            0,
            _lastTick,
            0));
        _lastTick = tick;
    }
    /// <summary>
    /// 结束拖拽
    /// </summary>
    /// <param name="point"></param>
    /// <param name="timestamp"></param>
    public void OnGestureDragEnd(Point point, ulong timestamp)
    {
        switch (_inputState)
        {
            case PianoRollInputState.MovingNotes:
                DocManager.Inst.EndUndoGroup();
                break;
            case PianoRollInputState.ResizingNotes:
                DocManager.Inst.EndUndoGroup();
                break;
            case PianoRollInputState.Panning:
                _panMotion.EndDirectManipulation(timestamp);
                break;
            case PianoRollInputState.DrawingPitch:
                DocManager.Inst.EndUndoGroup();
                _lastPitch = null;
                _inputState = PianoRollInputState.Idle;
                // 关闭放大镜
                RequestMagnifierClose?.Invoke();
                break;
            case PianoRollInputState.MovingAnchors:
                DocManager.Inst.EndUndoGroup();
                break;
            case PianoRollInputState.EditingVibrato:
                DocManager.Inst.EndUndoGroup();
                break;
        }

        ResetPitchDrawPointerState();
        RequestInvalidateVisual?.Invoke();
        _inputState = PianoRollInputState.Idle;
        _movingNotesOrigins = [];
        _resizingNotesOriginDurations = [];
        _resizingReferenceNoteIndex = 0;
        _movingAnchorsOrigins = [];
        _movingAnchorsNotes = [];
        _movingAnchorsIndices = [];
        _editingVibratoNote = null;
        _editingVibratoHandle = null;
        _editingVibratoNoteWidth = 0;
        _editingVibratoWaveWidth = 0;
        _editingVibratoPhaseTrackWidth = 0;
        _editingVibratoDurationMs = 0;
        EditingTip = string.Empty;
    }

    private void ResetPitchDrawPointerState()
    {
        IsPitchDrawingActive = false;
        PitchDrawPointer = null;
    }

    public void OnGesturePinch(double scaleX, double scaleY, Point center, Vector panDelta)
    {
        InterruptPanMotionIfRunning();

        // X 轴：保持 center Tick 不变
        int centerTick = PointXToTick(center.X);
        double newTickWidth = Math.Clamp(TickWidth * scaleX,
            ViewConstants.PianoRollTickWidthMin, ViewConstants.PianoRollTickWidthMax);
        TickOffset = centerTick - center.X / newTickWidth;
        TickWidth = newTickWidth;

        // Y 轴：保持 center Tone 不变
        double centerTone = PointYToPitch(center.Y);
        double newKeyHeight = Math.Clamp(KeyHeight * scaleY,
            ViewConstants.NoteHeightMin, ViewConstants.NoteHeightMax);

        KeyOffset = Math.Max(0, ViewConstants.MaxTone - 0.5 - centerTone - center.Y / newKeyHeight);
        KeyHeight = newKeyHeight;

        // 双指平移
        TickOffset -= panDelta.X / newTickWidth;
        KeyOffset = Math.Max(0, KeyOffset - panDelta.Y / newKeyHeight);

        InvalidateMaxOffsets();
        ApplyViewportLimits();

        // Pinched implies Panning state
        _inputState = PianoRollInputState.Panning;
    }

    public void OnTwoFingerTap()
    {
        InterruptPanMotionIfRunning();
        DocManager.Inst.Undo();
        ToastService.Enqueue(L.S("PianoRoll.Undone"));
    }

    public void OnThreeFingerTap()
    {
        InterruptPanMotionIfRunning();
        DocManager.Inst.Redo();
        ToastService.Enqueue(L.S("PianoRoll.Redone"));
    }

    private void InterruptPanMotionIfRunning()
    {
        if (_panMotion.IsRunning)
        {
            _panMotion.Cancel();
        }
    }

    private void ApplyPanDeltaFromMotion(Vector delta)
    {
        double prevTickOffset = TickOffset;
        double prevKeyOffset = KeyOffset;

        TickOffset -= delta.X / TickWidth;
        KeyOffset -= delta.Y / KeyHeight;
        ApplyViewportLimits();
        RequestInvalidateVisual?.Invoke();

        if (_panMotion.IsInertiaRunning &&
            Math.Abs(TickOffset - prevTickOffset) < 1e-6 &&
            Math.Abs(KeyOffset - prevKeyOffset) < 1e-6)
        {
            _panMotion.Stop();
        }
    }

    private void SyncPlayPosFromViewportCenter()
    {
        if (TickWidth <= 0)
        {
            return;
        }

        int syncTick = (int)Math.Round(TickOffset + PlayMarkerScreenX / TickWidth);
        syncTick = Math.Max(0, syncTick);
        DocManager.Inst.ExecuteCmd(new SeekPlayPosTickNotification(syncTick));
    }

    #endregion

    #region 上下文操作面板

    /// <summary>
    /// 根据当前 EditMode 和选区状态重建 PianoRollContextActions。
    /// </summary>
    private void RebuildPianoRollContextActions()
    {
        // 性能优化：收起状态下不构建菜单
        if (!IsContextMenuExpanded)
        {
            return;
        }

        List<ContextActionItem> items = [];
        bool hasNoteSelection = SelectedNotes.Count > 0;
        bool hasSingleNote = SelectedNotes.Count == 1;
        bool hasAnchorSelection = SelectedAnchors.Count > 0;
        bool hasEnabledVibrato = SelectedNotes.Any(note => note.vibrato.length > 0f);
        bool allVibratoDisabled = hasNoteSelection && SelectedNotes.All(note => note.vibrato.length <= 0f);
        bool allVibratoEnabled = hasNoteSelection && SelectedNotes.All(note => note.vibrato.length > 0f);

        switch (EditMode)
        {
            case PianoRollEditMode.Hand:
                break;

            case PianoRollEditMode.Note:
                if (hasNoteSelection)
                {
                    items.Add(new ContextActionItem
                    {
                        Icon = PackIconPhosphorIconsKind.Trash,
                        Tip = L.S("Common.Delete"),
                        IsDanger = true,
                        Command = ReactiveCommand.Create(DeleteSelectedNotes)
                    });
                }

                if (hasSingleNote)
                {
                    items.Add(new ContextActionItem
                    {
                        Icon = PackIconPhosphorIconsKind.Wrench,
                        Tip = L.S("PianoRoll.Action.Properties"),
                        Command = ReactiveCommand.Create(EditNoteProperties)
                    });
                }

                break;

            case PianoRollEditMode.MultiSelect:
                if (IsSelecting)
                {
                    items.Add(new ContextActionItem
                    {
                        Icon = PackIconPhosphorIconsKind.FlagFill,
                        Tip = L.S("PianoRoll.Action.SelectionEnd"),
                        Command = ReactiveCommand.Create(EndSelection)
                    });
                }
                else
                {
                    items.Add(new ContextActionItem
                    {
                        Icon = PackIconPhosphorIconsKind.Flag,
                        Tip = L.S("PianoRoll.Action.SelectionStart"),
                        Command = ReactiveCommand.Create(BeginSelection)
                    });
                }

                items.Add(new ContextActionItem
                {
                    Icon = PackIconPhosphorIconsKind.SelectionAll,
                    Tip = L.S("Common.SelectAll"),
                    Command = ReactiveCommand.Create(SelectAllNotes)
                });
                if (hasNoteSelection)
                {
                    items.Add(new ContextActionItem
                    {
                        Icon = PackIconPhosphorIconsKind.Trash,
                        Tip = L.S("Common.Delete"),
                        IsDanger = true,
                        Command = ReactiveCommand.Create(DeleteSelectedNotes)
                    });
                    items.Add(new ContextActionItem
                    {
                        Icon = PackIconPhosphorIconsKind.Copy,
                        Tip = L.S("Common.Copy"),
                        Command = ReactiveCommand.Create(CopySelectedNotes)
                    });
                    items.Add(new ContextActionItem
                    {
                        Icon = PackIconPhosphorIconsKind.Scissors,
                        Tip = L.S("Common.Cut"),
                        Command = ReactiveCommand.Create(CutSelectedNotes)
                    });
                }

                items.Add(new ContextActionItem
                {
                    Icon = PackIconPhosphorIconsKind.Clipboard,
                    Tip = L.S("Common.Paste"),
                    Command = ReactiveCommand.Create(PasteNotes)
                });
                break;

            case PianoRollEditMode.PitchPen:
                items.Add(new ContextActionItem
                {
                    Icon = IsPitchEraserMode ? PackIconPhosphorIconsKind.Eraser : PackIconPhosphorIconsKind.Pencil,
                    Tip = IsPitchEraserMode
                        ? L.S("PianoRoll.Action.SwitchToPitchPen")
                        : L.S("PianoRoll.Action.SwitchToEraser"),
                    Command = ReactiveCommand.Create(TogglePitchEraser)
                });
                break;

            case PianoRollEditMode.Anchor:
                if (hasAnchorSelection)
                {
                    items.Add(new ContextActionItem
                    {
                        Icon = PackIconPhosphorIconsKind.Trash,
                        Tip = L.S("Common.Delete"),
                        IsDanger = true,
                        Command = ReactiveCommand.Create(DeleteSelectedAnchors)
                    });
                }

                break;

            case PianoRollEditMode.Vibrato:
                if (hasNoteSelection)
                {
                    if (allVibratoDisabled)
                    {
                        items.Add(new ContextActionItem
                        {
                            Icon = PackIconPhosphorIconsKind.Plus,
                            Tip = L.S("PianoRoll.Action.AddVibrato"),
                            Command = ReactiveCommand.Create(EnableVibrato)
                        });
                    }
                    else
                    {
                        if (!allVibratoEnabled)
                        {
                            // Mixed selection: also offer "Add" for the disabled notes
                            items.Add(new ContextActionItem
                            {
                                Icon = PackIconPhosphorIconsKind.Plus,
                                Tip = L.S("PianoRoll.Action.AddVibrato"),
                                Command = ReactiveCommand.Create(EnableVibratoForDisabled)
                            });
                        }

                        items.Add(new ContextActionItem
                        {
                            Icon = PackIconPhosphorIconsKind.Trash,
                            Tip = L.S("Common.Delete"),
                            IsDanger = true,
                            Command = ReactiveCommand.Create(DeleteVibrato)
                        });
                    }
                }

                break;
        }

        PianoRollContextActions = items;
    }

    // ── 操作方法存根 ──────────────────────────────────────────────────

    private void DeleteSelectedNotes()
    {
        if (EditingVoicePart == null || SelectedNotes.Count == 0)
        {
            return;
        }

        DocManager.Inst.StartUndoGroup(deferValidate: true);
        foreach (UNote note in SelectedNotes)
        {
            DocManager.Inst.ExecuteCmd(new RemoveNoteCommand(EditingVoicePart, note));
        }

        DocManager.Inst.EndUndoGroup();
        SelectedNotes.Clear();
    }

    private void EditNoteProperties()
    {
        // TODO: 实现音符属性编辑弹窗
        ToastService.Enqueue(L.S("PianoRoll.Toast.NotePropertiesNotImpl"));
    }

    private void BeginSelection()
    {
        if (EditingVoicePart == null)
        {
            return;
        }

        // 保存开始选区tick
        int tick = PlayPosTick - EditingVoicePart.position;
        if (tick < 0)
        {
            tick = 0;
        }
        BeginSelectionTick = tick;
        IsSelecting = true;
        RebuildPianoRollContextActions();
        RequestInvalidateVisual?.Invoke(); // 触发画布重绘以显示选区可视效果
        EditingTip = L.S("PianoRoll.Selection.EndTip");
    }

    private void EndSelection()
    {
        if (EditingVoicePart == null)
        {
            EditingTip = string.Empty;
            IsSelecting = false;
            RequestInvalidateVisual?.Invoke(); // 触发画布重绘以清除选区可视效果
            return;
        }

        int endSelectionTick = PlayPosTick - EditingVoicePart.position;
        if (endSelectionTick < 0)
        {
            endSelectionTick = 0;
        }

        if (BeginSelectionTick > endSelectionTick)
        {
            // 交换
            int temp = BeginSelectionTick;
            BeginSelectionTick = endSelectionTick;
            endSelectionTick = temp;
        }

        SelectedNotes.Clear();
        foreach (UNote note in EditingVoicePart.notes)
        {
            if (note.End < BeginSelectionTick)
            {
                continue;
            }

            if (note.position > endSelectionTick)
            {
                break;
            }

            SelectedNotes.Add(note);
        }

        EditingTip = string.Empty;
        IsSelecting = false;
        RequestInvalidateVisual?.Invoke(); // 触发画布重绘以清除选区可视效果
        RebuildPianoRollContextActions();
        ToastService.Enqueue(string.Format(L.S("PianoRoll.Selection.Selected"), SelectedNotes.Count));
    }

    private void SelectAllNotes()
    {
        SelectedNotes.Clear();
        if (EditingVoicePart == null)
        {
            return;
        }

        SelectedNotes.AddRange(EditingVoicePart.notes);
        ToastService.Enqueue(string.Format(L.S("PianoRoll.Selection.AllSelected"), SelectedNotes.Count));
    }

    private void CopySelectedNotes()
    {
        if (EditingVoicePart == null || SelectedNotes.Count == 0)
        {
            return;
        }

        DocManager.Inst.NotesClipboard = SelectedNotes
            .OrderBy(n => n.position)
            .Select(n => n.Clone())
            .ToList();
        ToastService.Enqueue(string.Format(L.S("PianoRoll.Selection.Copied"), SelectedNotes.Count));
    }

    private void CutSelectedNotes()
    {
        if (EditingVoicePart == null || SelectedNotes.Count == 0)
        {
            return;
        }

        DocManager.Inst.NotesClipboard = SelectedNotes
            .OrderBy(n => n.position)
            .Select(n => n.Clone())
            .ToList();

        DocManager.Inst.StartUndoGroup(deferValidate: true);
        foreach (UNote note in SelectedNotes)
        {
            DocManager.Inst.ExecuteCmd(new RemoveNoteCommand(EditingVoicePart, note));
        }

        DocManager.Inst.EndUndoGroup();

        int count = SelectedNotes.Count;
        SelectedNotes.Clear();
        ToastService.Enqueue(string.Format(L.S("PianoRoll.Selection.Cut"), count));
    }

    private void PasteNotes()
    {
        if (EditingVoicePart == null || DocManager.Inst.NotesClipboard == null || DocManager.Inst.NotesClipboard.Count == 0)
        {
            return;
        }

        var project = DocManager.Inst.Project;
        var notes = DocManager.Inst.NotesClipboard.Select(n => n.Clone()).ToList();

        int left = PlayPosTick;
        int minPosition = notes.Min(n => n.position);
        if (left < EditingVoicePart.position)
        {
            return;
        }

        int offset = left - minPosition - EditingVoicePart.position;
        notes.ForEach(note => note.position += offset);

        DocManager.Inst.StartUndoGroup(deferValidate: true);
        DocManager.Inst.ExecuteCmd(new AddNoteCommand(EditingVoicePart, notes));
        int minDurTick = EditingVoicePart.GetMinDurTick(project);
        if (EditingVoicePart.Duration < minDurTick)
        {
            DocManager.Inst.ExecuteCmd(new ResizeVoicePartCommand(project, EditingVoicePart, minDurTick - EditingVoicePart.Duration, false));
        }

        DocManager.Inst.EndUndoGroup();

        SelectedNotes.Clear();
        SelectedNotes.AddRange(notes);
        ToastService.Enqueue(string.Format(L.S("PianoRoll.Selection.Pasted"), notes.Count));
    }

    private void TogglePitchEraser()
    {
        IsPitchEraserMode = !IsPitchEraserMode;
    }

    private void DeleteSelectedAnchors()
    {
        if (EditingVoicePart == null || SelectedAnchors.Count == 0) return;

        // 按音符分组，收集可删除的索引（非首末点）
        Dictionary<UNote, List<int>> toDelete = new();
        foreach (PitchPoint pp in SelectedAnchors)
        {
            UNote? note = FindNoteForPitchPoint(pp);
            if (note == null) continue;
            int idx = note.pitch.data.IndexOf(pp);
            if (idx <= 0 || idx >= note.pitch.data.Count - 1) continue; // 保护首末点
            if (!toDelete.TryGetValue(note, out List<int>? list))
                toDelete[note] = list = new List<int>();
            list.Add(idx);
        }

        if (toDelete.Count == 0) return;

        DocManager.Inst.StartUndoGroup();
        foreach ((UNote note, List<int> indices) in toDelete)
        {
            // 从大到小删，避免索引漂移
            foreach (int idx in indices.OrderByDescending(x => x))
                DocManager.Inst.ExecuteCmd(new DeletePitchPointCommand(EditingVoicePart, note, idx));
        }

        DocManager.Inst.EndUndoGroup();

        SelectedAnchors.Clear();
    }

    private void SelectPrevAnchor()
    {
        if (SelectedAnchors.Count == 0)
        {
            SelectNearestAnchor();
            if (SelectedAnchors.Count == 0) return;
        }

        PitchPoint current = SelectedAnchors[0];
        UNote? note = FindNoteForPitchPoint(current);
        if (note == null) return;
        int idx = note.pitch.data.IndexOf(current);
        if (idx > 0)
        {
            SelectSingleAnchor(note, note.pitch.data[idx - 1]);
        }
        else
        {
            // 已在当前音符首点，跳到前一音符的末点
            if (note.Prev != null && note.Prev.pitch.data.Count > 0) // Warning: Core 项目又在乱写 null，加个保护
            {
                SelectSingleAnchor(note.Prev, note.Prev.pitch.data[^1]);
            }
            // 否则静默不操作（已到全局首点）
        }
    }

    private void SelectNextAnchor()
    {
        if (SelectedAnchors.Count == 0)
        {
            SelectNearestAnchor();
            if (SelectedAnchors.Count == 0) return;
        }

        PitchPoint current = SelectedAnchors[0];
        UNote? note = FindNoteForPitchPoint(current);
        if (note == null) return;
        int idx = note.pitch.data.IndexOf(current);
        if (idx < note.pitch.data.Count - 1)
        {
            SelectSingleAnchor(note, note.pitch.data[idx + 1]);
        }
        else
        {
            // 已在当前音符末点，跳到后一音符的首点
            if (note.Next != null && note.Next.pitch.data.Count > 0) // Warning: Core 项目又在乱写 null，加个保护
            {
                SelectSingleAnchor(note.Next, note.Next.pitch.data[0]);
            }
            // 否则静默不操作（已到全局末点）
        }
    }

    private void SelectNearestAnchor()
    {
        if (EditingVoicePart == null) return;
        UProject project = DocManager.Inst.Project;
        int playTick = PlayPosTick;

        PitchPoint? nearest = null;
        UNote? nearestNote = null;
        long minDist = long.MaxValue;

        foreach (UNote note in EditingVoicePart.notes)
        {
            foreach (PitchPoint pp in note.pitch.data)
            {
                int ppAbsTick = project.timeAxis.MsPosToTickPos(note.PositionMs + pp.X);
                long dist = Math.Abs((long)ppAbsTick - playTick);
                if (dist >= minDist) continue;
                minDist = dist;
                nearest = pp;
                nearestNote = note;
            }
        }

        if (nearest == null || nearestNote == null) return;
        SelectSingleAnchor(nearestNote, nearest);
    }

    private void AddPitchPointAtPlayPos()
    {
        if (EditingVoicePart == null) return;
        UProject project = DocManager.Inst.Project;

        // 计算播放标记的绝对毫秒位置
        double playPosMs = project.timeAxis.TickPosToMsPos(PlayPosTick);

        // 遍历所有音符，找到控制点列表 X 范围包含播放标记的音符
        UNote? note = null;
        foreach (UNote n in EditingVoicePart.notes)
        {
            if (n.pitch.data.Count < 2) continue; // 理论上控制点总数至少为2（首末点）

            // 控制点 X 范围：[data[0].X, data[-1].X]，相对于音符起始 ms
            float minXMs = n.pitch.data[0].X;
            float maxXMs = n.pitch.data[^1].X;
            float playPosRelXMs = (float)(playPosMs - n.PositionMs);

            if (playPosRelXMs >= minXMs && playPosRelXMs <= maxXMs)
            {
                note = n;
                break;
            }
        }

        if (note == null)
        {
            ToastService.Enqueue(L.S("PianoRoll.Toast.ControlPointNotFound"));
            return;
        }

        // 控制点 X = 播放标记相对于音符起始的毫秒偏移
        float relXMs = (float)(playPosMs - note.PositionMs);

        // 找正确插入索引（保持 pitch.data 按 X 升序）
        List<PitchPoint> data = note.pitch.data;
        int insertIdx = data.Count;
        for (int i = 0; i < data.Count; i++)
        {
            if (!(data[i].X >= relXMs)) continue;
            insertIdx = i;
            break;
        }

        // 不允许覆盖/替换首末点（只能插入索引 1..data.Count-1 之间）
        insertIdx = Math.Clamp(insertIdx, 1, data.Count - 1);

        PitchPoint prevPoint = data[insertIdx - 1];
        PitchPoint nextPoint = data[insertIdx];
        float relY = (float)MusicMath.InterpolateShape(
            prevPoint.X,
            nextPoint.X,
            prevPoint.Y,
            nextPoint.Y,
            relXMs,
            prevPoint.shape);
        PitchPoint newPoint = new PitchPoint(relXMs, relY, prevPoint.shape);
        DocManager.Inst.StartUndoGroup();
        DocManager.Inst.ExecuteCmd(new AddPitchPointCommand(EditingVoicePart, note, newPoint, insertIdx));
        DocManager.Inst.EndUndoGroup();

        // 选中新插入的控制点
        SelectSingleAnchor(note, newPoint);
    }

    /// <summary>
    /// 在 Part.notes 中找到包含指定 PitchPoint 的音符。
    /// 若找不到返回 null（理论上不应发生）。
    /// </summary>
    private UNote? FindNoteForPitchPoint(PitchPoint pp)
    {
        if (EditingVoicePart == null) return null;
        foreach (UNote note in EditingVoicePart.notes)
        {
            if (note.pitch.data.Contains(pp))
                return note;
        }

        return null;
    }

    private void SelectSingleAnchor(UNote note, PitchPoint pitchPoint)
    {
        SelectedNotes.Clear();
        SelectedNotes.Add(note);
        SelectedAnchors.Clear();
        SelectedAnchors.Add(pitchPoint);
    }

    private PitchPoint AddPitchPointFromCurveHit(PitchCurveHit hit)
    {
        PitchPoint newPoint = new PitchPoint(hit.XMs, hit.Y, hit.Shape);
        DocManager.Inst.ExecuteCmd(new AddPitchPointCommand(EditingVoicePart!, hit.Note, newPoint, hit.InsertIndex));
        return newPoint;
    }

    private void InsertAnchorAtCurveHit(PitchCurveHit hit)
    {
        if (EditingVoicePart == null)
        {
            return;
        }

        DocManager.Inst.StartUndoGroup(deferValidate: true);
        PitchPoint newPoint = AddPitchPointFromCurveHit(hit);
        DocManager.Inst.EndUndoGroup();
        SelectSingleAnchor(hit.Note, newPoint);
    }

    private void StartToInsertAndMoveAnchor(PitchCurveHit hit)
    {
        if (EditingVoicePart == null)
        {
            return;
        }

        DocManager.Inst.StartUndoGroup(deferValidate: true);
        PitchPoint newPoint = AddPitchPointFromCurveHit(hit);
        SelectSingleAnchor(hit.Note, newPoint);
        StartToMoveAnchors(beginUndoGroup: false);
    }

    /// <summary>
    /// 准备开始拖拽锚点：记录各选中锚点的初始坐标快照，进入 MovingAnchors 状态。
    /// </summary>
    private void StartToMoveAnchors(bool beginUndoGroup = true)
    {
        if (EditingVoicePart == null) return;
        int count = SelectedAnchors.Count;

        (float X, float Y)[] origins = new (float X, float Y)[count];
        UNote[] notes = new UNote[count];
        int[] indices = new int[count];

        for (int i = 0; i < count; i++)
        {
            PitchPoint pp = SelectedAnchors[i];
            UNote? note = FindNoteForPitchPoint(pp);
            if (note == null)
            {
                // 数据不一致，放弃进入移动状态
                return;
            }

            origins[i] = (pp.X, pp.Y);
            notes[i] = note;
            indices[i] = note.pitch.data.IndexOf(pp);
        }

        _movingAnchorsOrigins = origins;
        _movingAnchorsNotes = notes;
        _movingAnchorsIndices = indices;

        _inputState = PianoRollInputState.MovingAnchors;
        if (beginUndoGroup)
        {
            DocManager.Inst.StartUndoGroup(deferValidate: true);
        }
    }

    private const double AnchorDragSensitivity = 0.5; // 触控灵敏度系数：移动端手指移动 2px 等效 1px
    private const double _pitchPointHitMargin = 5000.0;

    private static Vector ApplyVibratoCurve(Vector totalOffset)
    {
        const double knee = 30.0;
        const double baseScale = 0.3;

        double px = Math.Abs(totalOffset.X);
        double py = Math.Abs(totalOffset.Y);

        double effectiveX = px < knee
            ? px * px / knee
            : knee + (px - knee);
        double effectiveY = py < knee
            ? py * py / knee
            : knee + (py - knee);

        return new Vector(
            Math.Sign(totalOffset.X) * effectiveX * baseScale,
            Math.Sign(totalOffset.Y) * effectiveY * baseScale);
    }

    /// <summary>
    /// 拖拽中更新锚点位置。totalOffset 为相对 DragBegin 起点的累积屏幕偏移。
    /// </summary>
    private void UpdateMovingAnchors(Vector totalOffset)
    {
        if (EditingVoicePart == null) return;
        UProject project = DocManager.Inst.Project;

        // 降低灵敏度：用户移动 2px → 锚点等效移动 1px
        Vector scaledTotal = totalOffset * AnchorDragSensitivity;

        for (int i = 0; i < SelectedAnchors.Count; i++)
        {
            PitchPoint pp = SelectedAnchors[i];
            UNote note = _movingAnchorsNotes[i];
            int index = _movingAnchorsIndices[i];
            (float origX, float origY) = _movingAnchorsOrigins[i];

            bool isFirst = (index == 0);
            bool isLast = (index == note.pitch.data.Count - 1);

            // ── X 轴（屏幕像素 → Tick → ms → PitchPoint.X） ──────────────────
            double tickDelta = scaledTotal.X / TickWidth;
            double origAbsMs = note.PositionMs + origX;
            int origAbsTick = project.timeAxis.MsPosToTickPos(origAbsMs);
            double targetAbsMs = project.timeAxis.TickPosToMsPos(
                (int)Math.Round(origAbsTick + tickDelta));
            double targetRelX = targetAbsMs - note.PositionMs;

            // X 约束：不可超过相邻控制点
            if (!isFirst)
                targetRelX = Math.Max(targetRelX, note.pitch.data[index - 1].X);
            if (!isLast)
                targetRelX = Math.Min(targetRelX, note.pitch.data[index + 1].X);

            float deltaX = (float)(targetRelX - pp.X);

            // ── Y 轴（屏幕像素 → semitone → centitone → PitchPoint.Y） ────────
            float deltaY;
            if (isLast)
            {
                // 末尾点：Y 恒为 0（与 PC 端 PitchPointEditState 一致）
                deltaY = -pp.Y;
            }
            else if (isFirst && note.pitch.snapFirst)
            {
                // 首点 snapFirst=true：Y 由前一音符音高差固定
                UNote snapTo = (note.Prev != null && note.Prev.End == note.position) // Warning: Core 项目又在乱写 null，加个保护
                    ? note.Prev
                    : note;
                float targetY = (snapTo.AdjustedTone - note.AdjustedTone) * 10;
                deltaY = targetY - pp.Y;
            }
            else
            {
                // 普通点：origin 音高 + 屏幕偏移换算的音高变化量
                double origPitchSemitone = note.AdjustedTone + origY / 10.0;
                double targetPitchSemitone = origPitchSemitone - scaledTotal.Y / KeyHeight;
                float targetY = (float)((targetPitchSemitone - note.AdjustedTone) * 10);
                deltaY = targetY - pp.Y;
            }

            if (deltaX == 0f && deltaY == 0f) continue;
            DocManager.Inst.ExecuteCmd(new MovePitchPointCommand(EditingVoicePart, pp, deltaX, deltaY));
        }

        if (SelectedAnchors.Count == 1)
        {
            PitchPoint pp = SelectedAnchors[0];
            EditingTip = $"X: {pp.X:F1} ms  Pitch: {pp.Y * 10:F0} cent";
        }
    }

    /// <summary>
    /// 循环切换控制点形状：io → l → i → o → io
    /// </summary>
    private void CyclePitchPointShape()
    {
        if (EditingVoicePart == null || SelectedAnchors.Count == 0) return;

        DocManager.Inst.StartUndoGroup();
        foreach (PitchPoint pp in SelectedAnchors)
        {
            // io(0) → l(1) → i(2) → o(3) → io(0) ...
            PitchPointShape nextShape = (PitchPointShape)(((int)pp.shape + 1) % 4);
            DocManager.Inst.ExecuteCmd(new ChangePitchPointShapeCommand(EditingVoicePart, pp, nextShape));
        }

        DocManager.Inst.EndUndoGroup();
    }

    private void StartToEditVibrato(VibratoOverlayHit hit)
    {
        if (EditingVoicePart == null)
        {
            return;
        }

        DocManager.Inst.StartUndoGroup(deferValidate: true);
        _editingVibratoNote = hit.Note;
        _editingVibratoHandle = hit.HandleKind;

        if (_editingVibratoNote.vibrato.length <= 0f)
        {
            DocManager.Inst.ExecuteCmd(new VibratoLengthCommand(
                EditingVoicePart,
                _editingVibratoNote,
                NotePresets.Default.DefaultVibrato.VibratoLength));
        }

        UVibrato vibrato = _editingVibratoNote.vibrato;
        _editingVibratoOriginDepth = vibrato.depth;
        _editingVibratoOriginPeriod = vibrato.period;
        _editingVibratoOriginLength = vibrato.length;
        _editingVibratoOriginFadeIn = vibrato.@in;
        _editingVibratoOriginFadeOut = vibrato.@out;
        _editingVibratoOriginShift = vibrato.shift;
        _editingVibratoOriginDrift = vibrato.drift;
        _editingVibratoNoteWidth = Math.Max(hit.Layout.NoteRect.Width, 1);
        _editingVibratoWaveWidth = Math.Max(hit.Layout.WaveEndX - hit.Layout.WaveStartX, 1);
        _editingVibratoPhaseTrackWidth = Math.Max(hit.Layout.PhaseTrackRect.Width, 1);
        _editingVibratoDurationMs = Math.Max(_editingVibratoNote.DurationMs, 1.0);
        _inputState = PianoRollInputState.EditingVibrato;
    }

    private void UpdateEditingVibrato(Vector totalOffset)
    {
        if (EditingVoicePart == null || _editingVibratoNote == null || _editingVibratoHandle == null)
        {
            return;
        }

        UNote note = _editingVibratoNote;
        Vector scaledTotal = totalOffset;

        switch (_editingVibratoHandle.Value)
        {
            case VibratoHandleKind.Body:
                {
                    Vector bodyOffset = ApplyVibratoCurve(totalOffset); // 深度 & 频率使用非线性曲线实现精调
                    float depth = _editingVibratoOriginDepth - (float)(bodyOffset.Y / Math.Max(KeyHeight, 1) * 100.0);
                    float period = _editingVibratoOriginPeriod +
                        (float)(bodyOffset.X * (_editingVibratoDurationMs / Math.Max(_editingVibratoNoteWidth, 1.0)));
                    if (!NearlyEqual(note.vibrato.depth, depth))
                    {
                        DocManager.Inst.ExecuteCmd(new VibratoDepthCommand(EditingVoicePart, note, depth));
                    }

                    if (!NearlyEqual(note.vibrato.period, period))
                    {
                        DocManager.Inst.ExecuteCmd(new VibratoPeriodCommand(EditingVoicePart, note, period));
                    }

                    EditingTip = $"Depth: {note.vibrato.depth:F0} cent  Period: {note.vibrato.period:F0} ms";
                    break;
                }
            case VibratoHandleKind.Start:
                {
                    float length = _editingVibratoOriginLength -
                        (float)(scaledTotal.X / Math.Max(_editingVibratoNoteWidth, 1.0) * 100.0);
                    if (!NearlyEqual(note.vibrato.length, length))
                    {
                        DocManager.Inst.ExecuteCmd(new VibratoLengthCommand(EditingVoicePart, note, length));
                    }

                    EditingTip = $"Length: {note.vibrato.length:F0}%";
                    break;
                }
            case VibratoHandleKind.FadeIn:
                {
                    float fadeIn = _editingVibratoOriginFadeIn +
                        (float)(scaledTotal.X / Math.Max(_editingVibratoWaveWidth, 1.0) * 100.0);
                    if (!NearlyEqual(note.vibrato.@in, fadeIn))
                    {
                        DocManager.Inst.ExecuteCmd(new VibratoFadeInCommand(EditingVoicePart, note, fadeIn));
                    }

                    EditingTip = $"FadeIn: {note.vibrato.@in:F0}%";
                    break;
                }
            case VibratoHandleKind.FadeOut:
                {
                    float fadeOut = _editingVibratoOriginFadeOut -
                        (float)(scaledTotal.X / Math.Max(_editingVibratoWaveWidth, 1.0) * 100.0);
                    if (!NearlyEqual(note.vibrato.@out, fadeOut))
                    {
                        DocManager.Inst.ExecuteCmd(new VibratoFadeOutCommand(EditingVoicePart, note, fadeOut));
                    }

                    EditingTip = $"FadeOut: {note.vibrato.@out:F0}%";
                    break;
                }
            case VibratoHandleKind.Phase:
                {
                    float shift = _editingVibratoOriginShift +
                        (float)(scaledTotal.X / Math.Max(_editingVibratoPhaseTrackWidth, 1.0) * 100.0);
                    if (!NearlyEqual(note.vibrato.shift, shift))
                    {
                        DocManager.Inst.ExecuteCmd(new VibratoShiftCommand(EditingVoicePart, note, shift));
                    }

                    EditingTip = $"Phase: {note.vibrato.shift:F0}%";
                    break;
                }
            case VibratoHandleKind.Drift:
                {
                    float drift = _editingVibratoOriginDrift - (float)(scaledTotal.Y / Math.Max(KeyHeight, 1) * 100.0);
                    if (!NearlyEqual(note.vibrato.drift, drift))
                    {
                        DocManager.Inst.ExecuteCmd(new VibratoDriftCommand(EditingVoicePart, note, drift));
                    }

                    EditingTip = $"Drift: {note.vibrato.drift:F0}%";
                    break;
                }
        }
    }

    private void ToggleVibrato(UNote note)
    {
        if (EditingVoicePart == null)
        {
            return;
        }

        DocManager.Inst.StartUndoGroup(deferValidate: true);
        float targetLength = note.vibrato.length > 0f
            ? 0f
            : NotePresets.Default.DefaultVibrato.VibratoLength;
        DocManager.Inst.ExecuteCmd(new VibratoLengthCommand(EditingVoicePart, note, targetLength));
        DocManager.Inst.EndUndoGroup();
    }

    private static UVibrato CreateDefaultVibrato(float length)
    {
        var preset = NotePresets.Default.DefaultVibrato;
        return new UVibrato
        {
            length = length,
            period = preset.VibratoPeriod,
            depth = preset.VibratoDepth,
            @in = preset.VibratoIn,
            @out = preset.VibratoOut,
            shift = preset.VibratoShift,
            drift = preset.VibratoDrift,
            volLink = preset.VibratoVolLink,
        };
    }

    private static bool NearlyEqual(float left, float right)
    {
        return Math.Abs(left - right) < 0.01f;
    }

    private void EnableVibrato()
    {
        if (EditingVoicePart == null || SelectedNotes.Count == 0)
        {
            return;
        }

        DocManager.Inst.StartUndoGroup(deferValidate: true);
        foreach (UNote note in SelectedNotes)
        {
            DocManager.Inst.ExecuteCmd(new SetVibratoCommand(
                EditingVoicePart,
                note,
                CreateDefaultVibrato(NotePresets.Default.DefaultVibrato.VibratoLength)));
        }

        DocManager.Inst.EndUndoGroup();
    }

    private void EnableVibratoForDisabled()
    {
        if (EditingVoicePart == null || SelectedNotes.Count == 0)
        {
            return;
        }

        DocManager.Inst.StartUndoGroup(deferValidate: true);
        foreach (UNote note in SelectedNotes)
        {
            if (note.vibrato.length > 0f)
            {
                continue;
            }

            DocManager.Inst.ExecuteCmd(new SetVibratoCommand(
                EditingVoicePart,
                note,
                CreateDefaultVibrato(NotePresets.Default.DefaultVibrato.VibratoLength)));
        }

        DocManager.Inst.EndUndoGroup();
    }

    private void DeleteVibrato()
    {
        if (EditingVoicePart == null || SelectedNotes.Count == 0 || SelectedNotes.All(note => note.vibrato.length <= 0f))
        {
            return;
        }

        DocManager.Inst.StartUndoGroup(deferValidate: true);
        foreach (UNote note in SelectedNotes)
        {
            if (note.vibrato.length <= 0f)
            {
                continue;
            }

            DocManager.Inst.ExecuteCmd(new VibratoLengthCommand(EditingVoicePart, note, 0f));
        }

        DocManager.Inst.EndUndoGroup();
    }

    #endregion

    public void Dispose()
    {
        StopPreviewTone();
        _panMotion.Dispose();
        DocManager.Inst.RemoveSubscriber(this);
        _disposables.Dispose();
        GC.SuppressFinalize(this);
    }

    public void OnNext(UCommand cmd, bool isUndo)
    {
        switch (cmd)
        {
            case DeletePitchPointCommand deletePitchPointCommand
                : // TODO: 这里由于core项目设计缺陷，撤回时实际执行的是 AddPitchPointCommand，无法简单地判断删除了哪个点，会导致使用出现bug
                SelectedAnchors.Remove(deletePitchPointCommand.Point);
                break;
            case VibratoCommand:
                RebuildPianoRollContextActions();
                RequestInvalidateVisual?.Invoke();
                break;
            case ProjectCommand:
                RequestInvalidateVisual?.Invoke();
                break;
            case TrackChangeSingerCommand: // 切换歌手
                _ = UpdatePortraitAsync(); // 更新立绘
                break;
        }
    }
}