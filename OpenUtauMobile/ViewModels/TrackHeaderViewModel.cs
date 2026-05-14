using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using OpenUtau.Api;
using OpenUtau.Core;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using OpenUtauMobile.Services;
using OpenUtauMobile.Themes.OpenUtauMobile.Runtime;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;

namespace OpenUtauMobile.ViewModels;

/// <summary>
/// 单条轨道头的 ViewModel，对应一个 UTrack。
/// 由 TrackHeaderCanvas.Add() 创建，TrackHeaderCanvas.Remove() 释放。
/// </summary>
public class TrackHeaderViewModel : ViewModelBase, IDisposable
{
    private readonly UTrack _track;
    private readonly CompositeDisposable _disposable = [];
    private bool _isRefreshing;

    [Reactive] public string TrackName { get; set; } = string.Empty;
    [Reactive] public bool Muted { get; set; }
    [Reactive] public double Volume { get; set; }
    [Reactive] public double Pan { get; set; }
    [Reactive] public IBrush TrackColorBrush { get; set; } = Brushes.Transparent;
    [Reactive] public string SingerName { get; set; } = string.Empty;
    [Reactive] public Bitmap? SingerIcon { get; set; }

    /// <summary>
    /// 音素器缩写，用于 UI 显示
    /// </summary>
    [Reactive]
    public string PhonemizerTag { get; set; } = string.Empty;

    [Reactive] public string RendererName { get; set; } = string.Empty;
    [Reactive] public bool IsExpanded { get; set; }

    public ReactiveCommand<Unit, Unit> MuteCommand { get; }
    public ReactiveCommand<Unit, Unit> SoloCommand { get; }
    public ReactiveCommand<Unit, Unit> SelectSingerCommand { get; }
    public ReactiveCommand<Unit, Unit> SelectPhonemizerCommand { get; }
    public ReactiveCommand<Unit, Unit> SelectRendererCommand { get; }
    public ReactiveCommand<Unit, Unit> RemoveTrackCommand { get; }
    public ReactiveCommand<Unit, Unit> MoveUpCommand { get; }
    public ReactiveCommand<Unit, Unit> MoveDownCommand { get; }
    public ReactiveCommand<Unit, Unit> RenameCommand { get; }
    public ReactiveCommand<Unit, Unit> ChangeColorCommand { get; }

    public TrackHeaderViewModel(UTrack track)
    {
        _track = track;
        Refresh();
        // 订阅 Volume 属性变化
        this.WhenAnyValue(vm => vm.Volume)
            .Skip(1) // 跳过初始值
            .Subscribe(volume =>
            {
                if (_isRefreshing)
                    return;
                _track.Volume = volume;
                DocManager.Inst.ExecuteCmd(new VolumeChangeNotification(_track.TrackNo, volume));
            }).DisposeWith(_disposable);

        // 订阅 Pan 属性变化
        this.WhenAnyValue(vm => vm.Pan)
            .Skip(1) // 跳过初始值
            .Subscribe(pan =>
            {
                if (_isRefreshing)
                    return;
                _track.Pan = pan;
                DocManager.Inst.ExecuteCmd(new PanChangeNotification(_track.TrackNo, pan));
            }).DisposeWith(_disposable);
        // 静音切换
        MuteCommand = ReactiveCommand.Create(ToggleMute).DisposeWith(_disposable);

        SoloCommand = ReactiveCommand.Create(ToggleSolo).DisposeWith(_disposable);
        // 选择歌手命令
        SelectSingerCommand = ReactiveCommand.CreateFromTask(SelectSinger).DisposeWith(_disposable);
        // 选择音素器命令
        SelectPhonemizerCommand = ReactiveCommand.CreateFromTask(SelectPhonemizer).DisposeWith(_disposable);
        // 选择渲染器命令
        SelectRendererCommand = ReactiveCommand.CreateFromTask(SelectRenderer).DisposeWith(_disposable);
        // 删除命令
        RemoveTrackCommand = ReactiveCommand.Create(RemoveTrack).DisposeWith(_disposable);
        // 上移命令
        MoveUpCommand = ReactiveCommand.Create(MoveUp).DisposeWith(_disposable);
        // 下移命令
        MoveDownCommand = ReactiveCommand.Create(MoveDown).DisposeWith(_disposable);
        // 重命名命令
        RenameCommand = ReactiveCommand.CreateFromTask(Rename).DisposeWith(_disposable);
        // 更改轨道颜色命令
        ChangeColorCommand = ReactiveCommand.CreateFromTask(ChangeColor).DisposeWith(_disposable);
    }

    private async Task SelectSinger()
    {
        USinger? singer = await TrackHeaderService.Inst.PickSingerAsync();
        if (singer == null) return;
        DocManager.Inst.StartUndoGroup("切换歌手");
        Log.Information("正在为轨道 {TrackName} 选择歌手 {SingerName}", TrackName, singer.Name);
        // 执行切换歌手命令
        DocManager.Inst.ExecuteCmd(new TrackChangeSingerCommand(DocManager.Inst.Project, _track, singer));
        // 切音素器
        if (!string.IsNullOrEmpty(singer.Id) &&
            Preferences.Default.SingerPhonemizers.TryGetValue(singer.Id, out string? phonemizerName) &&
            TryChangePhonemizer(phonemizerName))
        {
        }
        else if (!string.IsNullOrEmpty(singer.DefaultPhonemizer))
        {
            TryChangePhonemizer(singer.DefaultPhonemizer);
        }

        // 切渲染器
        if (!singer.Found) // 默认渲染器
        {
            URenderSettings settings = new();
            DocManager.Inst.ExecuteCmd(new TrackChangeRenderSettingCommand(DocManager.Inst.Project, _track, settings));
        }
        else if (singer.SingerType != _track.RendererSettings.Renderer?.SingerType)
        {
            URenderSettings settings = new()
            {
                renderer = Renderers.GetDefaultRenderer(singer.SingerType), // 根据歌手类型
            };
            DocManager.Inst.ExecuteCmd(new TrackChangeRenderSettingCommand(DocManager.Inst.Project, _track, settings));
        }

        // 
        DocManager.Inst.ExecuteCmd(new VoiceColorRemappingNotification(_track.TrackNo, true));
        DocManager.Inst.EndUndoGroup();
        // 保存
        if (!string.IsNullOrEmpty(singer.Id) && singer.Found)
        {
            Preferences.Default.RecentSingers.Remove(singer.Id);
            Preferences.Default.RecentSingers.Insert(0, singer.Id);
            if (Preferences.Default.RecentSingers.Count > 16)
            {
                Preferences.Default.RecentSingers.RemoveRange(16, Preferences.Default.RecentSingers.Count - 16);
            }
        }

        Preferences.Save();

        Refresh();
    }

    private async Task SelectPhonemizer()
    {
        Phonemizer? phonemizer = await TrackHeaderService.Inst.PickPhonemizerAsync();
        if (phonemizer == null) return;
        Log.Information("正在为轨道 {TrackName} 选择音素器 {PhonemizerName}", TrackName, phonemizer.Name);

        DocManager.Inst.StartUndoGroup("切换音素器");
        DocManager.Inst.ExecuteCmd(new TrackChangePhonemizerCommand(DocManager.Inst.Project, _track, phonemizer));
        DocManager.Inst.EndUndoGroup();
        string? name = phonemizer.GetType().FullName;
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        if (!string.IsNullOrEmpty(_track.Singer?.Id) && phonemizer != null)
        {
            Preferences.Default.SingerPhonemizers[_track.Singer.Id] = name;
        }

        Preferences.Default.RecentPhonemizers.Remove(name);
        Preferences.Default.RecentPhonemizers.Insert(0, name);
        while (Preferences.Default.RecentPhonemizers.Count > 8)
        {
            Preferences.Default.RecentPhonemizers.RemoveRange(8, Preferences.Default.RecentPhonemizers.Count - 8);
        }

        Preferences.Save();
        Refresh();
    }

    private async Task SelectRenderer()
    {
        if (_track.Singer is not { Found: true })
        {
            return;
        }

        string[] supportedRenderers = Renderers.GetSupportedRenderers(_track.Singer.SingerType);
        if (supportedRenderers.Length == 0)
        {
            return;
        }

        string? renderer = await TrackHeaderService.Inst.PickRendererAsync(supportedRenderers);
        if (string.IsNullOrEmpty(renderer))
        {
            return;
        }

        URenderSettings settings = _track.RendererSettings?.Clone() ?? new URenderSettings();
        settings.renderer = renderer;

        DocManager.Inst.StartUndoGroup();
        DocManager.Inst.ExecuteCmd(new TrackChangeRenderSettingCommand(DocManager.Inst.Project, _track, settings));
        DocManager.Inst.EndUndoGroup();
        Refresh();
    }


    private void ToggleMute()
    {
        _track.Mute = !_track.Mute;
        JudgeMuted();
    }

    private void ToggleSolo()
    {
        _track.Solo = !_track.Solo;
        JudgeMuted();
    }

    /// <summary>
    /// 计算后端模型静音状态，同步数据
    /// </summary>
    private void JudgeMuted()
    {
        // TODO: 暂不考虑solo
        _track.Muted = _track is { Mute: true, Solo: false };
        DocManager.Inst.ExecuteCmd(new VolumeChangeNotification(_track.TrackNo, _track.Muted ? -24 : _track.Volume));
        Refresh();
    }

    private bool TryChangePhonemizer(string phonemizerName)
    {
        try
        {
            PhonemizerFactory? factory = PhonemizerFactory.Get(phonemizerName);
            Phonemizer? phonemizer = factory?.Create();
            if (phonemizer != null)
            {
                DocManager.Inst.ExecuteCmd(
                    new TrackChangePhonemizerCommand(DocManager.Inst.Project, _track, phonemizer));
                return true;
            }
        }
        catch (Exception e)
        {
            Log.Error(e, $"无法加载音素器 {phonemizerName}");
        }

        return false;
    }

    private void RemoveTrack()
    {
        DocManager.Inst.StartUndoGroup("删除轨道");
        DocManager.Inst.ExecuteCmd(new RemoveTrackCommand(DocManager.Inst.Project, _track));
        DocManager.Inst.EndUndoGroup();
    }

    private void MoveUp()
    {
        if (_track == DocManager.Inst.Project.tracks.First())
        {
            return;
        }

        DocManager.Inst.StartUndoGroup("上移轨道");
        DocManager.Inst.ExecuteCmd(new MoveTrackCommand(DocManager.Inst.Project, _track, true));
        DocManager.Inst.EndUndoGroup();
    }

    private void MoveDown()
    {
        if (_track == DocManager.Inst.Project.tracks.Last())
        {
            return;
        }

        DocManager.Inst.StartUndoGroup("下移轨道");
        DocManager.Inst.ExecuteCmd(new MoveTrackCommand(DocManager.Inst.Project, _track, false));
        DocManager.Inst.EndUndoGroup();
    }

    private async Task Rename()
    {
        string? trackName = await TrackHeaderService.Inst.PickTrackNameAsync(_track.TrackName);
        if (string.IsNullOrWhiteSpace(trackName))
        {
            return;
        }

        trackName = trackName.Trim();
        if (string.Equals(trackName, _track.TrackName, StringComparison.Ordinal))
        {
            return;
        }

        DocManager.Inst.StartUndoGroup("重命名轨道");
        DocManager.Inst.ExecuteCmd(new RenameTrackCommand(DocManager.Inst.Project, _track, trackName));
        DocManager.Inst.EndUndoGroup();
        Refresh();
    }

    private async Task ChangeColor()
    {
        string? colorName = await TrackHeaderService.Inst.PickTrackColorAsync(_track.TrackColor);
        if (string.IsNullOrEmpty(colorName) || string.Equals(colorName, _track.TrackColor, StringComparison.Ordinal))
        {
            return;
        }

        DocManager.Inst.StartUndoGroup("改变轨道颜色");
        DocManager.Inst.ExecuteCmd(new ChangeTrackColorCommand(DocManager.Inst.Project, _track, colorName));
        DocManager.Inst.EndUndoGroup();
        Refresh();
    }

    private void RefreshAvatar()
    {
        USinger? singer = _track.Singer;
        if (singer?.AvatarData == null)
        {
            SingerIcon = null;
            return;
        }

        try
        {
            using MemoryStream stream = new(singer.AvatarData);
            SingerIcon = new Bitmap(stream);
        }
        catch (Exception e)
        {
            SingerIcon = null;
            Debug.WriteLine(e);
            Log.Error(e, "Failed to decode avatar.");
        }
    }

    /// <summary>
    /// 从 UTrack 重新同步所有属性到 ViewModel（响应 DocManager 命令后调用）。
    /// </summary>
    public void Refresh()
    {
        _isRefreshing = true;
        try
        {
            TrackName = _track.TrackName;
            Muted = _track.Muted;
            Volume = _track.Volume;
            Pan = _track.Pan;
            TrackColorBrush = _track.Muted ? Brushes.Gray : TrackPalette.GetTrackColor(_track.TrackColor).AccentColor;

            SingerName = _track.Singer?.Name ?? string.Empty; // 为什么 Core 项目里面乱写null啊啊啊啊！！！~~~
            PhonemizerTag = _track.Phonemizer?.Tag ?? string.Empty;
            RendererName = _track.RendererSettings.renderer ?? string.Empty;
            RefreshAvatar();
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    public void Dispose()
    {
        _disposable.Dispose();
        GC.SuppressFinalize(this);
    }
}