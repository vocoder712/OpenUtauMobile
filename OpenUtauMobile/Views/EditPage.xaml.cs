using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Views;
using OpenUtau.Api;
using OpenUtau.Core;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtau.Utils.Messages;
using OpenUtauMobile.Utils;
using OpenUtauMobile.ViewModels;
using OpenUtauMobile.ViewModels.Converters;
using OpenUtauMobile.Views.Controls;
using OpenUtauMobile.Views.DrawableObjects;
using OpenUtauMobile.Views.Utils;
using OpenUtauMobile.Resources.Strings;
using ReactiveUI;
using Serilog;
using SkiaSharp;
using System.Diagnostics;
using System.Reactive.Disposables;
using Preferences = OpenUtau.Core.Util.Preferences;
using System.Reactive.Linq;

namespace OpenUtauMobile.Views;

public partial class EditPage : ContentPage, ICmdSubscriber, IDisposable
{
    // 管理资源释放
    private readonly CompositeDisposable _disposables = [];
    private readonly EditViewModel _viewModel;
    // 定时器
    private IDispatcherTimer PlaybackTimer { get; }
    private IDispatcherTimer AutoSaveTimer { get; }
    //  走带画布手势处理器
    private readonly GestureProcessor _trackGestureProcessor;
    // 时间轴画布手势处理器
    private readonly GestureProcessor _timeLineGestureProcessor;
    // 钢琴卷帘画布手势处理器
    private readonly GestureProcessor _pianoRollGestureProcessor;
    // 音素画布手势处理器
    private readonly GestureProcessor _phonemeGestureProcessor;
    // 表情画布手势处理器
    private readonly GestureProcessor _expressionGestureProcessor;
    // 记录走带-主编辑区分隔初始位置
    private double OriginTrackMainEditDivPosY { get; set; }
    // 记录钢琴卷帘-扩展区分隔初始高度
    private double OriginExpHeight { get; set; }
    // 添加变量跟踪是否正在处理滚动同步，避免无限循环
    private bool isScrollingSyncInProgress = false;
    // 钢琴卷帘量化按钮长按标志
    private bool isPianoRollSnapDivButtonLongPressed = false;
    // 走带量化按钮长按标志
    private bool isTrackSnapDivButtonLongPressed = false;
#if ANDROID29_0_OR_GREATER
    // 放大镜
    private Android.Widget.Magnifier? magnifier = null;
#endif
    /// <summary>
    /// 标记用户是否正在绘制音高曲线、表情曲线等
    /// </summary>
    private bool IsUserDrawingCurve { get; set; } = false;
    /// <summary>
    /// 触摸点位置（实际坐标）
    /// </summary>
    private SKPoint TouchingPoint { get; set; } = new();
    #region 画笔
    // 音高线画笔
    private readonly SKPaint _pitchLinePaint = new()
    {
        Color = ThemeColorsManager.Current.PitchLine,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 4,
        IsAntialias = false,
    };
    private readonly SKPaint _pianoKeysPaint = new()
    {
        Style = SKPaintStyle.Fill
    };
    #endregion

    public EditPage(string path)
    {
        InitializeComponent();
        _viewModel = (EditViewModel)BindingContext;
        _viewModel.Path = path;
        this.Loaded += (s, e) =>
        {
            // 确保在UI绘制完成后的下一个UI线程周期执行
            Dispatcher.Dispatch(async () =>
            {
                // 添加加载项目work
                _viewModel.SetWork(WorkType.LoadingProject, path, detail:path);
                await _viewModel.Init();
                // 移除加载项目work
                _viewModel.RemoveWork(path);
                InitMagnifier();
                //EnableAndroidBlur();
            });
        };
        // 设置屏幕常亮
        if (Preferences.Default.KeepScreenOn)
        {
            DeviceDisplay.Current.KeepScreenOn = true;
        }
        // 在主布局改变后设置正确高度
        MainLayout.SizeChanged += (s, e) =>
        {
            _viewModel.MainLayoutHeight = MainLayout.Height;
            _viewModel.SetBoundDivControl();
            _viewModel.UpdateTrackMainEditBoundaries();
        };
        // 在主编辑区改变后设置正确高度
        MainEdit.SizeChanged += (s, e) =>
        {
            _viewModel.MainEditHeight = MainEdit.Height;
            _viewModel.DivExpPosY = _viewModel.MainEditHeight - _viewModel.ExpHeight;
            _viewModel.SetBoundExpDivControl();
            _viewModel.UpdatePianoRollExpBoundaries();
        };
        // 走带画布手势处理器初始化
        _trackGestureProcessor = new GestureProcessor(_viewModel.TrackTransformer);
        // 时间轴画布手势处理器初始化
        _timeLineGestureProcessor = new GestureProcessor(_viewModel.TrackTransformer);
        // 钢琴卷帘画布手势处理器初始化
        _pianoRollGestureProcessor = new GestureProcessor(_viewModel.PianoRollTransformer);
        // 音素画布手势处理器处理器初始化
        _phonemeGestureProcessor = new GestureProcessor(_viewModel.PianoRollTransformer);
        // 表情画布手势处理器初始化
        _expressionGestureProcessor = new GestureProcessor(_viewModel.PianoRollTransformer);
        SetupGestureEvents();
        // 设置ScrollView滚动事件
        ScrollTrckHeaders.Scrolled += ScrollTrckHeaders_Scrolled;
        // 初始化放大镜微件
        //InitMagnifier();
        // 订阅播放位置更新事件，重绘指针画布
        this.WhenAnyValue(x => x._viewModel.PlayPosTick)
            .Subscribe(_ =>
            {
                PlaybackPosCanvas.InvalidateSurface();
            })
            .DisposeWith(_disposables);
        // 订阅播放状态更新事件
        this.WhenAnyValue(x => x._viewModel.Playing)
            .Subscribe(Playing =>
            {
                if (Application.Current?.Resources != null)
                {
                    ButtonPlayOrPause.ImageSource = Playing
                        ? (ImageSource)Application.Current.Resources["pause"]
                        : (ImageSource)Application.Current.Resources["play"];
                }
            })
            .DisposeWith(_disposables);
        // 订阅走带Transformer更新事件
        _viewModel.WhenAnyValue(x => x.TrackTransformer.PanX,
            x => x.TrackTransformer.PanY,
            x => x.TrackTransformer.ZoomX,
            x => x.TrackTransformer.ZoomY)
            .Subscribe(_ =>
            {
                TrackCanvas.InvalidateSurface();
                PlaybackTickBackgroundCanvas.InvalidateSurface();
                PlaybackPosCanvas.InvalidateSurface();
            })
            .DisposeWith(_disposables);
        // 订阅钢琴卷帘TransformerX方向更新事件
        _viewModel.WhenAnyValue(x => x.PianoRollTransformer.PanX,
            x => x.PianoRollTransformer.ZoomX)
            .Throttle(TimeSpan.FromMilliseconds(16.6)) // 限制更新频率为60FPS
            .Subscribe(_ =>
            {
                PianoRollCanvas.InvalidateSurface();
                PianoRollTickBackgroundCanvas.InvalidateSurface();
                PianoRollPitchCanvas.InvalidateSurface();
                PhonemeCanvas.InvalidateSurface();
                ExpressionCanvas.InvalidateSurface();
            })
            .DisposeWith(_disposables);
        // 订阅钢琴卷帘TransformerY方向更新事件
        _viewModel.WhenAnyValue(x => x.PianoRollTransformer.PanY,
            x => x.PianoRollTransformer.ZoomY)
            .Subscribe(_ =>
            {
                //pianoKeysTransformer.SetPanY(_.Item1); // 同步钢琴键画布的Y平移
                //pianoKeysTransformer.SetZoomY(_.Item2); // 同步钢琴键画布的Y缩放
                //Debug.WriteLine($"钢琴Transformer更新: PanY={pianoKeysTransformer.PanY}, ZoomY={pianoKeysTransformer.ZoomY}");
                PianoRollCanvas.InvalidateSurface();
                PianoKeysCanvas.InvalidateSurface();
                PianoRollPitchCanvas.InvalidateSurface();
                PianoRollKeysBackgroundCanvas.InvalidateSurface();
            })
            .DisposeWith(_disposables);
        // 接收画布更新消息
        MessageBus.Current.Listen<RefreshCanvasMessage>()
            .Subscribe(_ =>
            {
                TrackCanvas.InvalidateSurface();
                PlaybackTickBackgroundCanvas.InvalidateSurface();
                PlaybackPosCanvas.InvalidateSurface();
            })
            .DisposeWith(_disposables);
        // 订阅音符编辑模式变化
        _viewModel.WhenAnyValue(x => x.CurrentNoteEditMode)
            .Subscribe(mode =>
            {
                SKColorMauiColorConverter converter = new();
                Color? activeColor = converter.Convert(ThemeColorsManager.Current.ActiveNoteEditModeButton, typeof(Color), null, null!) as Color;
                ButtonSwitchEditNoteMode.BackgroundColor = mode == EditViewModel.NoteEditMode.EditNote ? activeColor : Colors.Transparent;
                ButtonSwitchEditPitchCurveMode.BackgroundColor = mode == EditViewModel.NoteEditMode.EditPitchCurve ? activeColor : Colors.Transparent;
                ButtonSwitchEditPitchAnchorMode.BackgroundColor = mode == EditViewModel.NoteEditMode.EditPitchAnchor ? activeColor : Colors.Transparent;
                ButtonSwitchEditVibratoMode.BackgroundColor = mode == EditViewModel.NoteEditMode.EditVibrato ? activeColor : Colors.Transparent;
                // 重绘画布
                PianoRollCanvas.InvalidateSurface();
                PianoRollPitchCanvas.InvalidateSurface();
            })
            .DisposeWith(_disposables);
        // 回放定时器，定时通知回放管理器更新播放位置
        PlaybackTimer = Dispatcher.CreateTimer();
        PlaybackTimer.Interval = TimeSpan.FromSeconds(1 / (double)Preferences.Default.PlaybackRefreshRate);
        PlaybackTimer.Tick += (s, e) =>
        {
            PlaybackManager.Inst.UpdatePlayPos();
            _viewModel.Playing = PlaybackManager.Inst.Playing;
            PlaybackAutoScroll();
        };
        PlaybackTimer.Start();
        // 自动保存定时器
        AutoSaveTimer = Dispatcher.CreateTimer();
        AutoSaveTimer.Interval = TimeSpan.FromSeconds(30);
        AutoSaveTimer.Tick += (s, e) =>
        {
            DocManager.Inst.AutoSave();
        };
        AutoSaveTimer.Start();

        // 订阅opu后端命令
        DocManager.Inst.AddSubscriber(this);
        // 初始进行一次播放位置更新
        DocManager.Inst.ExecuteCmd(new SeekPlayPosTickNotification(0));
        // 更新钢琴键
        PianoKeysCanvas.InvalidateSurface();
        // 检测是否支援OpenGL
        Log.Information("=========================OpenGLES Support=========================");
        Log.Information(IsOpenGLESSupported().ToString());
        // 初始化缩放平移限制
        UpdateTrackCanvasZoomLimit();
        UpdatePianoRollCanvasZoomLimit();
        // 初始設定走带画布变幻器
        _viewModel.TrackTransformer.SetZoomX(0.1f);
        _viewModel.TrackTransformer.SetZoomY(1f);
        _viewModel.TrackTransformer.SetPanX(0f);
        _viewModel.TrackTransformer.SetPanY(0f);
        // 初始设定钢琴卷帘画布变幻器
        _viewModel.PianoRollTransformer.SetZoomX(0.5f);
        _viewModel.PianoRollTransformer.SetZoomY(0.8f);
        _viewModel.PianoRollTransformer.SetPanX(0f);
        _viewModel.PianoRollTransformer.SetPanY(-(float)(48 * _viewModel.HeightPerPianoKey * _viewModel.Density * _viewModel.PianoRollTransformer.ZoomY));
        Debug.WriteLine($"当前走带画布变换器: ZoomX={_viewModel.TrackTransformer.ZoomX}, ZoomY={_viewModel.TrackTransformer.ZoomY}, PanX={_viewModel.TrackTransformer.PanX}, PanY={_viewModel.TrackTransformer.PanY}");
    }

    private void PlaybackAutoScroll()
    {
        if (Preferences.Default.PlaybackAutoScroll != 2 || !_viewModel.Playing)
        {
            return; // 如果不自动滚动或未播放，直接返回
        }
        float playPosX = _viewModel.PlayPosTick * _viewModel.TrackTransformer.ZoomX + _viewModel.TrackTransformer.PanX;
        float viewWidth = (float)TrackCanvas.Width * (float)_viewModel.Density;
        while (playPosX < 0 || playPosX > viewWidth)
        {
            if (playPosX < 0) // 播放位置在左侧不可见区域
            {
                _viewModel.TrackTransformer.SetPanX(_viewModel.TrackTransformer.PanX + viewWidth * 0.8f); // 向右平移80%视图宽度
            }
            else if (playPosX > viewWidth) // 播放位置在右侧不可见区域
            {
                _viewModel.TrackTransformer.SetPanX(_viewModel.TrackTransformer.PanX - viewWidth * 0.8f); // 向左平移80%视图宽度
            }
            playPosX = _viewModel.PlayPosTick * _viewModel.TrackTransformer.ZoomX + _viewModel.TrackTransformer.PanX;
        }
    }

//    private void EnableAndroidBlur()
//    {
//#if ANDROID31_0_OR_GREATER
//        try
//        {
//            object? BorderExtendPlatformView = BorderExtend.Handler?.PlatformView;
//            Debug.WriteLine($"扩展区模糊层原生视图类型: {BorderExtendPlatformView?.GetType().FullName}");
//            if (BorderExtendPlatformView is Android.Views.View androidView)
//            {
//                if (androidView == null)
//                {
//                    Log.Warning("启用模糊效果失败，无法获取扩展区原生视图");
//                    return;
//                }
//                float radius = 20f;
//                var blurEffect = Android.Graphics.RenderEffect.CreateBlurEffect(radius, radius, Android.Graphics.Shader.TileMode.Decal);
//                androidView.SetRenderEffect(blurEffect);
//                Log.Information("启用模糊效果成功");
//            }
//            else
//            {
//                Log.Warning("不是Android原生视图，无法启用模糊效果");
//            }
//        }
//        catch (Exception ex)
//        {
//            Log.Error(ex, "启用模糊效果失败");
//            DocManager.Inst.ExecuteCmd(new ErrorMessageNotification("启用模糊效果失败", ex));
//        }
//#endif
//    }

    private void InitMagnifier()
    {
#if ANDROID29_0_OR_GREATER
        if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.Q)
        {
            object? pianoRollPlatformView = MainEdit.Handler?.PlatformView;
            Debug.WriteLine($"钢琴卷帘原生视图类型: {pianoRollPlatformView?.GetType().FullName}");
            if (pianoRollPlatformView is Android.Views.View pianoRollAndroidView)
            {
                if (pianoRollAndroidView == null)
                {
                    Debug.WriteLine("放大镜初始化失败，无法获取钢琴卷帘原生视图");
                    return;
                }
                magnifier = null;
                magnifier = new Android.Widget.Magnifier.Builder(pianoRollAndroidView)
                    .SetInitialZoom(1.5f)              // 增加缩放倍数
                    .SetSize(600, 450)               // 设置为矩形尺寸 (宽度, 高度)
                    .SetCornerRadius(16f)            // 稍微增加圆角
                    .SetElevation(12f)               // 添加阴影效果
                    .SetClippingEnabled(true)      // 启用裁剪以防止内容溢出
                    .SetDefaultSourceToMagnifierOffset(-270, -270) // 设置放大镜相对于触摸点的默认偏移
                    .Build();
            }
            else
            {
                Debug.WriteLine("不是Android原生视图");
            }
        }
#endif
    }

    protected override bool OnBackButtonPressed()
    {
        _ = AttemptExit();
        return true;
    }

    /// <summary>
    /// 设置各个手势事件
    /// </summary>
    private void SetupGestureEvents()
    {
        #region 走带画布手势事件
        // 订阅点击事件
        _trackGestureProcessor.Tap += (sender, e) =>
        {
            Debug.WriteLine($"点击走带事件: {e.Position}");
            DocManager.Inst.ExecuteCmd(new SeekPlayPosTickNotification((int)_viewModel.TrackTransformer.ActualToLogical(e.Position).X));
            switch (_viewModel.CurrentTrackEditMode)
            {
                case EditViewModel.TrackEditMode.Normal: // 只读模式
                    // 遍历所有可绘制对象，检查点击位置是否在其范围内
                    foreach (var part in _viewModel.DrawableParts)
                    {
                        if (part.IsPointInside(_viewModel.TrackTransformer.ActualToLogical(e.Position)))
                        {
                            if (_viewModel.SelectedParts.Contains(part.Part))
                            {
                                // 如果已经选中，更新播放指针
                                return;
                            }
                            _viewModel.SelectedParts.Clear(); // 清空当前选中状态，改为单选
                            _viewModel.SelectedParts.Add(part.Part); // 添加当前点击的分片
                            _viewModel.HandleSelectedNotesChanged();
                            TrackCanvas.InvalidateSurface();
                            PianoRollCanvas.InvalidateSurface();
                            PianoRollPitchCanvas.InvalidateSurface();
                            PianoRollTickBackgroundCanvas.InvalidateSurface();
                            return;
                        }
                    }
                    // 如果点击位置不在任何可编辑对象上，清空选中状态
                    Debug.WriteLine("点击了空白区域，清空选中状态");
                    _viewModel.SelectedParts.Clear();
                    TrackCanvas.InvalidateSurface();
                    PianoRollCanvas.InvalidateSurface();
                    PianoRollPitchCanvas.InvalidateSurface();
                    break;
                case EditViewModel.TrackEditMode.Edit: // 编辑模式
                    // 先遍历所有可绘制对象，检查点击位置是否在其范围内
                    foreach (var part in _viewModel.DrawableParts)
                    {
                        if (part.IsPointInside(_viewModel.TrackTransformer.ActualToLogical(e.Position)))
                        {
                            Debug.WriteLine($"点击了Part: {part.Part.DisplayName}");
                            if (_viewModel.SelectedParts.Contains(part.Part))
                            {
                                // 如果已经选中，取消选中
                                _viewModel.SelectedParts.Remove(part.Part);
                            }
                            else
                            {
                                _viewModel.SelectedParts.Add(part.Part); // 添加当前点击的分片
                            }
                            _viewModel.HandleSelectedNotesChanged();
                            TrackCanvas.InvalidateSurface();
                            PianoRollCanvas.InvalidateSurface();
                            PianoRollPitchCanvas.InvalidateSurface();
                            return;
                        }
                    }
                    // 如果点击位置不在任何可编辑对象上，清空选中状态
                    Debug.WriteLine("点击了空白区域，清空选中状态");
                    _viewModel.SelectedParts.Clear();
                    TrackCanvas.InvalidateSurface();
                    PianoRollCanvas.InvalidateSurface();
                    PianoRollPitchCanvas.InvalidateSurface();
                    break;
            }
        };
        // 订阅双击事件
        _trackGestureProcessor.DoubleTap += (sender, e) =>
        {
            Debug.WriteLine($"双击走带事件: {e.Position}");
            switch (_viewModel.CurrentTrackEditMode)
            {
                case EditViewModel.TrackEditMode.Normal: // 只读模式
                    break;
                case EditViewModel.TrackEditMode.Edit: // 编辑模式
                    break;
            }
        };
        // 订阅平移开始事件
        _trackGestureProcessor.PanStart += (sender, e) =>
        {
            Debug.WriteLine($"平移开始: {e.StartPosition}");
            switch (_viewModel.CurrentTrackEditMode)
            {
                case EditViewModel.TrackEditMode.Normal: // 只读模式
                    // 开始平移
                    _viewModel.TrackTransformer.StartPan(e.StartPosition);
                    break;
                case EditViewModel.TrackEditMode.Edit: // 编辑模式

                    // 先遍历所有可绘制对象，检查点击位置是否在其范围内
                    foreach (var part in _viewModel.DrawableParts)
                    {
                        if (part.IsPointInHandle(_viewModel.TrackTransformer.ActualToLogical(e.StartPosition)) && part.IsResizable)
                        {
                            Debug.WriteLine($"准备调整选中的对象的长度: {part.Part.DisplayName}");
                            _viewModel.StartResizePart(part.Part, _viewModel.TrackTransformer.ActualToLogical(e.StartPosition));
                            return; // 找到第一个可编辑对象后就停止
                        }
                        if (part.IsPointInside(_viewModel.TrackTransformer.ActualToLogical(e.StartPosition)) && part.IsSelected)
                        {
                            Debug.WriteLine($"准备拖动选中的对象: {part.Part.DisplayName}");
                            _viewModel.StartMoveParts(_viewModel.SelectedParts, _viewModel.TrackTransformer.ActualToLogical(e.StartPosition));
                            return; // 找到第一个可编辑对象后就停止
                        }
                    }
                    // 起始位置不在任何可编辑对象上，开始创建分片
                    _viewModel.StartCreatePart(_viewModel.TrackTransformer.ActualToLogical(e.StartPosition));
                    break;
            }
        };
        // 订阅平移更新事件
        _trackGestureProcessor.PanUpdate += (sender, e) =>
        {

            switch (_viewModel.CurrentTrackEditMode) // 分情况讨论
            {
                case EditViewModel.TrackEditMode.Normal: // 只读模式
                    // 更新平移位置
                    _viewModel.TrackTransformer.UpdatePan(e.Position);
                    // 同步ScrollView的滚动位置
                    UpdateLeftScrollView();
                    break;
                case EditViewModel.TrackEditMode.Edit: // 编辑模式
                    if (_viewModel.IsMovingParts)
                    {
                        _viewModel.UpdateMoveParts(_viewModel.TrackTransformer.ActualToLogical(e.Position)); // 如果正在拖动分片，更新分片位置
                        return;
                    }
                    if (_viewModel.IsCreatingPart) // 如果正在创建分片
                    {
                        // 更新创建分片位置
                        _viewModel.UpdateCreatePart(_viewModel.TrackTransformer.ActualToLogical(e.Position));
                        return;
                    }
                    if (_viewModel.IsResizingPart) // 如果正在调整分片长度
                    {
                        _viewModel.UpdateResizePart(_viewModel.TrackTransformer.ActualToLogical(e.Position));
                        return;
                    }
                    // 同步ScrollView的滚动位置
                    UpdateLeftScrollView();
                    break;
            }
        };
        // 订阅平移结束事件
        _trackGestureProcessor.PanEnd += (sender, e) =>
        {
            switch (_viewModel.CurrentTrackEditMode)
            {
                case EditViewModel.TrackEditMode.Normal: // 只读模式
                    // 结束平移
                    _viewModel.TrackTransformer.EndPan();
                    // TrackCanvas.InvalidateSurface();
                    break;
                case EditViewModel.TrackEditMode.Edit: // 编辑模式
                    if (_viewModel.IsMovingParts)
                    {
                        _viewModel.IsMovingParts = false; // 重置拖动状态
                        _viewModel.EndMoveParts(); // 结束分片拖动
                        return; // 如果是拖动分片，直接返回
                    }
                    if (_viewModel.IsCreatingPart) // 如果正在创建分片
                    {
                        _viewModel.IsCreatingPart = false; // 重置创建状态
                        _viewModel.EndCreatePart(); // 结束创建分片
                        return; // 如果是创建分片，直接返回
                    }
                    if (_viewModel.IsResizingPart) // 如果正在调整分片长度
                    {
                        _viewModel.IsResizingPart = false; // 重置调整状态
                        _viewModel.EndResizePart(); // 结束调整分片
                        return; // 如果是调整分片，直接返回
                    }
                    // 结束平移
                    _viewModel.TrackTransformer.EndPan();
                    break;
            }
        };
        // 订阅缩放开始事件
        _trackGestureProcessor.ZoomStart += (sender, e) =>
        {
            _viewModel.TrackTransformer.StartZoom(e.Point1, e.Point2);
        };
        // 订阅缩放更新事件
        _trackGestureProcessor.ZoomUpdate += (sender, e) =>
        {
            _viewModel.TrackTransformer.UpdateZoom(e.Point1, e.Point2);
            // 同步ScrollView的滚动位置
            UpdateLeftScrollView();
            UpdateTrackCanvasPanLimit();
        };
        // 订阅X轴缩放开始事件
        _trackGestureProcessor.XZoomStart += (sender, e) =>
        {
            _viewModel.TrackTransformer.StartXZoom(e.Point1, e.Point2);
        };
        // 订阅X轴缩放更新事件
        _trackGestureProcessor.XZoomUpdate += (sender, e) =>
        {
            _viewModel.TrackTransformer.UpdateXZoom(e.Point1, e.Point2);
            UpdateTrackCanvasPanLimit();
        };
        // 订阅Y轴缩放开始事件
        _trackGestureProcessor.YZoomStart += (sender, e) =>
        {
            _viewModel.TrackTransformer.StartYZoom(e.Point1, e.Point2);
            // 同步ScrollView的滚动位置
            UpdateLeftScrollView();
        };
        // 订阅Y轴缩放更新事件
        _trackGestureProcessor.YZoomUpdate += (sender, e) =>
        {
            _viewModel.TrackTransformer.UpdateYZoom(e.Point1, e.Point2);
            // 同步ScrollView的滚动位置
            UpdateLeftScrollView();
        };
        #endregion
        #region 钢琴卷帘画布手势事件
        // 订阅点击事件
        _pianoRollGestureProcessor.Tap += (sender, e) =>
        {
            Debug.WriteLine($"点击钢琴卷帘事件: {e.Position}");
            if (_viewModel.EditingNotes == null)
            {
                return; // 如果没有选中歌声分片，直接返回
            }
            switch (_viewModel.CurrentNoteEditMode)
            {
                case EditViewModel.NoteEditMode.EditNote:
                    UNote? hitNote = _viewModel.EditingNotes.IsPointInNote(_viewModel.PianoRollTransformer.ActualToLogical(e.Position));
                    if (hitNote == null) // 如果没有点击音符
                    {
                        if (_viewModel.SelectedNotes.Count() == 0) // 如果当前没有选中任何音符，创建一个新音符
                        {
                            _viewModel.CreateDefaultNote(_viewModel.PianoRollTransformer.ActualToLogical(e.Position));
                        }
                        else // 否则清空选中状态
                        {
                            _viewModel.SelectedNotes.Clear();
                        }
                    }
                    else // 点击到了音符
                    {
                        Debug.WriteLine($"点击了音符: {hitNote.lyric} ({hitNote.tone})");
                        _viewModel.SelectedNotes = [hitNote];
                    }
                    PianoRollCanvas.InvalidateSurface();
                    _viewModel.HandleSelectedNotesChanged();
                    break;
                case EditViewModel.NoteEditMode.EditPitchCurve:
                    break;
                case EditViewModel.NoteEditMode.EditPitchAnchor:
                    break;
                case EditViewModel.NoteEditMode.EditVibrato:
                    break;
                default:
                    break;
            }
        };
        // 订阅双击事件
        _pianoRollGestureProcessor.DoubleTap += async (sender, e) =>
        {
            Debug.WriteLine($"双击钢琴卷帘事件: {e.Position}");
            if (_viewModel.EditingNotes == null)
            {
                return; // 如果没有选中歌声分片，直接返回
            }
            switch (_viewModel.CurrentNoteEditMode)
            {
                case EditViewModel.NoteEditMode.EditNote:
                    UNote? hitNote = _viewModel.EditingNotes.IsPointInNote(_viewModel.PianoRollTransformer.ActualToLogical(e.Position));
                    if (hitNote != null && _viewModel.EditingPart is UVoicePart editingPart) // 双击音符，编辑歌词
                    {
                        Debug.WriteLine($"双击了音符: {hitNote.lyric} ({hitNote.tone})");
                        Popup editPopup = new EditLyricsPopup(editingPart, hitNote);
                        object? _ = await this.ShowPopupAsync(editPopup);
                    }
                    break;
                case EditViewModel.NoteEditMode.EditPitchCurve:
                    break;
                case EditViewModel.NoteEditMode.EditPitchAnchor:
                    break;
                case EditViewModel.NoteEditMode.EditVibrato:
                    break;
                default:
                    break;
            }
        };
        // 订阅平移开始事件
        _pianoRollGestureProcessor.PanStart += (sender, e) =>
        {
            //Debug.WriteLine($"钢琴卷帘平移开始事件: {e.StartPosition}");
            switch (_viewModel.CurrentNoteEditMode)
            {
                // 在手柄上？ => 调整音符长度
                // 在音符上？ => 拖动音符
                // 否则 => 平移画布
                case EditViewModel.NoteEditMode.EditNote:
                    if (_viewModel.SelectedNotes.Count > 0 && _viewModel.EditingNotes != null && _viewModel.EditingPart != null)
                    {
                        UNote? resizingNote = _viewModel.EditingNotes.IsPointInHandle(_viewModel.PianoRollTransformer.ActualToLogical(e.StartPosition));
                        if (resizingNote != null)
                        {
                            Debug.WriteLine($"准备调整选中的音符的长度");
                            _viewModel.StartResizeNotes(_viewModel.PianoRollTransformer.ActualToLogical(e.StartPosition), resizingNote);
                            return; // 找到手柄后就停止
                        }
                        UNote? hitNote = _viewModel.EditingNotes.IsPointInNote(_viewModel.PianoRollTransformer.ActualToLogical(e.StartPosition));
                        if (hitNote != null && _viewModel.SelectedNotes.Contains(hitNote))
                        {
                            Debug.WriteLine($"准备拖动选中的音符");
                            _viewModel.StartMoveNotes(_viewModel.PianoRollTransformer.ActualToLogical(e.StartPosition));
                            return; // 找到选中的音符后就停止
                        }
                        _viewModel.PianoRollTransformer.StartPan(e.StartPosition); // 否则平移画布
                        return;
                    }
                    // 没有选中音符或编辑的音符组，平移画布
                    _viewModel.PianoRollTransformer.StartPan(e.StartPosition);
                    break;
                case EditViewModel.NoteEditMode.EditPitchCurve:
                    if (_viewModel.EditingPart == null || _viewModel.EditingNotes == null)
                    {
                        return; // 如果没有选中歌声分片或音符，直接返回
                    }
                    _viewModel.StartDrawPitch(e.StartPosition);
                    IsUserDrawingCurve = true;
#if ANDROID29_0_OR_GREATER
                    //Android.Views.View? pianoRollAndroidView = sender as Android.Views.View;
                    if (magnifier == null)
                    {
                        Debug.WriteLine("放大镜未初始化");
                        break;
                    }
                    magnifier.Show(e.StartPosition.X + 60f * (float)_viewModel.Density, e.StartPosition.Y);
#endif
                    break;
                case EditViewModel.NoteEditMode.EditPitchAnchor:
                    break;
                case EditViewModel.NoteEditMode.EditVibrato:
                    break;
                default:
                    break;
            }
        };
        // 订阅平移更新事件
        _pianoRollGestureProcessor.PanUpdate += (sender, e) =>
        {
            switch (_viewModel.CurrentNoteEditMode)
            {
                case EditViewModel.NoteEditMode.EditNote:
                    if (_viewModel.IsMovingNotes)
                    {
                        _viewModel.UpdateMoveNotes(_viewModel.PianoRollTransformer.ActualToLogical(e.Position)); // 如果正在拖动音符，更新音符位置
                        return;
                    }
                    if (_viewModel.IsResizingNote) // 如果正在调整音符长度
                    {
                        _viewModel.UpdateResizeNotes(_viewModel.PianoRollTransformer.ActualToLogical(e.Position));
                        return;
                    }
                    // 更新平移位置
                    _viewModel.PianoRollTransformer.UpdatePan(e.Position);
                    break;
                case EditViewModel.NoteEditMode.EditPitchCurve:
                    if (_viewModel.EditingPart == null || _viewModel.EditingNotes == null)
                    {
                        return; // 如果没有选中歌声分片或音符，直接返回
                    }
                    _viewModel.UpdateDrawPitch(_viewModel.PianoRollTransformer.ActualToLogical(e.Position));
                    TouchingPoint = e.Position;
#if ANDROID29_0_OR_GREATER
                    if (magnifier == null)
                    {
                        Debug.WriteLine("放大镜未初始化");
                        break;
                    }
                    magnifier.Show(e.Position.X + 60f * (float)_viewModel.Density, e.Position.Y);
#endif
                    break;
                case EditViewModel.NoteEditMode.EditPitchAnchor:
                    break;
                case EditViewModel.NoteEditMode.EditVibrato:
                    break;
                default:
                    break;
            }
        };
        // 订阅平移结束事件
        _pianoRollGestureProcessor.PanEnd += (sender, e) =>
            {
                switch (_viewModel.CurrentNoteEditMode)
                {
                    case EditViewModel.NoteEditMode.EditNote:
                        if (_viewModel.IsMovingNotes)
                        {
                            _viewModel.IsMovingNotes = false; // 重置拖动状态
                            _viewModel.EndMoveNotes(); // 结束音符拖动
                            return; // 如果是拖动音符，直接返回
                        }
                        else if (_viewModel.IsResizingNote) // 如果正在调整音符长度
                        {
                            _viewModel.IsResizingNote = false; // 重置调整状态
                            _viewModel.EndResizeNotes(); // 结束调整音符
                            return; // 如果是调整音符，直接返回
                        }
                        else
                        {
                            // 结束平移
                            _viewModel.PianoRollTransformer.EndPan();
                            // 更新回放位置
                            if (!_viewModel.Playing)
                            {
                                int newPlayPosTick = (int)((ViewConstants.PianoRollPlaybackLinePos * _viewModel.Density - _viewModel.PianoRollTransformer.PanX) / _viewModel.PianoRollTransformer.ZoomX);
                                if (newPlayPosTick != _viewModel.PlayPosTick)
                                {
                                    DocManager.Inst.ExecuteCmd(new SeekPlayPosTickNotification(newPlayPosTick));
                                }
                            }
                        }
                        break;
                    case EditViewModel.NoteEditMode.EditPitchCurve:
                        _viewModel.EndDrawPitch();
                        IsUserDrawingCurve = false;
                        PianoRollPitchCanvas.InvalidateSurface();
#if ANDROID29_0_OR_GREATER
                        if (magnifier == null)
                        {
                            Debug.WriteLine("放大镜未初始化");
                            break;
                        }
                        magnifier.Dismiss();
#endif
                        break;
                    case EditViewModel.NoteEditMode.EditPitchAnchor:
                        break;
                    case EditViewModel.NoteEditMode.EditVibrato:
                        break;
                    default:
                        break;
                }
            };
        // 订阅缩放开始事件
        _pianoRollGestureProcessor.ZoomStart += (sender, e) =>
            {
                _viewModel.PianoRollTransformer.StartZoom(e.Point1, e.Point2);
            };
        // 订阅缩放更新事件
        _pianoRollGestureProcessor.ZoomUpdate += (sender, e) =>
            {
                _viewModel.PianoRollTransformer.UpdateZoom(e.Point1, e.Point2);
                UpdatePianoRollCanvasPanLimit();
            };
        // 订阅X轴缩放开始事件
        _pianoRollGestureProcessor.XZoomStart += (sender, e) =>
            {
                _viewModel.PianoRollTransformer.StartXZoom(e.Point1, e.Point2);
            };
        // 订阅X轴缩放更新事件
        _pianoRollGestureProcessor.XZoomUpdate += (sender, e) =>
            {
                _viewModel.PianoRollTransformer.UpdateXZoom(e.Point1, e.Point2);
                UpdatePianoRollCanvasPanLimit();
            };
        // 订阅Y轴缩放开始事件
        _pianoRollGestureProcessor.YZoomStart += (sender, e) =>
            {
                _viewModel.PianoRollTransformer.StartYZoom(e.Point1, e.Point2);
            };
        // 订阅Y轴缩放更新事件
        _pianoRollGestureProcessor.YZoomUpdate += (sender, e) =>
            {
                _viewModel.PianoRollTransformer.UpdateYZoom(e.Point1, e.Point2);
                UpdatePianoRollCanvasPanLimit();
            };
        #endregion
        #region 时间轴画布手势事件
        // 订阅点击事件
        _timeLineGestureProcessor.Tap += (sender, e) =>
            {
                Debug.WriteLine($"点击时间轴事件: {e.Position}");
                int tick = (int)_viewModel.TrackTransformer.ActualToLogical(e.Position).X;
                if (_viewModel.IsTrackSnapToGrid)
                {
                    tick = _viewModel.TrackTickToLinedTick(tick);
                }
                // 设置播放位置
                DocManager.Inst.ExecuteCmd(new SeekPlayPosTickNotification(tick));
            };
        // 订阅双击事件
        _timeLineGestureProcessor.DoubleTap += async (sender, e) =>
            {
                Debug.WriteLine($"双击时间轴事件: {e.Position}");
                Popup popup = new InsertTempoOrTimeSignaturePopup(_viewModel.PlayPosTick);
                object? result = await this.ShowPopupAsync(popup);
                if (result != null)
                {
                    if (result is Tuple<int, double> tuple)
                    {
                        _viewModel.AddTempoSignature(tuple.Item1, tuple.Item2);
                        return;
                    }
                    if (result is Tuple<int, int, int> tuple2)
                    {
                        _viewModel.AddTimeSignature(tuple2.Item1, tuple2.Item2, tuple2.Item3);
                        return;
                    }
                }
            };
        // 订阅平移开始事件
        _timeLineGestureProcessor.PanStart += (sender, e) =>
            {
                Debug.WriteLine($"时间轴平移开始: {e.StartPosition}");
                // 开始平移
                _viewModel.TrackTransformer.StartPan(e.StartPosition);
            };
        // 订阅平移更新事件
        _timeLineGestureProcessor.PanUpdate += (sender, e) =>
            {
                Debug.WriteLine($"时间轴平移更新: {e.Position}");
                // 更新平移位置
                _viewModel.TrackTransformer.UpdatePan(e.Position);
                // 同步ScrollView的滚动位置
                UpdateLeftScrollView();
            };
        // 订阅平移结束事件
        _timeLineGestureProcessor.PanEnd += (sender, e) =>
            {
                Debug.WriteLine("时间轴平移结束");
                // 结束平移
                _viewModel.TrackTransformer.EndPan();
            };
        // 订阅缩放开始事件
        _timeLineGestureProcessor.ZoomStart += (sender, e) =>
            {
                _viewModel.TrackTransformer.StartZoom(e.Point1, e.Point2);
            };
        // 订阅缩放更新事件
        _timeLineGestureProcessor.ZoomUpdate += (sender, e) =>
            {
                _viewModel.TrackTransformer.UpdateZoom(e.Point1, e.Point2);
                // 同步ScrollView的滚动位置
                UpdateLeftScrollView();
            };
        // 订阅X轴缩放开始事件
        _timeLineGestureProcessor.XZoomStart += (sender, e) =>
            {
                _viewModel.TrackTransformer.StartXZoom(e.Point1, e.Point2);
            };
        // 订阅X轴缩放更新事件
        _timeLineGestureProcessor.XZoomUpdate += (sender, e) =>
            {
                _viewModel.TrackTransformer.UpdateXZoom(e.Point1, e.Point2);
            };
        // 订阅Y轴缩放开始事件
        _timeLineGestureProcessor.YZoomStart += (sender, e) =>
            {
                _viewModel.TrackTransformer.StartYZoom(e.Point1, e.Point2);
                // 同步ScrollView的滚动位置
                UpdateLeftScrollView();
            };
        // 订阅Y轴缩放更新事件
        _timeLineGestureProcessor.YZoomUpdate += (sender, e) =>
            {
                _viewModel.TrackTransformer.UpdateYZoom(e.Point1, e.Point2);
                // 同步ScrollView的滚动位置
                UpdateLeftScrollView();
            };
        #endregion
        #region 音素画布手势事件
        // 订阅点击事件
        _phonemeGestureProcessor.Tap += (sender, e) =>
        {
            Debug.WriteLine($"点击音素画布事件: {e.Position}");
        };
        // 订阅双击事件
        _phonemeGestureProcessor.DoubleTap += (sender, e) =>
        {
            Debug.WriteLine($"双击音素画布事件: {e.Position}");
        };
        // 订阅平移开始事件
        _phonemeGestureProcessor.PanStart += (sender, e) =>
        {

        };
        // 订阅平移更新事件
        _phonemeGestureProcessor.PanUpdate += (sender, e) =>
        {
        };
        // 订阅平移结束事件
        _phonemeGestureProcessor.PanEnd += (sender, e) =>
        {
        };
        // 订阅缩放开始事件
        _phonemeGestureProcessor.ZoomStart += (sender, e) =>
        {
            _viewModel.PianoRollTransformer.StartZoom(e.Point1, e.Point2);
        };
        // 订阅缩放更新事件
        _phonemeGestureProcessor.ZoomUpdate += (sender, e) =>
        {
            _viewModel.PianoRollTransformer.UpdateZoom(e.Point1, e.Point2);
        };
        // 订阅X轴缩放开始事件
        _phonemeGestureProcessor.XZoomStart += (sender, e) =>
        {
            _viewModel.PianoRollTransformer.StartXZoom(e.Point1, e.Point2);
        };
        // 订阅X轴缩放更新事件
        _phonemeGestureProcessor.XZoomUpdate += (sender, e) =>
        {
            _viewModel.PianoRollTransformer.UpdateXZoom(e.Point1, e.Point2);
        };
        // 订阅Y轴缩放开始事件
        _phonemeGestureProcessor.YZoomStart += (sender, e) =>
        {
            _viewModel.PianoRollTransformer.StartYZoom(e.Point1, e.Point2);
        };
        // 订阅Y轴缩放更新事件
        _phonemeGestureProcessor.YZoomUpdate += (sender, e) =>
        {
            _viewModel.PianoRollTransformer.UpdateYZoom(e.Point1, e.Point2);
        };
        #endregion
        #region 表情画布手势事件
        // 订阅点击事件
        _expressionGestureProcessor.Tap += (sender, e) =>
        {
            Debug.WriteLine($"点击表情画布事件: {e.Position}");
        };
        // 订阅双击事件
        _expressionGestureProcessor.DoubleTap += (sender, e) =>
        {
            Debug.WriteLine($"双击表情画布事件: {e.Position}");
        };
        // 订阅平移开始事件
        _expressionGestureProcessor.PanStart += (sender, e) =>
        {
            _viewModel.PianoRollTransformer.StartPan(e.StartPosition);
        };
        // 订阅平移更新事件
        _expressionGestureProcessor.PanUpdate += (sender, e) =>
        {
            _viewModel.PianoRollTransformer.UpdatePan(e.Position);
        };
        // 订阅平移结束事件
        _expressionGestureProcessor.PanEnd += (sender, e) =>
        {
            _viewModel.PianoRollTransformer.EndPan();
            if (!_viewModel.Playing)
            {
                return;
            }
            int newPlayPosTick = (int)((ViewConstants.PianoRollPlaybackLinePos * _viewModel.Density - _viewModel.PianoRollTransformer.PanX) / _viewModel.PianoRollTransformer.ZoomX);
            if (newPlayPosTick != _viewModel.PlayPosTick)
            {
                DocManager.Inst.ExecuteCmd(new SeekPlayPosTickNotification(newPlayPosTick));
            }

        };
        // 订阅缩放开始事件
        _expressionGestureProcessor.ZoomStart += (sender, e) =>
        {
            _viewModel.PianoRollTransformer.StartZoom(e.Point1, e.Point2);
        };
        // 订阅缩放更新事件
        _expressionGestureProcessor.ZoomUpdate += (sender, e) =>
        {
            _viewModel.PianoRollTransformer.UpdateZoom(e.Point1, e.Point2);
        };
        // 订阅X轴缩放开始事件
        _expressionGestureProcessor.XZoomStart += (sender, e) =>
        {
            _viewModel.PianoRollTransformer.StartXZoom(e.Point1, e.Point2);
        };
        // 订阅X轴缩放更新事件
        _expressionGestureProcessor.XZoomUpdate += (sender, e) =>
        {
            _viewModel.PianoRollTransformer.UpdateXZoom(e.Point1, e.Point2);
        };
        // 订阅Y轴缩放开始事件
        _expressionGestureProcessor.YZoomStart += (sender, e) =>
        {
            _viewModel.PianoRollTransformer.StartYZoom(e.Point1, e.Point2);
        };
        // 订阅Y轴缩放更新事件
        _expressionGestureProcessor.YZoomUpdate += (sender, e) =>
        {
            _viewModel.PianoRollTransformer.UpdateYZoom(e.Point1, e.Point2);
        };
        #endregion

    }
    private void UpdateLeftScrollView()
    {
        if (!isScrollingSyncInProgress)
        {
            isScrollingSyncInProgress = true;
            try
            {
                // 计算应该滚动到的y位置 (TransformerPanY是负值，除以密度得到滚动位置)
                double scrollY = -_viewModel.TrackTransformer.PanY / _viewModel.Density;

                ScrollTrckHeaders.ScrollToAsync(0, scrollY, false);
            }
            catch (Exception ex)
            {
                // 记录异常但不阻断流程
                Debug.WriteLine($"滚动同步错误: {ex.Message}");
            }
            finally
            {
                isScrollingSyncInProgress = false;
            }
        }
    }

    public void OnNext(UCommand cmd, bool isUndo)
    {
        if (cmd is SetPlayPosTickNotification setPlayPosTickNotification)
        {
            _viewModel.PlayPosTick = setPlayPosTickNotification.playPosTick;
            _viewModel.PlayPosWaitingRendering = setPlayPosTickNotification.waitingRendering;
            _viewModel.PianoRollTransformer.SetPanX((float)(ViewConstants.PianoRollPlaybackLinePos * _viewModel.Density - _viewModel.PlayPosTick * _viewModel.PianoRollTransformer.ZoomX));
        }
        else if (cmd is ProgressBarNotification progressBarNotification)
        {
            ProgressbarWaitingRender.Progress = progressBarNotification.Progress / 100f;
            LabelProgress.Text = $"{progressBarNotification.Progress:0.##}%";
            LabelProgressMsg.Text = progressBarNotification.Info;
        }
        else if (cmd is SetCurveCommand setCurveCommand)
        {
            PianoRollPitchCanvas.InvalidateSurface();
            ExpressionCanvas.InvalidateSurface();
        }
        else if (cmd is AddNoteCommand addNoteCommand)
        {
            _viewModel.HandleSelectedNotesChanged();
            PianoRollCanvas.InvalidateSurface();
            TrackCanvas.InvalidateSurface(); // 重绘走带画布
            PianoRollPitchCanvas.InvalidateSurface();
            PianoRollPitchCanvas.InvalidateSurface();
        }
        else if (cmd is MoveNoteCommand moveNoteCommand)
        {
            PianoRollCanvas.InvalidateSurface();
            TrackCanvas.InvalidateSurface();
            PianoRollPitchCanvas.InvalidateSurface();
        }
        else if (cmd is ResizeNoteCommand resizeNoteCommand)
        {
            PianoRollCanvas.InvalidateSurface();
            TrackCanvas.InvalidateSurface();
            PianoRollPitchCanvas.InvalidateSurface();
        }
        else if (cmd is MovePartCommand movePartCommand)
        {
            UpdateTrackCanvasPanLimit();
            UpdatePianoRollCanvasPanLimit();
            TrackCanvas.InvalidateSurface(); // 重绘走带画布
            PianoRollCanvas.InvalidateSurface(); // 重绘钢琴卷帘画布
            PianoRollPitchCanvas.InvalidateSurface();
            PianoRollTickBackgroundCanvas.InvalidateSurface();
        }
        else if (cmd is AddPartCommand addPartCommand)
        {
            TrackCanvas.InvalidateSurface(); // 重绘走带画布
        }
        else if (cmd is ResizePartCommand resizePartCommand)
        {
            UpdateTrackCanvasPanLimit();
            UpdatePianoRollCanvasPanLimit();
            TrackCanvas.InvalidateSurface();
            PianoRollCanvas.InvalidateSurface(); // 重绘钢琴卷帘画布
            PianoRollTickBackgroundCanvas.InvalidateSurface();
        }
        else if (cmd is RemovePartCommand removePartCommand)
        {
            if (_viewModel.EditingPart == removePartCommand.part)
            {
                _viewModel.EditingPart = null;
                _viewModel.EditingNotes = null;
                _viewModel.SelectedNotes = [];
            }
            _viewModel.SelectedParts.Remove(removePartCommand.part);
            _viewModel.UpdateIsShowRenderPitchButton();
            UpdateTrackCanvasPanLimit();
            UpdatePianoRollCanvasPanLimit();
            TrackCanvas.InvalidateSurface(); // 重绘走带画布
            PianoRollCanvas.InvalidateSurface(); // 重绘钢琴卷帘画布
            PianoRollPitchCanvas.InvalidateSurface();
            PianoRollTickBackgroundCanvas.InvalidateSurface();
        }
        else if (cmd is RenamePartCommand renamePartCommand)
        {
            TrackCanvas.InvalidateSurface(); // 重绘走带画布
        }
        else if (cmd is AddTrackCommand addTrackCommand)
        {
            _viewModel.Tracks = [.. DocManager.Inst.Project.tracks];
            TrackCanvas.InvalidateSurface();
            UpdateTrackCanvasZoomLimit();
        }
        else if (cmd is PhonemizingNotification phonemizingNotification)
        {
            _viewModel.SetWork(type: WorkType.Phonemize, id: phonemizingNotification.part.GetHashCode().ToString(), detail: phonemizingNotification.part.DisplayName);
        }
        else if (cmd is PhonemizedNotification phonemizedNotification)
        {
            _viewModel.RemoveWork(id: phonemizedNotification.part.GetHashCode().ToString());
            PhonemeCanvas.InvalidateSurface();
            ExpressionCanvas.InvalidateSurface();
            PianoRollPitchCanvas.InvalidateSurface();
        }
        else if (cmd is LoadingNotification loadingNotification)
        {
            if (loadingNotification.startLoading)
            {
                WorkType workType = WorkType.Other;
                if (loadingNotification.window == typeof(UWavePart))
                {
                    workType = WorkType.ReadWave;
                }
                _viewModel.SetWork(type: workType, id: loadingNotification.loadObject);
            }
            else
            {
                _viewModel.RemoveWork(loadingNotification.loadObject);
                TrackCanvas.InvalidateSurface();
            }
        }
        else if (cmd is ChangeNoteLyricCommand changeNoteLyricCommand)
        {
            PianoRollCanvas.InvalidateSurface();
        }
        else if (cmd is RemoveNoteCommand removeNoteCommand)
        {
            _viewModel.HandleSelectedNotesChanged();
            PianoRollCanvas.InvalidateSurface();
            TrackCanvas.InvalidateSurface();
            PianoRollPitchCanvas.InvalidateSurface();
            PhonemeCanvas.InvalidateSurface();
            ExpressionCanvas.InvalidateSurface();
        }
        else if (cmd is RemoveTrackCommand removeTrackCommand)
        {
            _viewModel.ValidateSelectedParts(); // 验证选中分片中是否有被删除的分片
            _viewModel.RefreshTrack();
            _viewModel.HandleSelectedNotesChanged();
            TrackCanvas.InvalidateSurface();
            PianoRollCanvas.InvalidateSurface(); // 重绘钢琴卷帘画布
            UpdateTrackCanvasZoomLimit();
            PianoRollPitchCanvas.InvalidateSurface();
            PhonemeCanvas.InvalidateSurface();
            ExpressionCanvas.InvalidateSurface();
        }
        else if (cmd is TrackChangeSingerCommand trackChangeSingerCommand)
        {
            if (_viewModel.Tracks.Remove(trackChangeSingerCommand.track))
            {
                _viewModel.Tracks.Insert(trackChangeSingerCommand.track.TrackNo, trackChangeSingerCommand.track);
            }
            _viewModel.UpdateIsShowRenderPitchButton();
            TrackCanvas.InvalidateSurface(); // 重绘走带画布
            PianoRollCanvas.InvalidateSurface(); // 重绘钢琴卷帘画布
            _viewModel.LoadPortrait();
        }
        else if (cmd is AddTempoChangeCommand addTempoChangeCommand)
        {
            TrackCanvas.InvalidateSurface(); // 重绘走带画布
            PlaybackTickBackgroundCanvas.InvalidateSurface();
            RefreshProjectInfoDisplay();
        }
        else if (cmd is AddTimeSigCommand addTimeSigCommand)
        {
            TrackCanvas.InvalidateSurface(); // 重绘走带画布
            PlaybackTickBackgroundCanvas.InvalidateSurface();
            RefreshProjectInfoDisplay();
        }
        else if (cmd is BpmCommand bpmCommand)
        {
            TrackCanvas.InvalidateSurface(); // 重绘走带画布
            PlaybackTickBackgroundCanvas.InvalidateSurface();
            RefreshProjectInfoDisplay();
        }
        else if (cmd is TimeSignatureCommand timeSigCommand)
        {
            TrackCanvas.InvalidateSurface(); // 重绘走带画布
            PlaybackTickBackgroundCanvas.InvalidateSurface();
            RefreshProjectInfoDisplay();
        }
        else if (cmd is TrackChangePhonemizerCommand phonemizerCommand)
        {
            _viewModel.RefreshTrack(phonemizerCommand.track);
        }
        else if (cmd is ExportingNotification exportingNotification)
        {
            _viewModel.SetWork(WorkType.Export, exportingNotification.Id, exportingNotification.Progress, exportingNotification.Info);
        }
        else if (cmd is ExportedNotification exportedNotification)
        {
            _viewModel.RemoveWork(id: exportedNotification.Id);
            Toast.Make(exportedNotification.Info, CommunityToolkit.Maui.Core.ToastDuration.Short, 16).Show();
        }
        else if (cmd is KeyCommand keyCommand)
        {
            PianoKeysCanvas.InvalidateSurface();
            RefreshProjectInfoDisplay();
        }
        else if (cmd is ChangeTrackColorCommand changeTrackColorCommand)
        {
            _viewModel.RefreshTrack(changeTrackColorCommand.track);
            TrackCanvas.InvalidateSurface(); // 重绘走带画布
            PianoRollCanvas.InvalidateSurface(); // 重绘钢琴卷帘画布
            ExpressionCanvas.InvalidateSurface();
            if (_viewModel.EditingPart == null)
                return;
            _viewModel.EditingPartColor = ViewConstants.TrackMauiColors[DocManager.Inst.Project.tracks[_viewModel.EditingPart.trackNo].TrackColor];
        }
        else if (cmd is RenameTrackCommand renameTrackCommand)
        {
            _viewModel.RefreshTrack(renameTrackCommand.track);
        }
        else if (cmd is LoadProjectNotification loadProject)
        {
            OpenUtau.Core.Util.Preferences.AddRecentFileIfEnabled(loadProject.project.FilePath);
            _viewModel.Tracks = [.. OpenUtau.Core.DocManager.Inst.Project.tracks];
            _viewModel.Path = OpenUtau.Core.DocManager.Inst.Project.FilePath;
            PianoKeysCanvas.InvalidateSurface();
            PianoRollCanvas.InvalidateSurface();
            PianoRollTickBackgroundCanvas.InvalidateSurface();
            PlaybackPosCanvas.InvalidateSurface();
            TrackCanvas.InvalidateSurface();
            PlaybackTickBackgroundCanvas.InvalidateSurface();
            PianoRollPitchCanvas.InvalidateSurface();
            UpdateTrackCanvasZoomLimit();
            UpdatePianoRollCanvasZoomLimit();
            RefreshProjectInfoDisplay();
            _viewModel.InitExpressions();
        }
        else if (cmd is SaveProjectNotification saveProjectNotification)
        {
            OpenUtau.Core.Util.Preferences.AddRecentFileIfEnabled(saveProjectNotification.Path);
            _viewModel.Path = OpenUtau.Core.DocManager.Inst.Project.FilePath;
#if !WINDOWS
            CommunityToolkit.Maui.Alerts.Toast.Make(AppResources.Saved, CommunityToolkit.Maui.Core.ToastDuration.Short, 16).Show();
#endif
        }
    }

    #region 分割线
    /// <summary>
    /// 分隔线拖动事件
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void DivControlPanUpdated(object sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                //Debug.WriteLine("Pan started");
                OriginTrackMainEditDivPosY = _viewModel.DivPosY;
                break;
            case GestureStatus.Running:
                //Debug.WriteLine($"Pan running: {e.TotalX}, {e.TotalY}");
                _viewModel.DivPosY = Math.Clamp(OriginTrackMainEditDivPosY + e.TotalY, 0d, _viewModel.MainLayoutHeight - 50d);
                break;
            case GestureStatus.Completed:
                //Debug.WriteLine("Pan completed");
                _viewModel.SetBoundDivControl();
                UpdateTrackCanvasZoomLimit();
                UpdatePianoRollCanvasZoomLimit();
                break;
            case GestureStatus.Canceled:
                //Debug.WriteLine("Pan canceled");
                break;
        }
    }

    private void PanExpDivider_PanUpdated(object sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                OriginExpHeight = _viewModel.ExpHeight;
                break;
            case GestureStatus.Running:
                //Debug.WriteLine($"Pan running: {e.TotalX}, {e.TotalY}");
                _viewModel.ExpHeight = Math.Clamp(OriginExpHeight - e.TotalY, 50d, _viewModel.MainEditHeight);
                Debug.WriteLine($"ExpHeight: {_viewModel.ExpHeight}, MainEditHeight: {_viewModel.MainEditHeight}");
                break;
            case GestureStatus.Completed:
                //Debug.WriteLine("Pan completed");
                _viewModel.SetBoundExpDivControl();
                break;
            case GestureStatus.Canceled:
                //Debug.WriteLine("Pan canceled");
                break;
        }
    }

    private void UpdateTrackCanvasPanLimit()
    {
        int lastTick = 0;
        // 找到所有轨道中最右边的分片的结束位置
        foreach (UPart part in DocManager.Inst.Project.parts)
        {
            lastTick = Math.Max(lastTick, part.End);
        }
        lastTick += 9600;
        float minX = Math.Min(0f, (float)TrackCanvas.Width * (float)_viewModel.Density - lastTick * _viewModel.TrackTransformer.ZoomX);
        float maxX = 0f;
        float minY = (float)Math.Min((_viewModel.DivPosY - 20 - _viewModel.HeightPerTrack * (_viewModel.Tracks.Count + 1)) * _viewModel.Density, 0f);
        float maxY = 0f;
        _viewModel.TrackTransformer.SetPanLimit(minX, maxX, minY, maxY);
    }

    private void UpdatePianoRollCanvasPanLimit()
    {
        int lastTick = 0;
        // 找到所有轨道中最右边的分片的结束位置
        foreach (UPart part in DocManager.Inst.Project.parts)
        {
            lastTick = Math.Max(lastTick, part.End);
        }
        lastTick += 9600;
        float minX = Math.Min(0f, (float)PianoRollCanvas.Width * (float)_viewModel.Density - lastTick * _viewModel.PianoRollTransformer.ZoomX);
        float maxX = (float)(ViewConstants.PianoRollPlaybackLinePos * _viewModel.Density);
        float minY = (float)Math.Min(
            (_viewModel.MainLayoutHeight - _viewModel.DivPosY - 60 - _viewModel.HeightPerPianoKey * ViewConstants.TotalPianoKeys * _viewModel.PianoRollTransformer.ZoomY) * _viewModel.Density,
            0f);
        float maxY = 0f;
        _viewModel.PianoRollTransformer.SetPanLimit(minX, maxX, minY, maxY);
        Debug.WriteLine($"PianoRoll PanLimit updated: minX={minX}, lastTick={lastTick}");
    }

    private void UpdateTrackCanvasZoomLimit()
    {
        float minZoomX = 0.05f;
        float maxZoomX = 1f;
        float minZoomY = 1f;
        float maxZoomY = 1f;
        _viewModel.TrackTransformer.SetZoomLimit(minZoomX, maxZoomX, minZoomY, maxZoomY);
        UpdateTrackCanvasPanLimit();
    }

    private void UpdatePianoRollCanvasZoomLimit()
    {
        float minZoomX = 0.1f;
        float maxZoomX = 5f;
        float minZoomY = 0.2f;
        float maxZoomY = 3f;
        _viewModel.PianoRollTransformer.SetZoomLimit(minZoomX, maxZoomX, minZoomY, maxZoomY);
        UpdatePianoRollCanvasPanLimit();
    }

    //private void UpdatePanLimit()
    //{
    //    _viewModel.TrackTransformer.SetPanLimit(
    //        -100000f,
    //        0f,
    //        (float)Math.Min((_viewModel.DivPosY - 20 - _viewModel.HeightPerTrack * (_viewModel.Tracks.Count + 1)) * _viewModel.Density, 0f),
    //        0f);
    //    _viewModel.PianoRollTransformer.SetPanLimit(
    //        minX: -100000f,
    //        maxX: (float)(ViewConstants.PianoRollPlaybackLinePos * _viewModel.Density),
    //        minY: (float)Math.Min((_viewModel.MainLayoutHeight - _viewModel.DivPosY - _viewModel.HeightPerPianoKey * ViewConstants.TotalPianoKeys) * _viewModel.Density, 0f),
    //        maxY: 0f);
    //    //pianoKeysTransformer.SetPanLimit(
    //    //    minX: 0f,
    //    //    maxX: 0f,
    //    //    minY: (float)Math.Min((_viewModel.MainLayoutHeight - _viewModel.DivPosY - _viewModel.HeightPerPianoKey * ViewConstants.TotalPianoKeys) * _viewModel.Density, 0f),
    //    //    maxY: 0f);
    //}
    #endregion

    // ScrollView滚动事件处理
    private void ScrollTrckHeaders_Scrolled(object? sender, ScrolledEventArgs e)
    {
        if (isScrollingSyncInProgress)
            return;

        isScrollingSyncInProgress = true;
        try
        {
            // 当ScrollView滚动时，同步更新_viewModel.TrackTransformer的Y轴平移位置
            _viewModel.TrackTransformer.SetPanY((float)(-e.ScrollY * _viewModel.Density));
            // 重绘画布
            TrackCanvas.InvalidateSurface();
            // PlaybackTickBackgroundCanvas.InvalidateSurface();
        }
        finally
        {
            isScrollingSyncInProgress = false;
        }
    }

    private void TrackCanvas_Touch(object sender, SkiaSharp.Views.Maui.SKTouchEventArgs e)
    {
        // 处理触摸事件
        _trackGestureProcessor.ProcessTouch(sender, e);
    }

    /// <summary>
    /// 走带画布重绘
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void TrackCanvas_PaintSurface(object sender, SkiaSharp.Views.Maui.SKPaintSurfaceEventArgs e)
    {
        // 清空画布
        e.Surface.Canvas.Clear(SKColors.Transparent);
        if (e.Surface.Canvas.DeviceClipBounds.Height < 5) // 没办法，提高性能
        {
            return;
        }
        // 设置画布变换
        e.Surface.Canvas.SetMatrix(_viewModel.TrackTransformer.GetTransformMatrix());
        // 清空可绘制对象集合
        _viewModel.DrawableParts.Clear();
        // 绘制轨道分割线
        DrawableTrackBackground drawableTrackBackground = new(e.Surface.Canvas, _viewModel.HeightPerTrack * _viewModel.Density);
        drawableTrackBackground.Draw();
        // 绘制分片
        foreach (UPart part in DocManager.Inst.Project.parts)
        {
            bool isResizeable = _viewModel.SelectedParts.Contains(part) && _viewModel.CurrentTrackEditMode == EditViewModel.TrackEditMode.Edit;
            DrawablePart drawablePart = new(
                e.Surface.Canvas,
                part,
                _viewModel,
                isSelected: _viewModel.SelectedParts.Contains(part),
                isResizable: isResizeable);
            _viewModel.DrawableParts.Add(drawablePart);
            drawablePart.Draw();
        }
    }


    private void ButtonPlayOrPause_Clicked(object sender, EventArgs e)
    {
        if (PlaybackManager.Inst.Playing) // 如果正在播放 => 暂停
        {
            PlaybackManager.Inst.PlayOrPause();
        }
        else // 如果没有播放 => 播放
        {
            PlaybackManager.Inst.StopPlayback();
            PlaybackManager.Inst.PlayOrPause();
        }
    }

    private void ButtonSwitchEditMode_Clicked(object sender, EventArgs e)
    {
        _viewModel.CurrentTrackEditMode = _viewModel.CurrentTrackEditMode == EditViewModel.TrackEditMode.Edit ? EditViewModel.TrackEditMode.Normal : EditViewModel.TrackEditMode.Edit;
        // 重绘走带画布
        TrackCanvas.InvalidateSurface();
    }

    private void PianoRollCanvas_PaintSurface(object sender, SkiaSharp.Views.Maui.SKPaintSurfaceEventArgs e)
    {
        // 清空画布
        e.Surface.Canvas.Clear(SKColors.Transparent);
        if (e.Surface.Canvas.DeviceClipBounds.Height < 5)
        {
            // 提高性能
            return;
        }
        if (_viewModel.SelectedParts.Count == 0)
        {
            return; // 如果没有选中分片，直接返回
        }
        // 设置画布变换
        //e.Surface.Canvas.SetMatrix(_viewModel.PianoRollTransformer.GetTransformMatrix());
        if (_viewModel.SelectedParts[0] is UVoicePart part)
        {
            DrawableNotes drawableNotes = new(canvas: e.Surface.Canvas,
                part: part,
                viewModel: _viewModel,
                notesColor: ViewConstants.TrackSkiaColors[DocManager.Inst.Project.tracks[part.trackNo].TrackColor]);
            drawableNotes.Draw();
            _viewModel.EditingNotes = drawableNotes;
        }
    }

    private void PianoRollCanvas_Touch(object sender, SkiaSharp.Views.Maui.SKTouchEventArgs e)
    {
        _pianoRollGestureProcessor.ProcessTouch(sender, e);
    }


    private void PlaybackPosCanvas_PaintSurface(object sender, SkiaSharp.Views.Maui.SKPaintSurfaceEventArgs e)
    {
        SKCanvas Canvas = e.Surface.Canvas;
        // 清空画布
        Canvas.Clear(SKColors.Transparent);
        // 计算位置
        float x = (float)(_viewModel.PlayPosTick * _viewModel.TrackTransformer.ZoomX + _viewModel.TrackTransformer.PanX);
        // 创建画笔
        using (SKPaint paint = new SKPaint())
        {
            paint.StrokeWidth = 3f; // 设置线条宽度
            paint.Color = SKColor.Parse("#B3F353"); // 设置线条颜色为绿色
            Canvas.DrawLine(x, 0f, x, Canvas.DeviceClipBounds.Height, paint);
        }
    }

    private void PianoKeysCanvas_PaintSurface(object sender, SkiaSharp.Views.Maui.SKPaintSurfaceEventArgs e)
    {
        SKCanvas Canvas = e.Surface.Canvas;
        // 清空画布
        Canvas.Clear();
        float heightPerPianoKey = (float)(_viewModel.HeightPerPianoKey * _viewModel.Density);
        float width = Canvas.DeviceClipBounds.Width;

        float viewTop = -_viewModel.PianoRollTransformer.PanY / _viewModel.PianoRollTransformer.ZoomY;
        float viewBottom = viewTop + Canvas.DeviceClipBounds.Size.Height / _viewModel.PianoRollTransformer.ZoomY;
        int topKeyNum = Math.Max(0, (int)Math.Floor(viewTop / heightPerPianoKey));
        int bottomKeyNum = Math.Min(ViewConstants.TotalPianoKeys, (int)Math.Ceiling(viewBottom / heightPerPianoKey));
        float y = topKeyNum * heightPerPianoKey * _viewModel.PianoRollTransformer.ZoomY + _viewModel.PianoRollTransformer.PanY;
        for (int i = topKeyNum; i < bottomKeyNum; i++)
        {
            _pianoKeysPaint.Color = ViewConstants.PianoKeys[i].IsBlackKey ? ThemeColorsManager.Current.BlackPianoKey : ThemeColorsManager.Current.WhitePianoKey;
            Canvas.DrawRect(0, y, width, heightPerPianoKey * _viewModel.PianoRollTransformer.ZoomY, _pianoKeysPaint);
            y += heightPerPianoKey * _viewModel.PianoRollTransformer.ZoomY;
        }
        // 绘制键名文本
        y = (float)(topKeyNum + 0.5f) * heightPerPianoKey * _viewModel.PianoRollTransformer.ZoomY + _viewModel.PianoRollTransformer.PanY;
        //heightPerPianoKey = heightPerPianoKey * _viewModel.PianoRollTransformer.ZoomY;
        PianoKey? drawingKey = null;
        SKPaint textPaint = new();
        SKFont font = new()
        {
            Size = (float)(heightPerPianoKey * 0.5 * _viewModel.PianoRollTransformer.ZoomY),
            Typeface = ObjectProvider.NotoSansCJKscRegularTypeface
        };
        for (int i = topKeyNum; i < bottomKeyNum; i++)
        {
            drawingKey = ViewConstants.PianoKeys[i];
            int numberedNotationIndex = drawingKey.NoteNum - 60 - DocManager.Inst.Project.key;
            textPaint.Color = drawingKey.IsBlackKey ? ThemeColorsManager.Current.BlackPianoKeyText : ThemeColorsManager.Current.WhitePianoKeyText;
            Canvas.DrawText(drawingKey.NoteName, 5, y, font, textPaint);
            if (numberedNotationIndex >= 0 && numberedNotationIndex <= 11)
            {
                Canvas.DrawText(MusicMath.NumberedNotations[numberedNotationIndex], 50 * (float)_viewModel.Density, y, font, textPaint);
            }
            y += heightPerPianoKey * _viewModel.PianoRollTransformer.ZoomY;
        }
        // 恢复变换矩阵
        //Canvas.SetMatrix(originalMatrix);
    }

    private void PianoKeysCanvas_Touch(object sender, SkiaSharp.Views.Maui.SKTouchEventArgs e)
    {

    }

    private void ButtonZoomIn_Clicked(object sender, EventArgs e)
    {
        _viewModel.TrackTransformer.SetZoomX(_viewModel.TrackTransformer.ZoomX * 1.25f);
        _viewModel.TrackTransformer.SetPanX(_viewModel.TrackTransformer.PanX * 1.25f); // 放大时保持左侧位置不变
    }

    private void ButtonZoomOut_Clicked(object sender, EventArgs e)
    {
        _viewModel.TrackTransformer.SetZoomX(_viewModel.TrackTransformer.ZoomX / 1.25f);
        _viewModel.TrackTransformer.SetPanX(_viewModel.TrackTransformer.PanX / 1.25f); // 缩小时保持左侧位置不变
    }

    private async void ButtonSave_Clicked(object sender, EventArgs e)
    {
        await Save();
    }

    /// <summary>
    /// 保存
    /// </summary>
    /// <returns>是否成功</returns>
    private async Task<bool> Save()
    {
        if (!DocManager.Inst.Project.Saved)
        {
            return await SaveAs(); // 新项目必须另存为
        }
        else
        {
            DocManager.Inst.ExecuteCmd(new SaveProjectNotification(string.Empty)); // 保持当前路径保存
            return true;
        }
    }

    /// <summary>
    /// 另存为
    /// </summary>
    /// <returns>是否成功</returns>
    private async Task<bool> SaveAs()
    {
        string path = await ObjectProvider.SaveFile([".ustx"], this);
        if (!string.IsNullOrEmpty(path))
        {
            DocManager.Inst.ExecuteCmd(new SaveProjectNotification(path));
            return true;
        }
        return false;
    }

    private void ButtonUndo_Clicked(object sender, EventArgs e)
    {
        DocManager.Inst.Undo();
    }

    private void ButtonRedo_Clicked(object sender, EventArgs e)
    {
        DocManager.Inst.Redo();
    }

    private void TimeLineCanvas_PaintSurface(object sender, SkiaSharp.Views.Maui.SKPaintSurfaceEventArgs e)
    {

    }

    private void TimeLineCanvas_Touch(object sender, SkiaSharp.Views.Maui.SKTouchEventArgs e)
    {
        _timeLineGestureProcessor.ProcessTouch(sender, e);
    }

    private void PlaybackTickBackgroundCanvas_PaintSurface(object sender, SkiaSharp.Views.Maui.SKPaintSurfaceEventArgs e)
    {
        // 清空画布
        e.Surface.Canvas.Clear(ThemeColorsManager.Current.TrackBackground);
        // 设置画布变换
        e.Surface.Canvas.SetMatrix(_viewModel.TrackTransformer.GetTransformMatrix());
        // 绘制走带网格背景
        DrawableTickBackground drawableTickBackground = new(e.Surface.Canvas, _viewModel, _viewModel.TrackSnapDiv);
        drawableTickBackground.Draw();
    }

    private void ButtonRemovePart_Clicked(object sender, EventArgs e)
    {
        if (_viewModel.SelectedParts.Count > 0)
        {
            // 删除选中的分片
            _viewModel.RemoveSelectedParts();
        }
    }

    private void ButtonAddTrack_Clicked(object sender, EventArgs e)
    {
        EditViewModel.AddTrack();
    }

    private void PianoRollTickBackgroundCanvas_PaintSurface(object sender, SkiaSharp.Views.Maui.SKPaintSurfaceEventArgs e)
    {
        // 清空画布
        e.Surface.Canvas.Clear(SKColors.Transparent);
        if (e.Surface.Canvas.DeviceClipBounds.Height < 5)
        {
            // 提高性能
            return;
        }
        // 设置画布变换
        e.Surface.Canvas.SetMatrix(_viewModel.PianoRollTransformer.GetTransformMatrix());
        // 绘制钢琴卷帘网格背景
        DrawablePianoRollTickBackground drawablePianoRollTickBackground = new(e.Surface.Canvas, _viewModel);
        drawablePianoRollTickBackground.Draw();
    }

    private static bool IsOpenGLESSupported()
    {
        try
        {
            // 尝试创建一个临时的GL上下文来检测能力
            var testSurface = SKSurface.Create(new SKImageInfo(1, 1));
            testSurface?.Dispose();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void PianoRollKeysBackgroundCanvas_PaintSurface(object sender, SkiaSharp.Views.Maui.SKPaintSurfaceEventArgs e)
    {
        SKCanvas canvas = e.Surface.Canvas;
        canvas.Clear(ThemeColorsManager.Current.WhitePianoRollBackground);
        //canvas.SetMatrix(_viewModel.GetTransformMatrix());
        // 只绘制黑键对应的背景，提高性能
        float heightPerPianoKey = (float)(_viewModel.HeightPerPianoKey * _viewModel.Density);
        float viewTop = -_viewModel.PianoRollTransformer.PanY / _viewModel.PianoRollTransformer.ZoomY;
        float viewBottom = viewTop + canvas.DeviceClipBounds.Size.Height / _viewModel.PianoRollTransformer.ZoomY;
        int topKeyNum = Math.Max(0, (int)Math.Floor(viewTop / _viewModel.HeightPerPianoKey / _viewModel.Density));
        int bottomKeyNum = Math.Min(ViewConstants.TotalPianoKeys, (int)Math.Ceiling(viewBottom / heightPerPianoKey));
        float y = (float)(topKeyNum * heightPerPianoKey) * _viewModel.PianoRollTransformer.ZoomY + _viewModel.PianoRollTransformer.PanY;
        SKPaint paint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = ThemeColorsManager.Current.BlackPianoRollBackground,
        };
        for (int i = topKeyNum; i < bottomKeyNum; i++)
        {
            if (ViewConstants.PianoKeys[i].IsBlackKey)
            {
                canvas.DrawRect(0, y, canvas.DeviceClipBounds.Size.Width, heightPerPianoKey * _viewModel.PianoRollTransformer.ZoomY, paint);
            }
            y += heightPerPianoKey * _viewModel.PianoRollTransformer.ZoomY;
        }

    }

    private async void ButtonRenamePart_Clicked(object sender, EventArgs e)
    {
        // 启动撤销组
        DocManager.Inst.StartUndoGroup();
        // 重命名选中的第一个分片
        if (_viewModel.SelectedParts.Count > 0)
        {
            UPart part = _viewModel.SelectedParts[0];
            Popup popup = new RenamePopup(part.DisplayName, AppResources.RenamePart);
            object? result = await this.ShowPopupAsync(popup);
            if (result != null)
            {
                if (result is string newName)
                {
                    DocManager.Inst.ExecuteCmd(new RenamePartCommand(DocManager.Inst.Project, part, newName));
                }
            }
        }
        // 结束撤销组
        DocManager.Inst.EndUndoGroup();
    }

    private void ButtonMuted_Clicked(object sender, EventArgs e)
    {
        if (sender is Button button && button.BindingContext is UTrack track)
        {
            _viewModel.ToggleTrackMuted(track);
        }
    }

    /// <summary>
    /// 将当前轨道上移一层
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void ButtonMoveUp_Clicked(object sender, EventArgs e)
    {
        if (sender is Button button && button.BindingContext is UTrack track)
        {
            if (_viewModel.MoveTrackUp(track))
            {
                // 移动成功，更新UI
                TrackCanvas.InvalidateSurface();
            }
        }
    }

    /// <summary>
    /// 将当前轨道下移一层
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void ButtonMoveDown_Clicked(object sender, EventArgs e)
    {
        if (sender is Button button && button.BindingContext is UTrack track)
        {
            if (_viewModel.MoveTrackDown(track))
            {
                // 移动成功，更新UI
                TrackCanvas.InvalidateSurface();
            }
        }
    }

    private void ButtonSwitchNoteMode_Clicked(object sender, EventArgs e)
    {
        //if (sender == ButtonSwitchNormolMode)
        //{
        //    _viewModel.CurrentNoteEditMode = EditViewModel.NoteEditMode.Normal;
        //}
        if (sender == ButtonSwitchEditNoteMode)
        {
            _viewModel.CurrentNoteEditMode = EditViewModel.NoteEditMode.EditNote;
        }
        else if (sender == ButtonSwitchEditPitchCurveMode)
        {
            _viewModel.CurrentNoteEditMode = EditViewModel.NoteEditMode.EditPitchCurve;
        }
        else if (sender == ButtonSwitchEditPitchAnchorMode)
        {
            _viewModel.CurrentNoteEditMode = EditViewModel.NoteEditMode.EditPitchAnchor;
        }
        else if (sender == ButtonSwitchEditVibratoMode)
        {
            _viewModel.CurrentNoteEditMode = EditViewModel.NoteEditMode.EditVibrato;
        }
        else
        {
            Debug.WriteLine("未知的按钮");
        }
    }

    private async void ButtonSingerAvatar_Clicked(object sender, EventArgs e)
    {
        if (sender is ImageButton button && button.BindingContext is UTrack track)
        {
            Popup popup = new ChooseSingerPopup(track);
            object? result = await this.ShowPopupAsync(popup);
            if (result is USinger newSinger)
            {
                _viewModel.SetSinger(track, newSinger);
            }
        }
    }

    private void ButtonToggleDetailedTrackHeader_Clicked(object sender, EventArgs e)
    {
        _viewModel.ToggleDetailedTrackHeader();
    }

    private void GestureChangeVolume_PanUpdated(object sender, PanUpdatedEventArgs e)
    {
        if (sender is Slider slider && slider.BindingContext is UTrack track)
        {
            switch (e.StatusType)
            {
                case GestureStatus.Started:
                    // 保存初始音量
                    _viewModel.OriginalVolume = track.Volume;
                    break;
                case GestureStatus.Running:
                    double deltaVolume = e.TotalX / 20; // 每移动20像素，音量变化1.0
                    slider.Value = Math.Clamp(_viewModel.OriginalVolume + deltaVolume, -24, 12);
                    break;
                case GestureStatus.Completed:
                    Debug.WriteLine("Pan completed");
                    _viewModel.RefreshTrack(track);
                    break;
                case GestureStatus.Canceled:
                    break;
            }
        }
    }

    private void GestureResetPan_Tapped(object sender, TappedEventArgs e)
    {
        Debug.WriteLine("重置声像");
        if (sender is Slider slider)
        {
            slider.Value = 0;
        }
    }

    private void GestureChangePan_PanUpdated(object sender, PanUpdatedEventArgs e)
    {
        if (sender is Slider slider && slider.BindingContext is UTrack track)
        {
            switch (e.StatusType)
            {
                case GestureStatus.Started:
                    // 保存初始声像
                    _viewModel.OriginalPan = track.Pan;
                    break;
                case GestureStatus.Running:
                    double deltaPan = e.TotalX; // 每移动100像素，声像变化1.0
                    slider.Value = Math.Clamp(_viewModel.OriginalPan + deltaPan, -100.0, 100.0);
                    break;
                case GestureStatus.Completed:
                    Debug.WriteLine("Pan completed");
                    _viewModel.RefreshTrack(track);
                    break;
                case GestureStatus.Canceled:
                    break;
            }
        }
    }

    private void GestureResetVolume_Tapped(object sender, TappedEventArgs e)
    {
        Debug.WriteLine("重置音量");
        if (sender is Slider slider)
        {
            slider.Value = 0;
        }
    }

    private void ButtonRemoveNote_Clicked(object sender, EventArgs e)
    {
        _viewModel.RemoveNotes();
    }

    /// <summary>
    /// 音高线画布重绘
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void PianoRollPitchCanvas_PaintSurface(object sender, SkiaSharp.Views.Maui.SKPaintSurfaceEventArgs e)
    {        
        SKCanvas canvas = e.Surface.Canvas;
        canvas.Clear();
        if (_viewModel.EditingPart == null || _viewModel.EditingPart is not UVoicePart)
        {
            return; // 如果没有选中分片，直接返回
        }
        if (_viewModel.CurrentNoteEditMode == EditViewModel.NoteEditMode.EditNote)
        {
            return; // 如果当前是编辑音符模式，直接返回
        }
        float pitchDisplayPrecision = Preferences.Default.PitchDisplayPrecision; // 显示精度

        int leftTick = (int)(-_viewModel.PianoRollTransformer.PanX / _viewModel.PianoRollTransformer.ZoomX);
        int rightTick = (int)(canvas.DeviceClipBounds.Size.Width / _viewModel.PianoRollTransformer.ZoomX + leftTick);

        const int interval = 5; // 每5个tick一个点
        foreach (RenderPhrase phrase in _viewModel.EditingPart.renderPhrases) // 遍历所有Phrase
        {
            if (phrase.position > rightTick || phrase.end < leftTick)
            {
                continue;
            }
            int pitchStartTick = phrase.position - phrase.leading;
            int startIdx = Math.Max(0, (leftTick - pitchStartTick) / interval);
            int endIdx = Math.Min(phrase.pitches.Length, (rightTick - pitchStartTick) / interval + 1);
            using SKPath path = new();
            // 计算i步进
            int step = Math.Max(1, (int)(pitchDisplayPrecision / _viewModel.PianoRollTransformer.ZoomX));
            bool isFirstPoint = true;
            for (int i = startIdx; i < endIdx; i += step)
            {
                int t = pitchStartTick + i * interval;
                float p = phrase.pitches[i];
                SKPoint point = _viewModel.PianoRollTransformer.LogicalToActual(_viewModel.PitchAndTickToPoint(t, p));
                if (isFirstPoint)
                {
                    path.MoveTo(point);
                    isFirstPoint = false;
                }
                else
                {
                    path.LineTo(point);
                }
            }
            canvas.DrawPath(path, _pitchLinePaint);
        }
        // 绘制触摸中心
        if (IsUserDrawingCurve)
        {
            SKPaint centerPaint = new()
            {
                Color = SKColors.Yellow,
                StrokeWidth = 2,
                IsAntialias = false,
                Style = SKPaintStyle.Stroke,
            };
            float radius = 10f;
            canvas.DrawCircle(TouchingPoint, radius, centerPaint);
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this); // 防止终结器调用
        Debug.WriteLine("\n\n==============EditPage Dispose===============\n\n");
        PlaybackTimer.Stop();
        DocManager.Inst.RemoveSubscriber(this);
        PlaybackManager.Inst.StopPlayback();
        DeviceDisplay.Current.KeepScreenOn = false; // 解除屏幕常亮
        _disposables.Dispose();
    }

    private void ButtonPianoRollSnapToGrid_Clicked(object sender, EventArgs e)
    {
        if (isPianoRollSnapDivButtonLongPressed)
        {
            // 如果是长按触发的点击事件，忽略此次点击
            isPianoRollSnapDivButtonLongPressed = false;
            return;
        }
        Debug.WriteLine("单击");
        _viewModel.IsPianoRollSnapToGrid = !_viewModel.IsPianoRollSnapToGrid;
        //ButtonPianoRollSnapToGrid.BackgroundColor = _viewModel.IsPianoRollSnapToGrid ? Color.FromArgb("#FF4081") : Color.FromRgba("#FFFFFF");
        //ButtonPianoRollSnapToGrid.Text = _viewModel.IsPianoRollSnapToGrid ? "磁" : "不";
        if (Application.Current?.Resources != null)
        {
            ButtonPianoRollSnapToGrid.ImageSource = _viewModel.IsPianoRollSnapToGrid
                ? (ImageSource)Application.Current.Resources["magnet"]
                : (ImageSource)Application.Current.Resources["magnet-off"];
        }
    }

    private async void TouchBehaviorPianoRollSnapToGrid_LongPressCompleted(object sender, CommunityToolkit.Maui.Core.LongPressCompletedEventArgs e)
    {
        Debug.WriteLine("长按完成");
        // 阻止单击事件
        isPianoRollSnapDivButtonLongPressed = true;
        // 弹出选择菜单
        Popup popup = new PianoRollSnapDivPopup(_viewModel.PianoRollSnapDiv, _viewModel.SnapDivs, AppResources.PianoRollQuantization);
        object? result = await this.ShowPopupAsync(popup);
        if (result is int newSnapDiv)
        {
            _viewModel.PianoRollSnapDiv = newSnapDiv;
            PianoRollTickBackgroundCanvas.InvalidateSurface();
            Debug.WriteLine($"选择了新的量化: {newSnapDiv}");
        }
    }

    private async void TouchBehaviorTrackSnapToGrid_LongPressCompleted(object sender, CommunityToolkit.Maui.Core.LongPressCompletedEventArgs e)
    {
        Debug.WriteLine("长按完成");
        // 阻止单击事件
        isTrackSnapDivButtonLongPressed = true;
        // 弹出选择菜单
        Popup popup = new PianoRollSnapDivPopup(_viewModel.TrackSnapDiv, _viewModel.SnapDivs, "走带量化");
        object? result = await this.ShowPopupAsync(popup);
        if (result is int newSnapDiv)
        {
            _viewModel.TrackSnapDiv = newSnapDiv;
            PlaybackTickBackgroundCanvas.InvalidateSurface();
            Debug.WriteLine($"选择了新的量化: {newSnapDiv}");
        }
    }

    private void ButtonTrackSnapToGrid_Clicked(object sender, EventArgs e)
    {
        if (isTrackSnapDivButtonLongPressed)
        {
            // 如果是长按触发的点击事件，忽略此次点击
            isTrackSnapDivButtonLongPressed = false;
            return;
        }
        Debug.WriteLine("单击");
        _viewModel.IsTrackSnapToGrid = !_viewModel.IsTrackSnapToGrid;
        if (Application.Current?.Resources != null)
        {
            ButtonTrackSnapToGrid.ImageSource = _viewModel.IsTrackSnapToGrid
                ? (ImageSource)Application.Current.Resources["magnet"]
                : (ImageSource)Application.Current.Resources["magnet-off"];
        }
    }

    private async void ButtonMore_Clicked(object sender, EventArgs e)
    {
        Popup popup = new EditMenuPopup();
        object? result = await this.ShowPopupAsync(popup);
        if (result != null && result is string action)
        {
            if (action == "import_audio")
            {
                string path = await ObjectProvider.PickFile([".wav", ".mp3", ".flac", ".ogg"], this);
                if (string.IsNullOrEmpty(path))
                {
                    return;
                }
                _viewModel.ImportAudio(path);
            }
            else if (action == "save_as")
            {
                await SaveAs();
            }
            else if (action == "export_audio")
            {
                string file = await ObjectProvider.SaveFile([".wav"], this);
                if (!string.IsNullOrEmpty(file))
                {
                    Popup exportPopup = new ExportAudioPopup(file);
                    await this.ShowPopupAsync(exportPopup);
                }
            }
            else if (action == "settings")
            {
                await Navigation.PushModalAsync(new SettingsPage());
            }
            else if (action == "import_midi")
            {
                string path = await ObjectProvider.PickFile([".mid", ".midi"], this);
                if (string.IsNullOrEmpty(path))
                {
                    return;
                }
                _viewModel.ImportMidi(path);
            }
            else
            {
                Debug.WriteLine($"未知的操作: {action}");
            }

        }
    }

    private void ButtonRemoveTrack_Clicked(object sender, EventArgs e)
    {
        if (sender is Button button && button.BindingContext is UTrack track)
        {
            _viewModel.RemoveTrack(track);
        }
    }

    private async void ButtonBack_Clicked(object sender, EventArgs e)
    {
        await AttemptExit();
    }

    private async Task AttemptExit()
    {
        if (DocManager.Inst.ChangesSaved)
        { // 如果已经保存，直接关闭
            if (Preferences.Default.ClearCacheOnQuit)
            {
                Log.Information("Clearing cache...");
                PathManager.Inst.ClearCache();
                Log.Information("Cache cleared.");
            }
            await Navigation.PopModalAsync();
            Dispose();
            return;
        }
        if (!await AskIfSaveAndContinue())
        { // 询问是否保存
            return; // 如果‘取消’，则不关闭
        }
        await Navigation.PopModalAsync(); // 不保存，直接退出
    }

    private async Task<bool> AskIfSaveAndContinue()
    {
        Popup popup = new ExitPopup();
        object? result = await this.ShowPopupAsync(popup);
        if (result is string action)
        {
            switch (action)
            {
                case "save":
                    if (await Save())
                    {
                        return true; // 保存成功，继续关闭
                    }
                    else
                    {
                        return false; // 保存失败或取消，取消关闭
                    }
                case "discard":
                    return true; // 不保存，继续关闭
                case "cancel":
                    return false; // 取消关闭
                default:
                    return false; // 取消关闭
            }
        }
        return false;
    }

    public void RefreshProjectInfoDisplay()
    {
        LabelBpm.Text = DocManager.Inst.Project.tempos[0].bpm.ToString("F2");
        LabelBeatUnit.Text = DocManager.Inst.Project.timeSignatures[0].beatUnit.ToString();
        LabelBeatPerBar.Text = DocManager.Inst.Project.timeSignatures[0].beatPerBar.ToString();
        LabelKeyName.Text = $"1 = {MusicMath.KeysInOctave[DocManager.Inst.Project.key].Item1}";
    }

    /// <summary>
    /// 音素画布重绘
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void PhonemeCanvas_PaintSurface(object sender, SkiaSharp.Views.Maui.SKPaintSurfaceEventArgs e)
    {
        SKCanvas canvas = e.Surface.Canvas;
        // 清空画布
        canvas.Clear();
        UVoicePart? part = _viewModel.EditingPart;
        if (part == null)
        {
            return;
        }
        UProject project = DocManager.Inst.Project;

        int leftTick = (int)(-_viewModel.PianoRollTransformer.PanX / _viewModel.PianoRollTransformer.ZoomX);
        int rightTick = (int)(canvas.DeviceClipBounds.Size.Width / _viewModel.PianoRollTransformer.ZoomX + leftTick);

        float y = 25f * (float)_viewModel.Density;
        float height = 20f * (float)_viewModel.Density;
        SKPaint phonemePaint = new()
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2,
            Color = ViewConstants.TrackSkiaColors[DocManager.Inst.Project.tracks[part.trackNo].TrackColor]
        };
        SKPaint posLinePaint = new()
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 3,
            Color = ThemeColorsManager.Current.PhonemePosLine
        };
        SKFont textFont = new(ObjectProvider.NotoSansCJKscRegularTypeface, 12 * (float)_viewModel.Density);
        // 遍历音素
        foreach (var phoneme in part.phonemes)
        {
            double leftBound = project.timeAxis.MsPosToTickPos(phoneme.PositionMs - phoneme.preutter);
            double rightBound = phoneme.End + part.position;
            if (leftBound > rightTick || rightBound < leftTick || phoneme.Parent.OverlapError)
            {
                continue;
            }
            TimeAxis timeAxis = project.timeAxis;
            float x = (float)Math.Round(_viewModel.PianoRollTransformer.LogicalToActual(_viewModel.PitchAndTickToPoint(phoneme.position + part.position, 0)).X);
            double posMs = phoneme.PositionMs;
            if (!phoneme.Error)
            {
                // 预发声起始点 preutter
                float x0 = _viewModel.PianoRollTransformer.LogicalToActual(_viewModel.PitchAndTickToPoint(timeAxis.MsPosToTickPos(posMs + phoneme.envelope.data[0].X), 0)).X;
                float y0 = (1 - phoneme.envelope.data[0].Y / 100) * height;
                // overlap
                float x1 = _viewModel.PianoRollTransformer.LogicalToActual(_viewModel.PitchAndTickToPoint(timeAxis.MsPosToTickPos(posMs + phoneme.envelope.data[1].X), 0)).X;
                float y1 = (1 - phoneme.envelope.data[1].Y / 100) * height;
                //float x2 = _viewModel.PianoRollTransformer.LogicalToActual(_viewModel.PitchAndTickToPoint(timeAxis.MsPosToTickPos(posMs + phoneme.envelope.data[2].X), 0)).X;
                //float y2 = (1 - phoneme.envelope.data[2].Y / 100) * height;
                float x3 = _viewModel.PianoRollTransformer.LogicalToActual(_viewModel.PitchAndTickToPoint(timeAxis.MsPosToTickPos(posMs + phoneme.envelope.data[3].X), 0)).X;
                float y3 = (1 - phoneme.envelope.data[3].Y / 100) * height;
                float x4 = _viewModel.PianoRollTransformer.LogicalToActual(_viewModel.PitchAndTickToPoint(timeAxis.MsPosToTickPos(posMs + phoneme.envelope.data[4].X), 0)).X;
                float y4 = (1 - phoneme.envelope.data[4].Y / 100) * height;

                //var pen = selectedNotes.Contains(phoneme.Parent) ? ThemeManager.AccentPen2 : ThemeManager.AccentPen1;
                //var brush = selectedNotes.Contains(phoneme.Parent) ? ThemeManager.AccentBrush2Semi : ThemeManager.AccentBrush1Semi;

                SKPoint point0 = new(x0, y + y0);
                SKPoint point1 = new(x1, y + y1);
                //SKPoint point2 = new(x2, y + y2);
                SKPoint point3 = new(x3, y + y3);
                SKPoint point4 = new(x4, y + y4);
                SKPath path = new();
                path.MoveTo(point0);
                path.LineTo(point1);
                //path.LineTo(point2);
                path.LineTo(point3);
                path.LineTo(point4);
                path.Close();
                //Debug.WriteLine($"Phoneme {phoneme.phoneme} envelope points: \n0:{point0}, \n1:{point1}, \n2:{point2}, \n3:{point3}, \n4:{point4}");
                //var polyline = new PolylineGeometry(new Point[] { point0, point1, point2, point3, point4 }, true);
                // 音素多边形
                //Debug.WriteLine($"Phoneme {phoneme.phoneme} from {x0} to {x4}");
                canvas.DrawPath(path, phonemePaint);
                //    // preutter控制点
                //    brush = phoneme.preutterDelta.HasValue ? pen!.Brush : ThemeManager.BackgroundBrush;
                //    using (var state = context.PushTransform(Matrix.CreateTranslation(x0, y + y0 - 1)))
                //    {
                //        context.DrawGeometry(brush, pen, pointGeometry);
                //    }
                //    // overlap控制点
                //    brush = phoneme.overlapDelta.HasValue ? pen!.Brush : ThemeManager.BackgroundBrush;
                //    using (var state = context.PushTransform(Matrix.CreateTranslation(point1)))
                //    {
                //        context.DrawGeometry(brush, pen, pointGeometry);
                //    }
                //}
                // 音素position竖线
                canvas.DrawLine(new SKPoint(x, y), new SKPoint(x, y + height), posLinePaint);
                // 音素文本
                string displayPhoneme = phoneme.phonemeMapped ?? phoneme.phoneme;
                canvas.DrawText(displayPhoneme, x + 2, 15 * (float)_viewModel.Density, textFont, ThemeColorsManager.Current.PhonemeTextPaint);
            }
        }
    }

    private void PhonemeCanvas_Touch(object sender, SkiaSharp.Views.Maui.SKTouchEventArgs e)
    {
        _phonemeGestureProcessor.ProcessTouch(sender, e);
    }

    private void ExpressionCanvas_PaintSurface(object sender, SkiaSharp.Views.Maui.SKPaintSurfaceEventArgs e)
    {
        SKCanvas canvas = e.Surface.Canvas;
        canvas.Clear();
        if (e.Surface.Canvas.DeviceClipBounds.Height < 5)
        {
            // 提高性能
            return;
        }
        // 1. 初始化和验证
        // 2. 计算可视区域
        // 3. 根据表情类型分别处理：
        //    - 曲线型 (UExpressionType.Curve)
        //    - 数值型/选项型 (UExpressionType.Numerical/Options)

        // 初始化和验证
        if (_viewModel.EditingPart == null)
        {
            return;
        }
        UProject project = DocManager.Inst.Project;
        UTrack track = project.tracks[_viewModel.EditingPart.trackNo];
        if (_viewModel.PrimaryExpressionAbbr == null)
        {
            return;
        }
        if (!track.TryGetExpDescriptor(project, _viewModel.PrimaryExpressionAbbr, out var descriptor))
        { // 尝试从名称（如DYN）获取描述器
            return;
        }
        if (descriptor.max <= descriptor.min)
        {
            return;
        }
        // 计算视口
        int leftTick = (int)(-_viewModel.PianoRollTransformer.PanX / _viewModel.PianoRollTransformer.ZoomX);
        int rightTick = (int)(canvas.DeviceClipBounds.Size.Width / _viewModel.PianoRollTransformer.ZoomX + leftTick);
        float optionHeight = descriptor.type == UExpressionType.Options
            ? canvas.DeviceClipBounds.Height / descriptor.options.Length
            : 0f;
        SKPaint defaultPaint = new()
        {
            StrokeWidth = 2,
            Color = SKColors.Gray
        };
        SKPaint editedPaint = new()
        {
            StrokeWidth = 4,
            Color = SKColor.Parse("#fe71a3")
        };
        // 曲线型
        if (descriptor.type == UExpressionType.Curve)
        {
            UCurve? curve = _viewModel.EditingPart.curves.FirstOrDefault(c => c.descriptor == descriptor); // 选出对应表情的curve
            float defaultHeight = (float)Math.Round(canvas.DeviceClipBounds.Height - canvas.DeviceClipBounds.Height * (descriptor.defaultValue - descriptor.min) / (descriptor.max - descriptor.min));
            // 如果没有绘制过，就画默认值
            if (curve == null)
            {
                //float x1 = (float)Math.Round(_viewModel.PianoRollTransformer.LogicalToActual(_viewModel.PitchAndTickToPoint(leftTick, 0)).X);
                //float x2 = (float)Math.Round(_viewModel.PianoRollTransformer.LogicalToActual(_viewModel.PitchAndTickToPoint(rightTick, 0)).X);
                //canvas.DrawLine(new SKPoint(x1, defaultHeight), new SKPoint(x2, defaultHeight), defaultPaint);
                canvas.DrawLine(0, defaultHeight, canvas.DeviceClipBounds.Width, defaultHeight, defaultPaint);
                return;
            }
            //int lTick = (int)Math.Floor((double)leftTick / 5) * 5;
            //int rTick = (int)Math.Ceiling((double)rightTick / 5) * 5;
            int lTick = leftTick;
            int rTick = rightTick;
            int index = curve.xs.BinarySearch(lTick - _viewModel.EditingPart.position);
            if (index < 0)
            {
                index = -index - 1;
            }
            index = Math.Max(0, index) - 1;
            // 分段线条绘制
            while (index < curve.xs.Count)
            {
                int tick1 = index < 0 ? lTick : curve.xs[index] + _viewModel.EditingPart.position;
                //tick1 += _viewModel.EditingPart.position;
                float value1 = index < 0 ? descriptor.defaultValue : curve.ys[index];
                float x1 = _viewModel.PianoRollTransformer.LogicalToActual(_viewModel.PitchAndTickToPoint(tick1, 0)).X;
                float y1 = defaultHeight - canvas.DeviceClipBounds.Height * (value1 - descriptor.defaultValue) / (descriptor.max - descriptor.min);
                int tick2 = index == curve.xs.Count - 1 ? rTick : curve.xs[index + 1] + _viewModel.EditingPart.position;
                //tick2 += _viewModel.EditingPart.position;
                float value2 = index == curve.xs.Count - 1 ? descriptor.defaultValue : curve.ys[index + 1];
                float x2 = _viewModel.PianoRollTransformer.LogicalToActual(_viewModel.PitchAndTickToPoint(tick2, 0)).X;
                float y2 = defaultHeight - canvas.DeviceClipBounds.Height * (value2 - descriptor.defaultValue) / (descriptor.max - descriptor.min);
                SKPaint paint = value1 == descriptor.defaultValue && value2 == descriptor.defaultValue ? defaultPaint : editedPaint; // 绘制值用粗线，默认值用细线
                canvas.DrawLine(new SKPoint(x1, y1), new SKPoint(x2, y2), paint);
                //using (var state = canvas.PushTransform(Matrix.CreateTranslation(x1, y1))) {
                //    canvas.DrawGeometry(brush, null, pointGeometry);
                //}
                index++;
                if (tick2 >= rTick)
                {
                    break;
                }
            }
            return;
        }
        //// 遍历音素，包括选项型和数值型
        //foreach (UPhoneme phoneme in _viewModel.EditingPart.phonemes)
        //{
        //    if (phoneme.Error || phoneme.Parent == null)
        //    {
        //        continue;
        //    }
        //    double leftBound = phoneme.position;
        //    double rightBound = phoneme.End;
        //    if (leftBound >= rightTick || rightBound <= leftTick)
        //    {
        //        continue;
        //    }
        //    var note = phoneme.Parent;
        //    var hPen = selectedNotes.Contains(note) ? ThemeManager.AccentPen2Thickness2 : ThemeManager.AccentPen1Thickness2;
        //    var vPen = selectedNotes.Contains(note) ? ThemeManager.AccentPen2Thickness3 : ThemeManager.AccentPen1Thickness3;
        //    var brush = selectedNotes.Contains(note) ? ThemeManager.AccentBrush2 : ThemeManager.AccentBrush1;
        //    var (value, overriden) = phoneme.GetExpression(project, track, Key);
        //    double x1 = Math.Round(viewModel.TickToneToPoint(phoneme.position, 0).X);
        //    double x2 = Math.Round(viewModel.TickToneToPoint(phoneme.End, 0).X);
        //    // 数值型
        //    if (descriptor.type == UExpressionType.Numerical)
        //    {
        //        double valueHeight = Math.Round(Bounds.Height - Bounds.Height * (value - descriptor.min) / (descriptor.max - descriptor.min));
        //        double zeroHeight = Math.Round(Bounds.Height - Bounds.Height * (0f - descriptor.min) / (descriptor.max - descriptor.min));
        //        canvas.DrawLine(vPen, new Point(x1 + 0.5, zeroHeight + 0.5), new Point(x1 + 0.5, valueHeight + 3));
        //        canvas.DrawLine(hPen, new Point(x1 + 3, valueHeight), new Point(Math.Max(x1 + 3, x2 - 3), valueHeight));
        //        using (var state = canvas.PushTransform(Matrix.CreateTranslation(x1 + 0.5, valueHeight)))
        //        {
        //            canvas.DrawGeometry(overriden ? brush : ThemeManager.BackgroundBrush, vPen, pointGeometry);
        //        }
        //        // 选项型
        //    }
        //    else if (descriptor.type == UExpressionType.Options)
        //    {
        //        for (int i = 0; i < descriptor.options.Length; ++i)
        //        {
        //            double y = optionHeight * (descriptor.options.Length - 1 - i + 0.5);
        //            using (var state = canvas.PushTransform(Matrix.CreateTranslation(x1 + 4.5, y)))
        //            {
        //                if ((int)value == i)
        //                {
        //                    if (overriden)
        //                    {
        //                        canvas.DrawGeometry(brush, null, pointGeometry);
        //                    }
        //                    canvas.DrawGeometry(null, hPen, circleGeometry);
        //                }
        //                else
        //                {
        //                    canvas.DrawGeometry(null, ThemeManager.NeutralAccentPenSemi, circleGeometry);
        //                }
        //            }
        //        }
        //    }
        //}
        //// 选项型的背景框和文字
        //if (descriptor.type == UExpressionType.Options)
        //{
        //    for (int i = 0; i < descriptor.options.Length; ++i)
        //    {
        //        string option = descriptor.options[i];
        //        if (string.IsNullOrEmpty(option))
        //        {
        //            option = "\"\"";
        //        }
        //        var textLayout = TextLayoutCache.Get(option, ThemeManager.ForegroundBrush, 12);
        //        double y = optionHeight * (descriptor.options.Length - 1 - i + 0.5) - textLayout.Height * 0.5;
        //        y = Math.Round(y);
        //        var size = new Size(textLayout.Width + 8, textLayout.Height + 2);
        //        using (var state = canvas.PushTransform(Matrix.CreateTranslation(12, y)))
        //        {
        //            canvas.DrawRectangle(
        //                ThemeManager.BackgroundBrush,
        //                ThemeManager.NeutralAccentPenSemi,
        //                new Rect(new Point(-4, -0.5), size), 4, 4);
        //            textLayout.Draw(canvas, new Point());
        //        }
        //    }
        //}
    }

    private void ExpressionCanvas_Touch(object sender, SkiaSharp.Views.Maui.SKTouchEventArgs e)
    {
        _expressionGestureProcessor.ProcessTouch(sender, e);
    }

    private async void ButtonEditBpm_Clicked(object sender, EventArgs e)
    {
        Popup popup = new EditBpmPopup(DocManager.Inst.Project.tempos[0].bpm.ToString());
        object? result = await this.ShowPopupAsync(popup);
        if (result is string bpmStr && double.TryParse(bpmStr, out double newBpm) && newBpm > 0)
        {
            DocManager.Inst.StartUndoGroup();
            DocManager.Inst.ExecuteCmd(new BpmCommand(DocManager.Inst.Project, newBpm));
            DocManager.Inst.EndUndoGroup();
            RefreshProjectInfoDisplay();
        }
    }

    private async void ButtonEditBeat_Clicked(object sender, EventArgs e)
    {
        Popup popup = new EditBeatPopup(DocManager.Inst.Project.timeSignatures[0].beatPerBar, DocManager.Inst.Project.timeSignatures[0].beatUnit);
        object? result = await this.ShowPopupAsync(popup);
        if (result is Tuple<int, int> newTimeSignature)
        {
            DocManager.Inst.StartUndoGroup();
            DocManager.Inst.ExecuteCmd(new TimeSignatureCommand(DocManager.Inst.Project, newTimeSignature.Item1, newTimeSignature.Item2));
            DocManager.Inst.EndUndoGroup();
            RefreshProjectInfoDisplay();
        }
    }

    private async void ButtonEditKey_Clicked(object sender, EventArgs e)
    {
        Popup popup = new EditKeyPopup(DocManager.Inst.Project.key);
        object? result = await this.ShowPopupAsync(popup);
        if (result is int newKey)
        {
            DocManager.Inst.StartUndoGroup();
            DocManager.Inst.ExecuteCmd(new KeyCommand(DocManager.Inst.Project, newKey));
            DocManager.Inst.EndUndoGroup();
        }
    }

    private async void ButtonRenderPitch_Clicked(object sender, EventArgs e)
    {
        bool isContinue = true;
        if (Preferences.Default.WarnOnRenderPitch)
        {
            isContinue = await DisplayAlert(AppResources.LoadPitchRenderingResult, AppResources.LoadPitchRenderingResultPrompt, AppResources.Confirm, AppResources.CancelText);
        }
        if (!isContinue)
        {
            return;
        }
        if (_viewModel.EditingPart != null)
        {
            List<UNote> notes = [];
            if (_viewModel.SelectedNotes.Count > 0)
            {
                notes.AddRange(_viewModel.SelectedNotes);
            }
            else
            {
                notes.AddRange(_viewModel.EditingPart.notes);
            }
            CancellationTokenSource cts = new();
            string workId = notes.GetHashCode().ToString();
            _viewModel.SetWork(WorkType.RenderPitch, workId, 0.5d, string.Empty, cts);
            await Task.Run(() =>
            {
                _viewModel.RenderPitchAsync(_viewModel.EditingPart, notes, (workId, renderedPhrases, totalPhrases) =>
                {
                    UpdateRenderProgress(workId, renderedPhrases, totalPhrases);
                }, cts.Token, workId);
            });
        }
    }

    public void UpdateRenderProgress(string workId, int renderedPhrases, int totalPhrases)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (renderedPhrases >= totalPhrases)
            {
                _viewModel.RemoveWork(workId);
            }
            else
            {
                double progress = totalPhrases > 0 ? (double)renderedPhrases / totalPhrases : 0;
                _viewModel.SetWork(WorkType.RenderPitch, workId, progress, string.Empty, null);
            }
        });
    }

    private void ButtonTryCancelWork_Clicked(object sender, EventArgs e)
    {
        if (sender is Button button && button.BindingContext is RunningWork work)
        {
            _viewModel.TryCancelWork(work.Id);
        }
    }

    private void ButtonExchangeExp_Clicked(object sender, EventArgs e)
    {
        UExpressionDescriptor tmp = _viewModel.PrimaryExpressionDescriptor;
        _viewModel.PrimaryExpressionDescriptor = _viewModel.SecondaryExpressionDescriptor;
        _viewModel.SecondaryExpressionDescriptor = tmp;
        _viewModel.UpdateExpressions();
        ExpressionCanvas.InvalidateSurface();
    }

    private async void ButtonChangeTrackColor_Clicked(object sender, EventArgs e)
    {
        if (sender is Button button && button.BindingContext is UTrack track)
        {
            Popup popup = new ChooseTrackColorPopup();
            object? result = await this.ShowPopupAsync(popup);
            if (result is string colorKey)
            {
                DocManager.Inst.StartUndoGroup();
                DocManager.Inst.ExecuteCmd(new ChangeTrackColorCommand(DocManager.Inst.Project, track, colorKey));
                DocManager.Inst.EndUndoGroup();
            }
        }
    }

    private async void ButtonChangeTrackName_Clicked(object sender, EventArgs e)
    {
        if (sender is Button button && button.BindingContext is UTrack track)
        {
            Popup popup = new RenamePopup(track.TrackName, AppResources.RenameTrack);
            object? result = await this.ShowPopupAsync(popup);
            if (result is string newName && !string.IsNullOrEmpty(newName) && newName != track.TrackName)
            {
                DocManager.Inst.StartUndoGroup();
                DocManager.Inst.ExecuteCmd(new RenameTrackCommand(DocManager.Inst.Project, track, newName));
                DocManager.Inst.EndUndoGroup();
            }
        }
    }

    private void ButtonStop_Clicked(object sender, EventArgs e)
    {
        PlaybackManager.Inst.StopPlayback();
    }

    private async void ButtonChangePhonemizer_Clicked(object sender, EventArgs e)
    {
        if (sender is Button button && button.BindingContext is UTrack track)
        {
            Popup popup = new SelectPhonemizerPopup();
            object? result = await this.ShowPopupAsync(popup);
            if (result is PhonemizerFactory factory)
            {
                try
                {
                    Phonemizer phonemizer = factory.Create();
                    if (track.Phonemizer != null && track.Phonemizer.GetType() == phonemizer.GetType())
                    {
                        return;
                    }
                    DocManager.Inst.StartUndoGroup();
                    DocManager.Inst.ExecuteCmd(new TrackChangePhonemizerCommand(DocManager.Inst.Project, track, phonemizer));
                    DocManager.Inst.EndUndoGroup();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "未能更改音素器");
                    DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(ex));
                }
                finally
                {
                    DocManager.Inst.EndUndoGroup();
                }
            }
        }
    }
}