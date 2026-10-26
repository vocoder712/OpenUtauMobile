using System;
using System.Reactive;
using System.Reactive.Linq;
using OpenUtau.Core;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services.Dialogs;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtauMobile.ViewModels;

/// <summary>编辑器常驻工具栏与状态行的展示绑定。</summary>
public partial class EditorViewModel
{
    /// <summary>当前工程历史是否允许撤销。</summary>
    [Reactive] public bool CanUndo { get; private set; }

    /// <summary>当前工程历史是否允许重做。</summary>
    [Reactive] public bool CanRedo { get; private set; }

    /// <summary>节拍器开关复用现有播放管理器。</summary>
    [Reactive] public bool IsMetronomeEnabled { get; set; }

    /// <summary>重复播放开关复用现有播放管理器。</summary>
    [Reactive] public bool IsRepeatEnabled { get; set; }

    /// <summary>录制暂只显示待实现提示。</summary>
    public ReactiveCommand<Unit, Unit> RecordCommand { get; } =
        ReactiveCommand.Create(() => ToastService.Enqueue(L.S("Editor.RecordTodo")));

    /// <summary>根据当前工程时间轴换算的播放时间。</summary>
    private TimeSpan PlaybackPositionTime => TimeSpan.FromMilliseconds(
        Math.Max(0, DocManager.Inst.Project.timeAxis.TickPosToMsPos(PlayPosTick)));

    /// <summary>时间码主行，显示分和秒。</summary>
    public string PlaybackMinutesSeconds => PlaybackPositionTime.ToString(@"mm\:ss");

    /// <summary>时间码次行，显示小数点及毫秒。</summary>
    public string PlaybackFraction => PlaybackPositionTime.ToString(@"\.fff");

    /// <summary>展示绑定复用已有通知，不另行启动播放计时器。</summary>
    private void InitializeEditorChrome()
    {
        IsMetronomeEnabled = PlaybackManager.Inst.Metronome;
        IsRepeatEnabled = PlaybackManager.Inst.LoopPlayback;
        this.WhenAnyValue(vm => vm.IsMetronomeEnabled).Skip(1)
            .Subscribe(enabled => PlaybackManager.Inst.Metronome = enabled).DisposeWith(_disposables);
        this.WhenAnyValue(vm => vm.IsRepeatEnabled).Skip(1)
            .Subscribe(enabled => PlaybackManager.Inst.LoopPlayback = enabled).DisposeWith(_disposables);
        this.WhenAnyValue(vm => vm.IsLoadingProject)
            .Subscribe(_ => RefreshHistoryState()).DisposeWith(_disposables);
        this.WhenAnyValue(vm => vm.PlayPosTick)
            .Subscribe(_ => RefreshPlaybackTime()).DisposeWith(_disposables);
    }

    /// <summary>两行时间码复用现有播放位置通知。</summary>
    private void RefreshPlaybackTime()
    {
        this.RaisePropertyChanged(nameof(PlaybackMinutesSeconds));
        this.RaisePropertyChanged(nameof(PlaybackFraction));
    }

    /// <summary>在命令通知及加载状态变化时同步常驻历史按钮。</summary>
    private void RefreshHistoryState()
    {
        DocManager manager = DocManager.Inst;
        bool available = !IsLoadingProject && !manager.HasOpenUndoGroup;
        CanUndo = available && manager.GetUndoState(out _);
        CanRedo = available && manager.GetRedoState(out _);
    }
}
