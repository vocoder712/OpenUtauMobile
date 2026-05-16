using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using DynamicData.Binding;
using IconPacks.Avalonia.PhosphorIcons;
using NAudio.Wave;
using OpenUtau.Core;
using OpenUtau.Core.Format;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using OpenUtauMobile.Controls;
using OpenUtauMobile.Controls.Gestures;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services;
using OpenUtauMobile.Storage;
using OpenUtauMobile.Themes.OpenUtauMobile.Runtime;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;

namespace OpenUtauMobile.ViewModels;

/// <summary>
/// 走带编曲区编辑模式
/// </summary>
public enum TrackEditMode
{
    Normal, // 正常模式
    MultiSelect // 多选模式
}

public enum TrackInputState
{
    Idle, // 空闲
    Panning, // 平移视口
    MovingParts, // 拖动分片
    ResizingParts, // 调整分片长度
}

public class EditorViewModel : NavigateViewModelBase, ICmdSubscriber, IDisposable
{
    // ── 内部状态 ────────────────────────────────────────────────────────
    private readonly Action<UVoicePart, int>? _onRequestEditLyric;

    #region 响应式命令

    /// <summary>
    /// 返回上一页
    /// </summary>
    public ReactiveCommand<Unit, Unit> BackCommand { get; set; }

    /// <summary>
    /// 切换播放/暂停状态
    /// </summary>
    public ReactiveCommand<Unit, Unit> PlayPauseCommand { get; set; }

    /// <summary>
    /// 停止回放
    /// </summary>
    public ReactiveCommand<Unit, Unit> StopCommand { get; set; }

    /// <summary>
    /// 撤销操作
    /// </summary>
    public ReactiveCommand<Unit, Unit> UndoCommand { get; set; }

    /// <summary>
    /// 重做操作
    /// </summary>
    public ReactiveCommand<Unit, Unit> RedoCommand { get; set; }
    /// <summary>
    /// 保存
    /// </summary>
    public ReactiveCommand<Unit, Task<bool>> SaveCommand { get; set; }

    /// <summary>
    /// 显示更多弹窗命令
    /// </summary>
    public ReactiveCommand<Unit, Unit> ShowMorePopupCommand { get; set; }

    /// <summary>
    /// 切换轨道头展开收起状态
    /// </summary>
    public ReactiveCommand<Unit, Unit> ToggleTrackHeaderCommand { get; }

    /// <summary>
    /// 编辑曲速命令
    /// </summary>
    public ReactiveCommand<Unit, Unit> EditBpmCommand { get; set; }

    /// <summary>
    /// 编辑拍号命令
    /// </summary>
    public ReactiveCommand<Unit, Unit> EditTimeSignatureCommand { get; set; }

    /// <summary>
    /// 编辑调号命令
    /// </summary>
    public ReactiveCommand<Unit, Unit> EditKeyCommand { get; set; }

    /// <summary>
    /// 新建轨道命令
    /// </summary>
    public ReactiveCommand<Unit, Unit> AddTrackCommand { get; set; }

    #endregion

    #region 数据源

    [Reactive] public string ProjectPath { get; set; } = string.Empty;
    [Reactive] public UVoicePart? EditingVoicePart { get; set; }
    [Reactive] public UWavePart? EditingWavePart { get; set; }
    [Reactive] public ObservableCollectionExtended<UPart> SelectedParts { get; init; } = [];

    /// <summary>
    /// 走带编曲区编辑模式
    /// </summary>
    [Reactive]
    public TrackEditMode TrackEditMode { get; set; } = TrackEditMode.Normal;

    /// <summary>
    /// 走带编曲区当前编辑模式下的上下文操作列表。
    /// 由 RebuildTrackContextActions() 重新计算后推送给 ContextActionPanel.Actions。
    /// </summary>
    [Reactive]
    public IReadOnlyList<ContextActionItem> TrackContextActions { get; private set; } = [];

    /// <summary>
    /// 走带编曲区轨道头是否展开
    /// </summary>
    [Reactive]
    public bool IsTrackHeaderExpanded { get; set; }

    /// <summary>
    /// 上下文菜单是否展开
    /// </summary>
    [Reactive]
    public bool IsContextMenuExpanded { get; set; } = true;

    /// <summary>
    /// 项目曲速（第一个 Tempo），用于走带区轨道头主键显示。
    /// </summary>
    [Reactive]
    public string ProjectBpm { get; private set; } = "120";

    /// <summary>
    /// 项目拍号（第一个 TimeSignature），用于走带区轨道头主键显示。
    /// </summary>
    [Reactive]
    public string ProjectTimeSignature { get; private set; } = "4/4";

    /// <summary>
    /// 项目音名，用于走带区轨道头主键显示。格式为 "1 = C"。
    /// </summary>
    [Reactive]
    public string ProjectKey { get; private set; } = "1 = C";

    /// <summary>
    /// 当前进度百分比 (0-100)
    /// </summary>
    [Reactive]
    public double ProgressValue { get; set; }

    /// <summary>
    /// 进度消息
    /// </summary>
    [Reactive]
    public string ProgressMessage { get; set; } = string.Empty;
    public bool Saved => !string.IsNullOrEmpty(DocManager.Inst.Project.FilePath) && DocManager.Inst.Project.Saved;

    /// <summary>
    /// 钢琴卷帘视图模型
    /// </summary>
    [Reactive]
    public PianoRollViewModel PianoRollViewModel { get; set; } = new();

    // 视口控制属性
    [Reactive] public double TickWidth { get; set; } = ViewConstants.TickWidthDefault; // 缩放
    [Reactive] public double TrackHeight { get; set; } = 60; // 高度
    [Reactive] public double TickOffset { get; set; } // X 滚动
    [Reactive] public double TrackOffset { get; set; } // Y 滚动

    /// <summary>
    /// 走带编曲区吸附分度，仅作用于 Part 的创建和拖拽。
    /// -1 = 自动（根据 TickWidth 动态推导）；0 = 关闭（自由移动）；正数 = 固定分度。
    /// </summary>
    [Reactive]
    public int SnapDiv { get; set; } = -1;

    #endregion

    private bool WasPanMotionInterrupted { get; set; }

    // 走带编曲区视口尺寸
    private double TrackAreaWidth { get; set; }
    private double TrackAreaHeight { get; set; }

    // 走带编曲画布动态上限缓存
    private bool _maxOffsetsDirty = true;
    private double _cachedMaxTickOffset;

    private double _cachedMaxTrackOffset;

    // 回放定时器
    private DispatcherTimer PlaybackTimer { get; set; }

    // 自动保存定时器
    private DispatcherTimer? _autoSaveTimer;

    // 走带自动翻页状态
    private bool _autoPageActive;
    private double _autoPageTargetTickOffset;
    private const double AutoPageViewportMarginRatio = 0.05; // 边界10%触发翻页
    private const double AutoPageStopEpsilonTicks = 0.5;
    private const double AutoPageLerpSharpness = 10.0;

    private const double AutoPageMaxStepViewportRatio = 0.35;

    // 最近一次来自回放流的等待标记（SetPlayPosTickNotification.waitingRendering）
    private bool _streamWaitingRender;

    private int _stopSeekState;

    // 随机数生成器
    private Random Randomer { get; } = new();

    // 释放资源
    private readonly CompositeDisposable _disposables = [];

    public event Action? RequestInvalidateVisual; // 请求视图重绘事件，供 PianoRollViewModel 调用，通知 PartsCanvas 刷新显示

    public EditorViewModel(MainViewModel navigator, string path = "") : base(navigator)
    {
        DocManager.Inst.AddSubscriber(this); // 订阅事件
        // 命令初始化
        BackCommand = ReactiveCommand.CreateFromTask(OnBackAsync);
        ToggleTrackHeaderCommand = ReactiveCommand.Create(() => { IsTrackHeaderExpanded = !IsTrackHeaderExpanded; });
        UndoCommand = ReactiveCommand.Create(Undo);
        RedoCommand = ReactiveCommand.Create(Redo);
        SaveCommand = ReactiveCommand.Create(Save);
        // 播放/暂停命令
        PlayPauseCommand = ReactiveCommand.Create(() =>
        {
            PlaybackManager.Inst.PlayOrPause(); // 切换播放/暂停状态
            SyncPlaybackStateFromAuthority();
            bool lockStartTime = Convert.ToBoolean(Preferences.Default.LockStartTime); // 是否锁定起始时间
            if (!PlaybackManager.Inst.OutputActive && !PlaybackManager.Inst.StartingToPlay &&
                lockStartTime) // 如果当前没有输出且不是正在启动播放状态，且用户设置了锁定起始时间，则重置播放位置到起始时间
            {
                DocManager.Inst.ExecuteCmd(new SeekPlayPosTickNotification(PlaybackManager.Inst.StartTick, true));
            }

            if (PlaybackManager.Inst.PlayingMaster || PlaybackManager.Inst.StartingToPlay)
            {
                _stopSeekState = 0;
            }
        }).DisposeWith(_disposables);
        // 停止命令
        StopCommand = ReactiveCommand.Create(() =>
        {
            bool wasPlaying = PlaybackManager.Inst.OutputActive || PlaybackManager.Inst.PlayingMaster;
            PlaybackManager.Inst.StopPlayback();
            SyncPlaybackStateFromAuthority();

            int mode = Preferences.Default.StopButtonBehavior;
            if (mode <= 0 || mode > 3) mode = 1;

            if (wasPlaying)
            {
                DocManager.Inst.ExecuteCmd(new SeekPlayPosTickNotification(PlaybackManager.Inst.StartTick));
                _stopSeekState = 1;
            }
            else
            {
                switch (mode)
                {
                    case 2:
                        DocManager.Inst.ExecuteCmd(new SeekPlayPosTickNotification(0));
                        _stopSeekState = 3;
                        break;
                    case 3:
                        DocManager.Inst.ExecuteCmd(new SeekPlayPosTickNotification(0));
                        break;
                    default:
                        if (_stopSeekState <= 0)
                        {
                            int? selectedTick = null;
                            if (SelectedParts is { Count: > 0 } parts)
                            {
                                selectedTick = parts.Min(p => p.position);
                            }

                            if (selectedTick.HasValue)
                            {
                                DocManager.Inst.ExecuteCmd(new SeekPlayPosTickNotification(selectedTick.Value));
                                _stopSeekState = 2;
                            }
                            else
                            {
                                DocManager.Inst.ExecuteCmd(new SeekPlayPosTickNotification(0));
                                _stopSeekState = 3;
                            }
                        }
                        else if (_stopSeekState == 1)
                        {
                            int? selectedTick = null;
                            if (SelectedParts is { Count: > 0 } parts)
                            {
                                selectedTick = parts.Min(p => p.position);
                            }

                            if (selectedTick.HasValue)
                            {
                                DocManager.Inst.ExecuteCmd(new SeekPlayPosTickNotification(selectedTick.Value));
                                _stopSeekState = 2;
                            }
                            else
                            {
                                DocManager.Inst.ExecuteCmd(new SeekPlayPosTickNotification(0));
                                _stopSeekState = 3;
                            }
                        }
                        else
                        {
                            DocManager.Inst.ExecuteCmd(new SeekPlayPosTickNotification(0));
                            _stopSeekState = 3;
                        }
                        break;
                }
            }
        }).DisposeWith(_disposables);
        // 工程信息编辑命令
        EditBpmCommand = ReactiveCommand.CreateFromTask(EditBpmAsync);
        EditTimeSignatureCommand = ReactiveCommand.CreateFromTask(EditTimeSignatureAsync);
        EditKeyCommand = ReactiveCommand.CreateFromTask(EditKeyAsync);
        AddTrackCommand = ReactiveCommand.Create(() =>
        {
            UProject project = DocManager.Inst.Project;
            UTrack newTrack = new UTrack(project) { TrackNo = project.tracks.Count };
            List<TrackPalette.TrackColorInfo> colors = new(TrackPalette.TrackColors);
            if (colors.Count > 0)
            {
                newTrack.TrackColor = colors[Randomer.Next(colors.Count)].Name;
            }

            DocManager.Inst.StartUndoGroup();
            DocManager.Inst.ExecuteCmd(new AddTrackCommand(project, newTrack));
            DocManager.Inst.EndUndoGroup();
        });
        _panMotion.PanDelta = ApplyPanDeltaFromMotion;
        _panMotion.MotionCompleted = interrupted => WasPanMotionInterrupted = interrupted;
        // 打开更多功能弹窗命令
        ShowMorePopupCommand = ReactiveCommand.CreateFromTask(ShowMorePopupAsync);
        // 绑定 EditingVoicePart → PianoRollViewModel.EditingVoicePart，数据单向流动
        this.WhenAnyValue(x => x.EditingVoicePart)
            .Subscribe(part =>
            {
                PianoRollViewModel.EditingVoicePart = part;
                PianoRollViewModel.SelectedNotes.Clear();
                PianoRollViewModel.SyncPlaybackState(PlayPosTick, IsPlaying, IsWaitingRender);
            })
            .DisposeWith(_disposables);

        // 绑定 EditingWavePart → PianoRollViewModel.EditingWavePart，数据单向流动
        this.WhenAnyValue(x => x.EditingWavePart)
            .Subscribe(part =>
            {
                PianoRollViewModel.EditingWavePart = part;
                PianoRollViewModel.SyncPlaybackState(PlayPosTick, IsPlaying, IsWaitingRender);
            })
            .DisposeWith(_disposables);

        // 订阅歌词编辑弹窗请求
        _onRequestEditLyric = (part, noteIndex) => { _ = ShowLyricEditPopupAsync(part, noteIndex); };
        PianoRollViewModel.RequestEditLyric += _onRequestEditLyric;

        PlaybackTimer = new() // 回放定时器，定时通知回放管理器更新播放位置
        {
            Interval = TimeSpan.FromSeconds(1 / Preferences.Default.PlaybackRefreshRate) // 按用户设置的刷新率计算间隔
        };
        PlaybackTimer.Tick += (_, _) => // 定时通知回放管理器更新播放位置
        {
            PlaybackManager.Inst.UpdatePlayPos();
            SyncPlaybackStateFromAuthority();
            UpdateTrackAutoPaging(); // 更新自动翻页状态
        };
        PlaybackTimer.Start(); // 启动定时器
        DocManager.Inst.ExecuteCmd(new SeekPlayPosTickNotification(0)); // 初始回放位置

        // 自动保存定时器
        if (Preferences.Default.AutoSaveEnabled)
        {
            _autoSaveTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(Math.Max(Preferences.Default.AutoSaveInterval, 30))
            };
            _autoSaveTimer.Tick += (_, _) => DocManager.Inst.AutoSave();
            _autoSaveTimer.Start();
        }

        // TrackHeight 变更时，MaxTrackOffset 依赖它，置脏并重新校验。目前设计下轨道高度不变，但预留了修改接口，保持逻辑完整性。
        this.WhenAnyValue(x => x.TrackHeight)
            .Subscribe(_ =>
            {
                InvalidateMaxOffsets();
                ApplyViewportLimits();
            })
            .DisposeWith(_disposables);

        // 编辑模式改变时重建
        this.WhenAnyValue(x => x.TrackEditMode)
            .Subscribe(_ => RebuildTrackContextActions())
            .DisposeWith(_disposables);
        // 选中的分片改变时rebuild上下文菜单
        SelectedParts.ObserveCollectionChanges()
            .Subscribe(_ => RebuildTrackContextActions())
            .DisposeWith(_disposables);

        // 量化统一：SnapDiv 同时驱动钢琴卷帘的吸附分度（初始值也在此同步）
        this.WhenAnyValue(x => x.SnapDiv)
            .Subscribe(div => PianoRollViewModel.PianoRollSnapDiv = div)
            .DisposeWith(_disposables);
        // 上下文菜单展开时触发重建
        this.WhenAnyValue(x => x.IsContextMenuExpanded)
            .Where(expanded => expanded) // 仅展开时触发
            .Subscribe(_ => RebuildTrackContextActions())
            .DisposeWith(_disposables);
        // 异步加载项目
        _ = LoadProjectAsync(path);
    }

    /// <summary>
    /// 异步加载项目，避免阻塞UI线程
    /// </summary>
    /// <param name="path"></param>
    private async Task LoadProjectAsync(string path)
    {
        DocManager.Inst.ExecuteCmd(new ProgressBarNotification(50, L.S("Editor.LoadingProject")));
        await Task.Run(() =>
        {
            if (!string.IsNullOrEmpty(path))
            {
                string[] files = [path];
                Formats.LoadProject(files);
            }
            else // 空白项目
            {
                DocManager.Inst.ExecuteCmd(new LoadProjectNotification(Ustx.Create())); // 新建空项目
            }
        });
        DocManager.Inst.Recovered = false;
        DocManager.Inst.ExecuteCmd(new ProgressBarNotification(100, L.S("Editor.ProjectLoaded")));
    }

    #region 命令处理

    public void OnNext(UCommand cmd, bool isUndo)
    {
        // TODO: 一定要发到UI线程吗？:不需要！docman已经做了
        switch (cmd)
        {
            case SetPlayPosTickNotification setPlayPos:
                // 等待渲染期间冻结回放标记，避免位置在等待点附近抖动漂移。
                if (!setPlayPos.waitingRendering)
                {
                    PlayPosTick = setPlayPos.playPosTick;
                }

                SyncPlaybackStateFromAuthority(setPlayPos.waitingRendering);

                // 播放头移动时仅在与按钮显隐相关的状态变化时重建上下文操作。
                // MaybeRebuildTrackContextActionsForPlayPos();
                break;
            case ProgressBarNotification progressNotif:
                RunOnUiThread(() =>
                {
                    ProgressValue = progressNotif.Progress;
                    ProgressMessage = progressNotif.Info;
                });
                break;
            case LoadProjectNotification loadProjectNotification:
                // 添加最近打开的项目
                Preferences.AddRecentFileIfEnabled(loadProjectNotification.project.FilePath);
                RunOnUiThread(() =>
                {
                    ProjectPath = loadProjectNotification.project.FilePath;
                    RefreshProjectInfo();
                    InvalidateMaxOffsets();
                    PianoRollViewModel.InvalidateMaxOffsets();
                    ApplyViewportLimits();
                    RequestInvalidateVisual?.Invoke();
                    // ResetTrackContextActionPlayPosCaches();
                });
                break;
            case TrackCommand:
                InvalidateMaxOffsets();
                break;
            case PartCommand:
                RunOnUiThread(() =>
                {
                    InvalidateMaxOffsets();
                    ApplyViewportLimits();
                    // 更新钢琴卷帘视口限制
                    PianoRollViewModel.InvalidateMaxOffsets();
                    RequestInvalidateVisual?.Invoke();
                });
                break;
            case ProjectCommand:
                // 曲速/拍号等工程级变更 → 刷新主键显示
                RunOnUiThread(() =>
                {
                    RefreshProjectInfo();
                    // ResetTrackContextActionPlayPosCaches();
                });
                RequestInvalidateVisual?.Invoke();
                break;
            case SaveProjectNotification saveProjectNotification:
                ToastService.Enqueue(L.S("Editor.Saved"));
                ProjectPath = DocManager.Inst.Project.FilePath;
                Preferences.AddRecentFileIfEnabled(saveProjectNotification.Path);
                break;
        }
    }

    private static void RunOnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }

    #endregion

    /// <summary>
    /// 从工程数据刷新 ProjectBpm / ProjectTimeSignature 显示字符串。
    /// </summary>
    private void RefreshProjectInfo()
    {
        UProject project = DocManager.Inst.Project;
        UTempo? tempo = project.tempos.Count > 0 ? project.tempos[0] : null;
        ProjectBpm = tempo != null ? tempo.bpm.ToString("0.##") : "120";
        UTimeSignature? ts = project.timeSignatures.Count > 0 ? project.timeSignatures[0] : null;
        ProjectTimeSignature = ts != null ? $"{ts.beatPerBar}/{ts.beatUnit}" : "4/4";
        int key = project.key;
        ProjectKey = $"1 = {MusicMath.KeysInOctave[key].Item1}";
    }

    #region 弹窗

    private async Task EditBpmAsync()
    {
        ProjectInfoEditViewModel vm = new ProjectInfoEditViewModel(ProjectInfoEditViewModel.EditMode.Bpm, ProjectBpm);
        bool? confirmed = await ShowProjectInfoEditPopupAsync(vm);
        if (confirmed != true) return;
        DocManager.Inst.StartUndoGroup();
        DocManager.Inst.ExecuteCmd(new BpmCommand(DocManager.Inst.Project, vm.ParsedBpm));
        DocManager.Inst.EndUndoGroup();
    }

    private async Task EditTimeSignatureAsync()
    {
        ProjectInfoEditViewModel vm =
            new ProjectInfoEditViewModel(ProjectInfoEditViewModel.EditMode.TimeSignature, ProjectTimeSignature);
        bool? confirmed = await ShowProjectInfoEditPopupAsync(vm);
        if (confirmed != true) return;
        DocManager.Inst.StartUndoGroup();
        DocManager.Inst.ExecuteCmd(new TimeSignatureCommand(DocManager.Inst.Project, vm.ParsedBeatPerBar,
            vm.ParsedBeatUnit));
        DocManager.Inst.EndUndoGroup();
    }

    private async Task EditKeyAsync()
    {
        // 传入当前 key 索引，供 VM 预选高亮（暂用字符串传递，VM 内解析）
        int currentKey = DocManager.Inst.Project.key;
        ProjectInfoEditViewModel vm =
            new ProjectInfoEditViewModel(ProjectInfoEditViewModel.EditMode.Key, currentKey.ToString());
        bool? confirmed = await ShowProjectInfoEditPopupAsync(vm);
        if (confirmed != true) return;
        DocManager.Inst.StartUndoGroup();
        DocManager.Inst.ExecuteCmd(new KeyCommand(DocManager.Inst.Project, vm.SelectedKey));
        DocManager.Inst.EndUndoGroup();
    }

    private static Task<bool?> ShowProjectInfoEditPopupAsync(ProjectInfoEditViewModel vm)
    {
        return Dispatcher.UIThread.InvokeAsync(() =>
            PopupService.Show<bool?>(new ProjectInfoEditPopup(), vm)
        );
    }

    private async Task ShowMorePopupAsync()
    {
        EditorMoreViewModel vm = new();
        EditorMoreAction action = await Dispatcher.UIThread.InvokeAsync(() =>
            PopupService.Show<EditorMoreAction>(new EditorMorePopup(), vm));

        switch (action)
        {
            case EditorMoreAction.ImportAudio:
                _ = ImportAudio();
                break;
            case EditorMoreAction.ImportMidi: // TODO: 合并到导入轨道
                _ = ImportMidi();
                break;
            case EditorMoreAction.ImportTrack:
                // TODO: Handle ImportTrack action
                ToastService.Enqueue(L.S("EditorMore.Toast.ImportTrack"));
                break;
            case EditorMoreAction.ExportAudio:
                _ = ShowExportAudioPopupAsync();
                break;
            case EditorMoreAction.SaveAs:
                _ = RequestSaveAs();
                break;
        }
    }

    private static async Task ImportAudio()
    {
        string file = await FilePicker.PickSingleFileAsync(L.S("FilePicker.ImportAudio"),
            ["*.mp3", "*.wav", "*.flac", "*.aac", "*.ogg", "*.aiff", "*.aif", "*.aifc"]);
        if (file == string.Empty)
        {
            return;
        }

        UProject project = DocManager.Inst.Project;
        UWavePart part = new()
        {
            FilePath = file,
        };
        part.Load(project);
        int trackNo = project.tracks.Count;
        part.trackNo = trackNo;
        DocManager.Inst.StartUndoGroup();
        DocManager.Inst.ExecuteCmd(new AddTrackCommand(project, new UTrack(project) { TrackNo = trackNo }));
        DocManager.Inst.ExecuteCmd(new AddPartCommand(project, part));
        DocManager.Inst.EndUndoGroup();
    }

    private static async Task ImportMidi()
    {
        string file = await FilePicker.PickSingleFileAsync(L.S("FilePicker.ImportMIDI"), ["*.mid", "*.midi"]);
        if (file == string.Empty)
        {
            return;
        }

        UProject project = DocManager.Inst.Project;
        List<UVoicePart> parts = MidiWriter.Load(file, project);
        DocManager.Inst.StartUndoGroup("导入MIDI", true);
        foreach (UVoicePart part in parts)
        {
            UTrack track = new(project)
            {
                TrackNo = project.tracks.Count,
                TrackColor = TrackPalette.TrackColors[new Random().Next(TrackPalette.TrackColors.Count)].Name
            };
            part.trackNo = track.TrackNo;
            if (part.name != "New Part") // 这个逻辑，ennn……
            {
                track.TrackName = part.name;
            }

            part.AfterLoad(project, track);
            DocManager.Inst.ExecuteCmd(new AddTrackCommand(project, track));
            DocManager.Inst.ExecuteCmd(new AddPartCommand(project, part));
        }

        DocManager.Inst.EndUndoGroup();
        // TODO
        ToastService.Enqueue(L.S("Editor.SyncTempoTodo"));
    }

    /// <summary>
    /// 显示歌词编辑弹窗。ViewModel 直接执行命令，弹窗关闭不返回任何信息。
    /// </summary>
    private static async Task<object?> ShowLyricEditPopupAsync(UVoicePart part, int noteIndex)
    {
        LyricEditViewModel vm = new LyricEditViewModel(part, noteIndex);
        try
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
                PopupService.Show<object>(new LyricEditPopup(), vm));
        }
        finally
        {
            // 弹窗关闭后释放 ViewModel 资源
            vm.Dispose();
        }
    }

    #endregion

    #region 视口限制

    /// <summary>
    /// 由 PartsCanvas.OnSizeChanged 推送控件尺寸。
    /// 置脏并重新校验，确保 offset 上限随控件尺寸实时更新。
    /// </summary>
    public void OnTrackAreaSizeChanged(double width, double height)
    {
        TrackAreaWidth = width;
        TrackAreaHeight = height;
        InvalidateMaxOffsets();
        ApplyViewportLimits();
    }

    /// <summary>
    /// 标记动态上限缓存失效。
    /// 应在 Parts/Tracks 内容变化、TickWidth/TrackHeight/画布尺寸变化时调用。
    /// </summary>
    private void InvalidateMaxOffsets() => _maxOffsetsDirty = true;

    /// <summary>
    /// 若缓存有效直接返回（O(1)），否则遍历 Parts 重算并缓存（O(Parts.Count)）。
    /// </summary>
    private void EnsureMaxOffsets()
    {
        if (!_maxOffsetsDirty) return;

        // MaxTickOffset：最远内容结尾（Tick）+ 余量 - 可见宽度（Tick）
        int resolution = DocManager.Inst.Project.resolution;
        int trackCount = DocManager.Inst.Project.tracks.Count;
        double contentEndTick = 0;
        foreach (UPart part in DocManager.Inst.Project.parts)
        {
            double end = part.position + part.Duration;
            if (end > contentEndTick) contentEndTick = end;
        }

        PianoRollViewModel.ContentEndTick = contentEndTick; // 同步钢琴卷帘内容结尾，供其限制使用
        double visibleTicks = TickWidth > 0 ? TrackAreaWidth / TickWidth : 0;
        _cachedMaxTickOffset = Math.Max(0,
            contentEndTick + ViewConstants.SpareQuarterCount * resolution - visibleTicks);

        // MaxTrackOffset：总轨道高度外加Adder高度 - 可见高度
        int totalTracks = trackCount + 1;
        _cachedMaxTrackOffset = Math.Max(0, totalTracks * TrackHeight - TrackAreaHeight);

        _maxOffsetsDirty = false;
    }

    /// <summary>
    /// 将所有视口控制量 Clamp 到合法范围。
    /// ========== 重要！！！ ==========
    /// 所有修改 TickWidth / TrackHeight / TickOffset / TrackOffset 的入口在赋值后都必须调用此方法。
    /// ===============================
    /// 拖拽平移时读缓存（O(1)）；缩放/内容变更时重算（O(Parts.Count)）。
    /// </summary>
    private void ApplyViewportLimits()
    {
        TickWidth = Math.Clamp(TickWidth, ViewConstants.TickWidthMin, ViewConstants.TickWidthMax);
        TrackHeight = Math.Clamp(TrackHeight, ViewConstants.TrackHeightMin, ViewConstants.TrackHeightMax);
        EnsureMaxOffsets(); // 缓存命中则 O(1)，否则重算
        TickOffset = Math.Clamp(TickOffset, 0, _cachedMaxTickOffset);
        TrackOffset = Math.Clamp(TrackOffset, 0, _cachedMaxTrackOffset);
    }

    #endregion

    #region 回放相关

    // 播放状态
    /// <summary>
    /// 是否正在播放（不包含 StartingToPlay 状态）。UI 线程绑定此属性以响应图标状态，确保回放状态变化时图标实时更新。
    /// </summary>
    [Reactive]
    public bool IsPlaying { get; set; }

    /// <summary>
    /// 是否正在等待渲染（StartingToPlay 状态）。UI 线程绑定此属性以响应图标状态，确保回放状态变化时图标实时更新。
    /// </summary>
    [Reactive]
    public bool IsWaitingRender { get; set; }

    [Reactive] public int PlayPosTick { get; set; }

    /// <summary>
    /// 从 PlaybackManager 与播放位置通知同步 IsPlaying / IsWaitingRender。
    /// </summary>
    private void SyncPlaybackStateFromAuthority(bool? streamWaitingRender = null)
    {
        if (streamWaitingRender.HasValue)
        {
            _streamWaitingRender = streamWaitingRender.Value;
        }

        PlaybackManager playback = PlaybackManager.Inst;
        bool transportRunning = playback.PlayingMaster &&
                                playback.AudioOutput.PlaybackState == PlaybackState.Playing;
        if (!transportRunning)
        {
            _streamWaitingRender = false;
        }

        bool waiting = playback.StartingToPlay || (transportRunning && _streamWaitingRender);
        IsWaitingRender = waiting;
        IsPlaying = transportRunning && !waiting;

        // 仅在播放或等待渲染时启动定时器
        if (IsPlaying || IsWaitingRender)
        {
            if (!PlaybackTimer.IsEnabled)
            {
                PlaybackTimer.Start();
            }
        }
        else
        {
            if (PlaybackTimer.IsEnabled)
            {
                PlaybackTimer.Stop();
            }

            _autoPageActive = false;
        }

        PianoRollViewModel.SyncPlaybackState(PlayPosTick, IsPlaying, IsWaitingRender);
    }

    private void UpdateTrackAutoPaging()
    {
        if (!IsTrackAutoPagingEnabled() || !IsPlaying || TickWidth <= 0 || TrackAreaWidth <= 0)
        {
            _autoPageActive = false;
            return;
        }

        if (_inputState != TrackInputState.Idle)
        {
            return;
        }

        double visibleTicks = TrackAreaWidth / TickWidth; // 可见tick数
        if (visibleTicks <= 0)
        {
            _autoPageActive = false;
            return;
        }

        double leftSafe = TickOffset + visibleTicks * AutoPageViewportMarginRatio;
        double rightSafe = TickOffset + visibleTicks * (1 - AutoPageViewportMarginRatio);
        if (PlayPosTick < leftSafe || PlayPosTick > rightSafe)
        {
            _autoPageTargetTickOffset = PlayPosTick - visibleTicks * AutoPageViewportMarginRatio;
            _autoPageActive = true;
        }

        if (!_autoPageActive)
        {
            return;
        }

        double target = _autoPageTargetTickOffset;
        double delta = target - TickOffset;
        if (Math.Abs(delta) <= AutoPageStopEpsilonTicks)
        {
            TickOffset = target;
            ApplyViewportLimits();
            _autoPageActive = false;
            return;
        }

        // 检查是否使用硬翻页（配置为1）
        if (Preferences.Default.PlaybackAutoScroll == 1)
        {
            // 硬翻页：直接跳转到目标位置
            TickOffset = target;
            ApplyViewportLimits();
            _autoPageActive = false;
        }
        else
        {
            // 平滑翻页：使用原有的插值动画
            double dt = PlaybackTimer.Interval.TotalSeconds;
            double alpha = 1 - Math.Exp(-AutoPageLerpSharpness * dt);
            double step = delta * alpha;
            double maxStep = visibleTicks * AutoPageMaxStepViewportRatio;
            if (Math.Abs(step) > maxStep)
            {
                step = Math.Sign(step) * maxStep;
            }

            TickOffset += step;
            ApplyViewportLimits();

            if (Math.Abs(_autoPageTargetTickOffset - TickOffset) <= AutoPageStopEpsilonTicks)
            {
                _autoPageActive = false;
            }
        }
    }

    /// <summary>
    /// 是否启用自动翻页
    /// </summary>
    /// <returns></returns>
    private static bool IsTrackAutoPagingEnabled()
    {
        // PlaybackAutoScroll: 0 = off, non-zero = on.
        return Preferences.Default.PlaybackAutoScroll != 0;
    }

    #endregion

    #region 坐标转换

    public double CanvasXToTick(double x) => (x / TickWidth) + TickOffset;
    public int CanvasYToTrackNo(double y) => (int)((y + TrackOffset) / TrackHeight);
    public double TickToCanvasX(double tick) => (tick - TickOffset) * TickWidth;
    public double TrackNoToCanvasY(int trackNo) => trackNo * TrackHeight - TrackOffset;

    /// <summary>
    /// 将任意 tick 位置吸附到最近的网格点。
    /// SnapDiv == 0：关闭，直接返回原始 tick。
    /// SnapDiv == -1：自动，由 MusicMath.GetSnapUnit 根据当前 TickWidth 推导分度。
    /// SnapDiv > 0：固定分度。
    /// </summary>
    public int SnapToRound(int tick)
    {
        int snapUnit = ResolveSnapUnit();
        if (snapUnit <= 0) return tick;
        return (int)(Math.Round((double)tick / snapUnit) * snapUnit);
    }

    /// <summary>
    /// 将任意 tick 位置向下（左）吸附到最近的网格点。
    /// 用于创建 Part 时确定起始位置。同 SnapTick 的分度规则。
    /// </summary>
    public int SnapTickFloor(int tick)
    {
        int snapUnit = ResolveSnapUnit();
        if (snapUnit <= 0) return tick;
        return (int)(Math.Floor((double)tick / snapUnit) * snapUnit);
    }

    /// <summary>
    /// 根据 SnapDiv 计算实际吸附单元（Tick 数）。
    /// 返回 0 表示关闭吸附（SnapDiv == 0）。
    /// </summary>
    private int ResolveSnapUnit()
    {
        if (SnapDiv == 0) return 0;
        int resolution = DocManager.Inst.Project.resolution;
        if (SnapDiv < 0)
        {
            // 自动：以像素宽度反推最合适的分度
            double minTicks = TickWidth > 0 ? ViewConstants.TrackMinTicklineWidth / TickWidth : resolution;
            MusicMath.GetSnapUnit(resolution, minTicks, triplet: false, out int ticks, out _);
            return ticks;
        }

        return resolution * 4 / SnapDiv;
    }

    #endregion

    #region 命中测试

    public UPart? HitTestPart(Point canvasPoint)
    {
        double tick = CanvasXToTick(canvasPoint.X);
        int trackNo = CanvasYToTrackNo(canvasPoint.Y);
        foreach (UPart part in DocManager.Inst.Project.parts)
        {
            if (part.trackNo == trackNo && tick >= part.position && tick < part.position + part.Duration)
                return part;
        }

        return null;
    }

    /// <summary>
    /// 对选中分片的右侧调整手柄区域进行命中测试。
    /// 命中宽度大于视觉宽度，保证移动端触摸友好。
    /// </summary>
    public UPart? HitTestResizeHandle(Point canvasPoint)
    {
        foreach (UPart part in SelectedParts)
        {
            double partRight = TickToCanvasX(part.position + part.Duration);
            double partTop = TrackNoToCanvasY(part.trackNo);
            double partBottom = partTop + TrackHeight;
            double hitLeft = partRight - ViewConstants.ResizeHandleHitWidth;
            if (canvasPoint.X >= hitLeft && canvasPoint.X <= partRight &&
                canvasPoint.Y >= partTop && canvasPoint.Y <= partBottom)
            {
                return part;
            }
        }

        return null;
    }

    #endregion

    #region 手势响应 (由 PartsCanvas 视图层调用)

    private TrackInputState _inputState = TrackInputState.Idle; // 输入状态机
    private readonly ViewportMotionController _panMotion = new();
    private (int position, int trackNo)[] _movingPartOrigins = []; // 拖动开始时各选中分片的初始位置（position, trackNo），用于从绝对偏移计算目标位置
    private int[] _resizingPartOriginDurations = []; // 调整时长开始时各选中分片的初始时长（Duration），用于从绝对偏移计算目标时长
    private int _resizingReferencePartIndex; // 触发 resize 的手柄所属分片在 SelectedParts 中的索引，用作吸附基准

    /// <summary>
    /// 单击
    /// </summary>
    /// <param name="point"></param>
    public void OnGestureTap(Point point)
    {
        InterruptPanMotionIfRunning();
        UPart? hitTestPart = HitTestPart(point);
        switch (TrackEditMode)
        {
            case TrackEditMode.Normal:
                // 移动播放标记
                if (!IsPlaying)
                {
                    int tick = SnapToRound((int)CanvasXToTick(point.X));
                    DocManager.Inst.ExecuteCmd(new SeekPlayPosTickNotification(tick));
                }

                // 单选
                SelectedParts.Clear();
                if (hitTestPart != null)
                {
                    SelectedParts.Add(hitTestPart);
                    if (hitTestPart is UVoicePart voicePart)
                    {
                        EditingVoicePart = voicePart;
                        EditingWavePart = null;
                    }
                    else if (hitTestPart is UWavePart wavePart)
                    {
                        EditingWavePart = wavePart;
                        EditingVoicePart = null;
                    }
                }
                else
                {
                    EditingVoicePart = null;
                    EditingWavePart = null;
                }

                break;
            case TrackEditMode.MultiSelect:
                if (hitTestPart != null)
                {
                    // 反选
                    if (!SelectedParts.Remove(hitTestPart))
                        SelectedParts.Add(hitTestPart);
                }
                else
                {
                    SelectedParts.Clear();
                }

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
        UPart? hit = HitTestPart(point);
        switch (TrackEditMode)
        {
            case TrackEditMode.Normal:
                // 双击分片：进入编辑
                if (hit != null)
                {
                }
                // 双击空白：退出编辑，清空选择，创建新分片
                else
                {
                    EditingVoicePart = null;
                    EditingWavePart = null;
                    SelectedParts.Clear();
                    int trackNo = CanvasYToTrackNo(point.Y);
                    if (trackNo >= 0 && trackNo < DocManager.Inst.Project.tracks.Count)
                    {
                        // 创建4小节的新分片
                        UVoicePart newPart = new()
                        {
                            position = SnapToRound((int)CanvasXToTick(point.X)),
                            trackNo = trackNo,
                            Duration = DocManager.Inst.Project.resolution * 4 * 4,
                            name = L.S("Editor.NewPart")
                        };
                        DocManager.Inst.StartUndoGroup();
                        DocManager.Inst.ExecuteCmd(new AddPartCommand(DocManager.Inst.Project, newPart));
                        DocManager.Inst.EndUndoGroup();
                        SelectedParts.Add(newPart);
                        EditingVoicePart = newPart;
                    }
                }

                break;
            case TrackEditMode.MultiSelect:
                break;
        }
    }

    /// <summary>
    /// 开始拖动
    /// </summary>
    /// <param name="point"></param>
    public void OnGestureDragBegin(Point point)
    {
        // 新手势进入，先打断惯性滑动。
        InterruptPanMotionIfRunning();
        if (WasPanMotionInterrupted)
        {
            WasPanMotionInterrupted = false;
        }

        // 优先检测调整时长手柄（手柄区域在视觉上与 Part 矩形重叠，须先于 MovingParts 判断）
        UPart? resizeHit = SelectedParts.Count > 0 ? HitTestResizeHandle(point) : null;
        if (resizeHit != null)
        {
            _inputState = TrackInputState.ResizingParts;
            _resizingReferencePartIndex = SelectedParts.IndexOf(resizeHit);
            _resizingPartOriginDurations = new int[SelectedParts.Count];
            for (int i = 0; i < SelectedParts.Count; i++)
                _resizingPartOriginDurations[i] = SelectedParts[i].Duration;
            DocManager.Inst.StartUndoGroup(deferValidate: true);
            return;
        }

        UPart? hit = HitTestPart(point);
        // 无论哪种模式，在选中的分片上开始拖动都进入拖动状态。
        if (hit != null && SelectedParts.Contains(hit))
        {
            _inputState = TrackInputState.MovingParts;
            // 记录所有选中分片的初始位置
            UPart[] sortedParts =
                SelectedParts.OrderBy(p => p.position).ThenBy(p => p.trackNo)
                    .ToArray(); // 先按 position 排序，position 相同则按 trackNo 排序，确保移动时分片顺序稳定
            SelectedParts.Clear();
            SelectedParts.AddRange(sortedParts);
            _movingPartOrigins = new (int, int)[SelectedParts.Count];
            for (int i = 0; i < SelectedParts.Count; i++)
            {
                _movingPartOrigins[i] = (SelectedParts[i].position, SelectedParts[i].trackNo);
            }

            DocManager.Inst.StartUndoGroup(deferValidate: true);
        }
        else
        {
            switch (TrackEditMode)
            {
                case TrackEditMode.Normal:
                    _inputState = TrackInputState.Panning; // 进入平移状态
                    break;
                case TrackEditMode.MultiSelect when hit == null:
                    // TODO: 在多选模式下空白处拖拽进入框选状态
                    break;
            }
        }
    }

    /// <summary>
    /// 拖拽更新
    /// </summary>
    /// <param name="point"></param>
    /// <param name="delta"></param>
    /// <param name="totalOffset"></param>
    /// <param name="timestamp"></param>
    public void OnGestureDragUpdate(Point point, Vector delta, Vector totalOffset, ulong timestamp)
    {
        switch (_inputState)
        {
            // 移动分片状态
            case TrackInputState.MovingParts:
                int tickTotal = (int)(totalOffset.X / TickWidth);
                int trackTotal = (int)(totalOffset.Y / TrackHeight);
                // 将第一个分片的开头吸附对齐
                int newPos0 = _movingPartOrigins[0].position + tickTotal;
                newPos0 = SnapToRound(newPos0);
                tickTotal = newPos0 - _movingPartOrigins[0].position;
                int trackMax = DocManager.Inst.Project.tracks.Count - 1;
                // 第一轮遍历：预计算所有目标位置，任一越界则放弃本次移动
                (int pos, int track)[] targets = new (int pos, int track)[SelectedParts.Count];
                for (int i = 0; i < SelectedParts.Count; i++)
                {
                    (int originPos, int originTrack) = _movingPartOrigins[i];
                    int newPos = originPos + tickTotal;
                    int newTrack = originTrack + trackTotal;
                    if (newTrack < 0 || newTrack > trackMax || newPos < 0 || (newPos == SelectedParts[i].position &&
                                                                              newTrack == SelectedParts[i].trackNo))
                    {
                        return;
                    }

                    targets[i] = (newPos, newTrack);
                }

                // 第二轮遍历：执行移动
                for (int i = 0; i < SelectedParts.Count; i++)
                {
                    DocManager.Inst.ExecuteCmd(new MovePartCommand(
                        DocManager.Inst.Project, SelectedParts[i], targets[i].pos, targets[i].track));
                }

                // O(SelectedParts.Count) 线性时间复杂度
                break;
            case TrackInputState.ResizingParts:
                UpdateResizePart(totalOffset);
                break;
            case TrackInputState.Panning:
                if (!_panMotion.IsRunning)
                {
                    _panMotion.BeginDirectManipulation(timestamp);
                }

                _panMotion.UpdateDirectManipulation(delta, timestamp);
                break;
        }
    }

    private void UpdateResizePart(Vector totalOffset)
    {
        if (_resizingPartOriginDurations.Length == 0) return;
        int tickTotal = (int)(totalOffset.X / TickWidth);
        // 以触发手柄的分片的原始结束 tick 为基准吸附
        UPart referencePart = SelectedParts[_resizingReferencePartIndex];
        int rawEndTick = referencePart.position + _resizingPartOriginDurations[_resizingReferencePartIndex] + tickTotal;
        int snappedEnd = SnapToRound(rawEndTick);
        // Δ：吸附后的目标时长与 anchor 当前时长之差，所有分片共用
        int deltaDur = snappedEnd - (referencePart.position + referencePart.Duration);
        if (deltaDur == 0) return;
        int snapUnit = ResolveSnapUnit();
        int minDuration = snapUnit > 0 ? snapUnit : 60;
        // 第一轮：验证所有分片，任一过短则整体放弃
        foreach (UPart part in SelectedParts)
        {
            if (part.Duration + deltaDur < minDuration) return;
        }

        // 第二轮：对所有分片执行相同的 Δ
        foreach (UPart part in SelectedParts)
        {
            switch (part)
            {
                case UVoicePart voicePart:
                    DocManager.Inst.ExecuteCmd(
                        new ResizeVoicePartCommand(DocManager.Inst.Project, voicePart, deltaDur, fromStart: false));
                    break;
                case UWavePart wavePart:
                    DocManager.Inst.ExecuteCmd(
                        new ResizeWavePartCommand(DocManager.Inst.Project, wavePart, deltaDur, fromStart: false));
                    break;
            }
        }
    }

    /// <summary>
    /// 拖拽结束
    /// </summary>
    /// <param name="point"></param>
    /// <param name="timestamp"></param>
    public void OnGestureDragEnd(Point point, ulong timestamp)
    {
        if (_inputState == TrackInputState.MovingParts || _inputState == TrackInputState.ResizingParts)
        {
            DocManager.Inst.EndUndoGroup();
        }
        else if (_inputState == TrackInputState.Panning)
        {
            _panMotion.EndDirectManipulation(timestamp);
        }

        _inputState = TrackInputState.Idle;
        _movingPartOrigins = [];
        _resizingPartOriginDurations = [];
        _resizingReferencePartIndex = 0;
    }

    /// <summary>
    /// 缩放更新
    /// 仅X轴
    /// </summary>
    /// <param name="scaleX"></param>
    /// <param name="scaleY"></param>
    /// <param name="center"></param>
    /// <param name="panDelta"></param>
    public void OnGesturePinchUpdate(double scaleX, double scaleY, Point center, Vector panDelta)
    {
        InterruptPanMotionIfRunning();

        double centerTick = CanvasXToTick(center.X);
        TickWidth *= scaleX;
        TickOffset = centerTick - center.X / TickWidth;
        TickOffset -= panDelta.X / TickWidth;
        InvalidateMaxOffsets(); // 更新视口限制缓存
        ApplyViewportLimits(); // 施加视口限制
    }

    /// <summary>
    /// 双指点击
    /// </summary>
    public void OnTwoFingerTap()
    {
        InterruptPanMotionIfRunning();
        Undo();
    }

    /// <summary>
    /// 三指点击
    /// </summary>
    public void OnThreeFingerTap()
    {
        InterruptPanMotionIfRunning();
        Redo();
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
        double prevTrackOffset = TrackOffset;

        TickOffset -= delta.X / TickWidth;
        TrackOffset -= delta.Y;
        ApplyViewportLimits();
        RequestInvalidateVisual?.Invoke();

        if (_panMotion.IsInertiaRunning &&
            Math.Abs(TickOffset - prevTickOffset) < 1e-6 &&
            Math.Abs(TrackOffset - prevTrackOffset) < 1e-6)
        {
            _panMotion.Stop();
        }
    }

    #endregion

    #region 其他事件响应

    /// <summary>
    /// 撤销操作
    /// </summary>
    private void Undo()
    {
        DocManager.Inst.Undo();
        ToastService.Enqueue(L.S("Editor.Undone"));
    }

    /// <summary>
    /// 重做操作
    /// </summary>
    private void Redo()
    {
        DocManager.Inst.Redo();
        ToastService.Enqueue(L.S("Editor.Redone"));
    }

    /// <summary>
    /// 实际执行保存
    /// </summary>
    /// <param name="fileName">置空表示保存到原位</param>
    private static void SaveAs(string fileName)
    {
        DocManager.Inst.ExecuteCmd(new SaveProjectNotification(fileName));
    }
    /// <summary>
    /// 判断直接保存还是另存为
    /// </summary>
    private async Task<bool> Save()
    {
        if (!Saved) return await RequestSaveAs();
        SaveAs(string.Empty);
        return true;
    }
    /// <summary>
    /// 打开弹窗获取保存filename
    /// </summary>
    /// <returns>是否成功保存</returns>
    private static async Task<bool> RequestSaveAs()
    {
        FilePickerPopup view = new();
        // TODO: 使用统一文件入口
        FileSavePickerViewModel vm = new(
            L.S("FilePicker.SaveProjectAs"),
            "ustx",
            Preferences.Default.LastSaveProjectDirectory,
            L.S("FilePicker.DefaultProjectName"));
        string? fileName = await PopupService.Show<string>(view, vm);
        if (string.IsNullOrEmpty(fileName))
        {
            return false;
        }
        SaveAs(fileName);
        Preferences.Default.LastSaveProjectDirectory = Path.GetDirectoryName(fileName) ?? Preferences.Default.LastSaveProjectDirectory;
        Preferences.Save();
        return true;
    }

    private static async Task<byte> ShowExitEditorConfirmPopupAsync()
    {
        ExitEditorConfirmViewModel vm = new();
        byte register = await PopupService.Show<byte>(new ExitEditorConfirmPopup(), vm);
        return (byte)(register & 0b11);
    }

    private async Task ShowExportAudioPopupAsync()
    {
        string projectFilePath = DocManager.Inst.Project.FilePath;
        string defaultFileName = string.IsNullOrWhiteSpace(projectFilePath)
            ? "export"
            : Path.GetFileNameWithoutExtension(projectFilePath);

        string initialDirectory = Preferences.Default.LastSaveProjectDirectory;
        if (string.IsNullOrWhiteSpace(initialDirectory) && !string.IsNullOrWhiteSpace(projectFilePath))
        {
            initialDirectory = Path.GetDirectoryName(projectFilePath) ?? string.Empty;
        }

        ExportAudioPopupViewModel vm = new(defaultFileName, initialDirectory, ExecuteAudioExportAsync);
        await PopupService.Show<object?>(new ExportAudioPopup(), vm);
    }

    private async Task<bool> ExecuteAudioExportAsync(ExportAudioRequest request)
    {
        try
        {
            DateTime startedAt = DateTime.UtcNow;
            bool success;
            switch (request.Mode)
            {
                case ExportAudioMode.Mixdown:
                    await PlaybackManager.Inst.RenderMixdown(DocManager.Inst.Project, request.ExportPath);
                    success = File.Exists(request.ExportPath) &&
                              File.GetLastWriteTimeUtc(request.ExportPath) >= startedAt.AddSeconds(-1);
                    ToastService.Enqueue(L.S(success
                        ? "ExportAudioPopup.Toast.Success.Mixdown"
                        : "ExportAudioPopup.Toast.Failed"));
                    break;
                case ExportAudioMode.Tracks:
                    await PlaybackManager.Inst.RenderToFiles(DocManager.Inst.Project, request.ExportPath);
                    string? trackDir = Path.GetDirectoryName(request.ExportPath);
                    string trackBase = Path.GetFileNameWithoutExtension(request.ExportPath);
                    success = !string.IsNullOrWhiteSpace(trackDir) &&
                              Directory.Exists(trackDir) &&
                              Directory.GetFiles(trackDir, $"{trackBase}_*.wav")
                                  .Any(file => File.GetLastWriteTimeUtc(file) >= startedAt.AddSeconds(-1));
                    ToastService.Enqueue(L.S(success
                        ? "ExportAudioPopup.Toast.Success.Tracks"
                        : "ExportAudioPopup.Toast.Failed"));
                    break;
                default:
                    success = false;
                    ToastService.Enqueue(L.S("ExportAudioPopup.Toast.Failed"));
                    break;
            }

            string? directory = Path.GetDirectoryName(request.ExportPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Preferences.Default.LastSaveProjectDirectory = directory;
                Preferences.Save();
            }

            return success;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to export audio. mode={Mode}, path={Path}", request.Mode, request.ExportPath);
            ToastService.Enqueue(L.S("ExportAudioPopup.Toast.Failed"));
            return false;
        }
    }

    #endregion

    #region 上下文操作面板

    private void RebuildTrackContextActions()
    {
        // 性能优化：收起状态下不构建菜单
        if (!IsContextMenuExpanded)
        {
            return;
        }

        List<ContextActionItem> items = [];
        bool hasSelection = SelectedParts.Count > 0;

        switch (TrackEditMode)
        {
            case TrackEditMode.Normal:
                if (hasSelection)
                {
                    items.Add(new ContextActionItem
                    {
                        Icon = PackIconPhosphorIconsKind.Trash,
                        Tip = L.S("Common.Delete"),
                        IsDanger = true,
                        Command = ReactiveCommand.Create(DeleteSelectedParts)
                    });
                    items.Add(new ContextActionItem
                    {
                        Icon = PackIconPhosphorIconsKind.Textbox,
                        Tip = L.S("Editor.Action.Rename"),
                        Command = ReactiveCommand.Create(RenameSelectedPart)
                    });
                    items.Add(new ContextActionItem
                    {
                        Icon = PackIconPhosphorIconsKind.Copy,
                        Tip = L.S("Common.Copy"),
                        Command = ReactiveCommand.Create(CopySelectedParts)
                    });
                    items.Add(new ContextActionItem
                    {
                        Icon = PackIconPhosphorIconsKind.Scissors,
                        Tip = L.S("Common.Cut"),
                        Command = ReactiveCommand.Create(CutSelectedParts)
                    });
                }

                items.Add(new ContextActionItem
                {
                    Icon = PackIconPhosphorIconsKind.Clipboard,
                    Tip = L.S("Common.Paste"),
                    Command = ReactiveCommand.Create(PasteParts)
                });
                if (hasSelection)
                {
                    items.Add(new ContextActionItem
                    {
                        Icon = PackIconPhosphorIconsKind.SplitHorizontal,
                        Tip = L.S("Editor.Action.Split"),
                        Command = ReactiveCommand.Create(SplitSelectedParts)
                    });
                }

                // items.Add(new ContextActionItem
                // {
                //     Label = "♩+",
                //     Tip = L.S("Editor.Action.AddTimeSig"),
                //     Command = ReactiveCommand.Create(AddTimeSignatureMarker)
                // });
                // items.Add(new ContextActionItem
                // {
                //     Label = "♩=",
                //     Tip = L.S("Editor.Action.AddTempo"),
                //     Command = ReactiveCommand.Create(AddTempoMarker)
                // });
                // items.Add(new ContextActionItem
                // {
                //     Label = "⏮♩",
                //     Tip = L.S("Editor.Action.PrevTimeSig"),
                //     Command = ReactiveCommand.Create(GotoPrevTimeSignature)
                // });
                // items.Add(new ContextActionItem
                // {
                //     Label = "⏭♩",
                //     Tip = L.S("Editor.Action.NextTimeSig"),
                //     Command = ReactiveCommand.Create(GotoNextTimeSignature)
                // });
                // items.Add(new ContextActionItem
                // {
                //     Label = "⏮=",
                //     Tip = L.S("Editor.Action.PrevTempo"),
                //     Command = ReactiveCommand.Create(GotoPrevTempo)
                // });
                // items.Add(new ContextActionItem
                // {
                //     Label = "⏭=",
                //     Tip = L.S("Editor.Action.NextTempo"),
                //     Command = ReactiveCommand.Create(GotoNextTempo)
                // });
                // if (HasTimeSignatureAtPrevBar())
                // {
                //     items.Add(new ContextActionItem
                //     {
                //         Label = "♩×",
                //         Tip = L.S("Editor.Action.DeleteTimeSig"),
                //         IsDanger = true,
                //         Command = ReactiveCommand.Create(DeleteTimeSignatureAtPrevBar)
                //     });
                // }
                //
                // if (HasTempoAtPlayPos())
                // {
                //     items.Add(new ContextActionItem
                //     {
                //         Label = "=×",
                //         Tip = L.S("Editor.Action.DeleteTempo"),
                //         IsDanger = true,
                //         Command = ReactiveCommand.Create(DeleteTempoAtPlayPos)
                //     });
                // }

                break;

            case TrackEditMode.MultiSelect:
                if (hasSelection)
                {
                    items.Add(new ContextActionItem
                    {
                        Icon = PackIconPhosphorIconsKind.Copy,
                        Tip = L.S("Common.Copy"),
                        Command = ReactiveCommand.Create(CopySelectedParts)
                    });
                    items.Add(new ContextActionItem
                    {
                        Icon = PackIconPhosphorIconsKind.Scissors,
                        Tip = L.S("Common.Cut"),
                        Command = ReactiveCommand.Create(CutSelectedParts)
                    });
                }

                items.Add(new ContextActionItem
                {
                    Icon = PackIconPhosphorIconsKind.Clipboard,
                    Tip = L.S("Common.Paste"),
                    Command = ReactiveCommand.Create(PasteParts)
                });
                if (hasSelection)
                {
                    items.Add(new ContextActionItem
                    {
                        Icon = PackIconPhosphorIconsKind.SplitHorizontal,
                        Tip = L.S("Editor.Action.Split"),
                        Command = ReactiveCommand.Create(SplitSelectedParts)
                    });
                    items.Add(new ContextActionItem
                    {
                        Icon = PackIconPhosphorIconsKind.Unite,
                        Tip = L.S("Editor.Action.Merge"),
                        Command = ReactiveCommand.Create(MergeSelectedParts)
                    });
                    items.Add(new ContextActionItem
                    {
                        Icon = PackIconPhosphorIconsKind.Trash,
                        Tip = L.S("Common.Delete"),
                        IsDanger = true,
                        Command = ReactiveCommand.Create(DeleteSelectedParts)
                    });
                }

                items.Add(new ContextActionItem
                {
                    Icon = PackIconPhosphorIconsKind.SelectionAll,
                    Tip = L.S("Common.SelectAll"),
                    Command = ReactiveCommand.Create(SelectAllParts)
                });
                break;
        }

        TrackContextActions = items;
    }

    // private void ResetTrackContextActionPlayPosCaches()
    // {
    //     _lastHasTempoAtPlayPos = null;
    //     _lastHasTimeSignatureAtPrevBar = null;
    // }

    // private void MaybeRebuildTrackContextActionsForPlayPos()
    // {
    //     if (TrackEditMode != TrackEditMode.Normal)
    //     {
    //         return;
    //     }
    //     bool hasTempoAtPlayPos = HasTempoAtPlayPos();
    //     bool hasTimeSignatureAtPrevBar = HasTimeSignatureAtPrevBar();
    //     if (_lastHasTempoAtPlayPos == hasTempoAtPlayPos &&
    //         _lastHasTimeSignatureAtPrevBar == hasTimeSignatureAtPrevBar)
    //     {
    //         return;
    //     }
    //     _lastHasTempoAtPlayPos = hasTempoAtPlayPos;
    //     _lastHasTimeSignatureAtPrevBar = hasTimeSignatureAtPrevBar;
    //     RebuildTrackContextActions();
    // }

    // ── 操作方法存根 ──────────────────────────────────────────────────

    private void DeleteSelectedParts()
    {
        if (SelectedParts.Count == 0)
        {
            return;
        }

        // 将editing的分片置空
        if (EditingVoicePart != null && SelectedParts.Contains(EditingVoicePart))
        {
            EditingVoicePart = null;
        }

        if (EditingWavePart != null && SelectedParts.Contains(EditingWavePart))
        {
            EditingWavePart = null;
        }

        DocManager.Inst.StartUndoGroup(deferValidate: true);
        foreach (UPart part in SelectedParts)
        {
            DocManager.Inst.ExecuteCmd(new RemovePartCommand(DocManager.Inst.Project, part));
        }

        DocManager.Inst.EndUndoGroup();
        SelectedParts.Clear();
    }

    private void RenameSelectedPart()
    {
        ToastService.Enqueue("功能正在开发");
    }

    private void CopySelectedParts()
    {
        if (SelectedParts.Count == 0)
        {
            return;
        }

        DocManager.Inst.PartsClipboard = SelectedParts.Select(p => p.Clone()).ToList();
        ToastService.Enqueue(string.Format(L.S("Editor.Selection.Copied"), SelectedParts.Count));
    }

    private void CutSelectedParts()
    {
        if (SelectedParts.Count == 0)
        {
            return;
        }

        DocManager.Inst.PartsClipboard = SelectedParts.Select(p => p.Clone()).ToList();

        if (EditingVoicePart != null && SelectedParts.Contains(EditingVoicePart))
        {
            EditingVoicePart = null;
        }

        if (EditingWavePart != null && SelectedParts.Contains(EditingWavePart))
        {
            EditingWavePart = null;
        }

        DocManager.Inst.StartUndoGroup(deferValidate: true);
        foreach (UPart part in SelectedParts)
        {
            DocManager.Inst.ExecuteCmd(new RemovePartCommand(DocManager.Inst.Project, part));
        }

        DocManager.Inst.EndUndoGroup();

        int count = SelectedParts.Count;
        SelectedParts.Clear();
        ToastService.Enqueue(string.Format(L.S("Editor.Selection.Cut"), count));
    }

    private void PasteParts()
    {
        if (DocManager.Inst.PartsClipboard == null || DocManager.Inst.PartsClipboard.Count == 0)
        {
            return;
        }

        var clones = DocManager.Inst.PartsClipboard.Select(p => p.Clone()).ToList();
        int minPosition = clones.Min(p => p.position);
        int offset = PlayPosTick - minPosition;
        clones.ForEach(p => p.position += offset);

        DocManager.Inst.StartUndoGroup(deferValidate: true);
        foreach (UPart part in clones)
        {
            DocManager.Inst.ExecuteCmd(new AddPartCommand(DocManager.Inst.Project, part));
        }

        DocManager.Inst.EndUndoGroup();

        SelectedParts.Clear();
        SelectedParts.AddRange(clones);
        ToastService.Enqueue(string.Format(L.S("Editor.Selection.Pasted"), clones.Count));
    }

    private void SplitSelectedParts()
    {
        ToastService.Enqueue("功能正在开发");
    }

    private void MergeSelectedParts()
    {
        ToastService.Enqueue("功能正在开发");
    }

    private void SelectAllParts()
    {
        SelectedParts.Clear();
        SelectedParts.AddRange(DocManager.Inst.Project.parts);
        ToastService.Enqueue(string.Format(L.S("Editor.Selection.AllSelected"), SelectedParts.Count));
    }

    private void AddTimeSignatureMarker()
    {
        ToastService.Enqueue("功能正在开发");
    }

    private void AddTempoMarker()
    {
        ToastService.Enqueue("功能正在开发");
    }

    private void GotoPrevTimeSignature()
    {
        ToastService.Enqueue("功能正在开发");
    }

    private void GotoNextTimeSignature()
    {
        ToastService.Enqueue("功能正在开发");
    }

    private void GotoPrevTempo()
    {
        ToastService.Enqueue("功能正在开发");
    }

    private void GotoNextTempo()
    {
        ToastService.Enqueue("功能正在开发");
    }

    private void DeleteTimeSignatureAtPrevBar()
    {
        ToastService.Enqueue("功能正在开发");
    }

    private void DeleteTempoAtPlayPos()
    {
        ToastService.Enqueue("功能正在开发");
    }

    private bool HasTimeSignatureAtPrevBar()
    {
        UProject project = DocManager.Inst.Project;
        int prevBarPos = GetPrevBarPosition();
        return project.timeSignatures.Any(ts => ts.barPosition == prevBarPos);
    }

    private bool HasTempoAtPlayPos()
    {
        UProject project = DocManager.Inst.Project;
        return project.tempos.Any(t => t.position == PlayPosTick);
    }

    private int GetPrevBarPosition()
    {
        // TODO: 实现正确的 tick→小节位置转换（参考 MusicMath.TickToBarBeat）
        return 0;
    }

    #endregion

    /// <summary>
    /// 当请求返回（返回按钮或返回键）
    /// </summary>
    private async Task OnBackAsync()
    {
        try
        {
            if (DocManager.Inst.ChangesSaved)
            {
                Navigator.NavigateBack(this);
                return;
            }

            byte actionRegister = await ShowExitEditorConfirmPopupAsync();
            switch ((ExitEditorActionRegister)actionRegister)
            {
                case ExitEditorActionRegister.ExitWithoutSave:
                    Preferences.Default.RecoveryPath = string.Empty;
                    Preferences.Save();
                    Navigator.NavigateBack(this);
                    return;
                case ExitEditorActionRegister.SaveAndExit:
                    if (await Save())
                    {
                        Navigator.NavigateBack(this);
                    }

                    return;
                case ExitEditorActionRegister.Cancel:
                default:
                    return;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error while handling back navigation in EditorViewModel");
            Navigator.NavigateBack(this);
        }
    }

    public override void OnBackRequested()
    {
        _ = OnBackAsync();
    }

    public void Dispose()
    {
        PlaybackManager.Inst.StopPlayback(); // 停止回放
        PlaybackTimer.Stop(); // 停止定时器
        _autoSaveTimer?.Stop(); // 停止自动保存定时器
        _panMotion.Dispose();
        DocManager.Inst.RemoveSubscriber(this); // 取消订阅事件

        // 取消订阅歌词编辑弹窗请求
        if (_onRequestEditLyric != null)
        {
            PianoRollViewModel.RequestEditLyric -= _onRequestEditLyric;
        }

        _disposables.Dispose(); // 释放绑定订阅
        PianoRollViewModel.Dispose(); // 释放子 ViewModel
        BackCommand.Dispose(); // 释放命令
        PlayPauseCommand.Dispose(); // 释放播放暂停命令
        StopCommand.Dispose(); // 释放停止命令
        GC.SuppressFinalize(this); // 告知 GC 无需调用终结器
    }
}