using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using OpenUtau.Core;
using OpenUtau.Core.SignalChain;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.ViewModels;
using OpenUtauMobile.Services;
using Serilog;

namespace OpenUtauMobile.Controls;

public partial class MixerPanel : UserControl, ICmdSubscriber
{
    public event Action? CloseRequested;
    public MixerViewModel ViewModel { get; } = new();
    private bool _editingIdentity;
    private readonly DispatcherTimer _meterTimer = new() { Interval = TimeSpan.FromMilliseconds(34) };
    private PlaybackMeters? _meters;
    private double _lastMeterFrame;

    public MixerPanel()
    {
        InitializeComponent();
        DataContext = ViewModel;
        _meterTimer.Tick += OnMeterFrame;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ViewModel.Refresh(DocManager.Inst.Project);
        ViewModel.Activate();
        UpdateLayoutMode();
        DocManager.Inst.AddSubscriber(this);
        _meterTimer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ViewModel.Deactivate();
        _meterTimer.Stop();
        ReleaseMeters();
        DocManager.Inst.RemoveSubscriber(this);
        base.OnDetachedFromVisualTree(e);
    }

    private void ReleaseMeters()
    {
        if (_meters != null) _meters.Enabled = false;
        _meters = null;
    }

    private void OnMeterFrame(object? sender, EventArgs e)
    {
        double now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        if (now - _lastMeterFrame < 1.0 / 30) return;
        _lastMeterFrame = now;
        PlaybackMeters? current = IsEffectivelyVisible ? PlaybackManager.Inst.Meters : null;
        if (current?.Project != DocManager.Inst.Project) current = null;
        if (!ReferenceEquals(current, _meters))
        {
            ReleaseMeters();
            _meters = current;
            if (_meters != null)
            {
                // 重开面板或恢复播放时丢弃旧邮箱，避免显示暂停前积存的峰值。
                _meters.Master.Consume();
                foreach (StereoPeakMeter meter in _meters.Tracks.Values) meter.Consume();
                _meters.Enabled = true;
            }
        }
        SetMeter(ViewModel.Master, _meters?.Master);
        foreach (MixerChannelViewModel channel in ViewModel.Channels)
        {
            StereoPeakMeter? meter = null;
            if (channel.Track != null) _meters?.Tracks.TryGetValue(channel.Track, out meter);
            SetMeter(channel, meter);
        }
    }

    private static void SetMeter(MixerChannelViewModel channel, StereoPeakMeter? meter)
    {
        // 无新样本（暂停、停止、等待渲染）即静音，回落和峰值保持由电平控件处理。
        (channel.LeftDb, channel.RightDb) = meter?.Consume()
            ?? (double.NegativeInfinity, double.NegativeInfinity);
    }

    public void OnNext(UCommand cmd, bool isUndo)
    {
        if (cmd is MixCommand or VolumeChangeNotification or PanChangeNotification)
        {
            ViewModel.RefreshParameters();
            return;
        }
        if (cmd is TrackCommand or LoadProjectNotification)
        {
            ViewModel.Refresh(DocManager.Inst.Project);
            UpdateLayoutMode();
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        // 按混音面板实际可用宽度响应，横屏手机不强制挤出详情列。
        ViewModel.IsWide = e.NewSize.Width >= 760;
        ViewModel.ChannelHeight = Math.Max(480, e.NewSize.Height - 68); // 最小高度 480，留出标题栏和底部按钮栏的空间。
        UpdateLayoutMode();
    }

    private void UpdateLayoutMode()
    {
        bool detail = ViewModel.IsDetailOpen && ViewModel.SelectedChannel != null;
        ChannelsScroll.IsVisible = ViewModel.IsWide || !detail;
        OverviewScroll.IsVisible = ViewModel.IsWide || !detail;
        DetailBorder.IsVisible = detail;
        BodyGrid.ColumnDefinitions[1].Width = new GridLength(ViewModel.IsWide && detail ? 320 : 0);
        Grid.SetColumn(DetailBorder, ViewModel.IsWide ? 1 : 0);
    }

    private void OnFxClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: MixerChannelViewModel channel })
        {
            ViewModel.SelectedChannel = channel;
            ViewModel.IsDetailOpen = true;
            UpdateLayoutMode();
        }
    }

    private void OnFxToggleClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: MixerChannelViewModel channel })
            channel.FxEnabled = !channel.FxEnabled;
    }

    private async void OnRenameClick(object? sender, RoutedEventArgs e) => await EditIdentityAsync(sender, false);

    private async void OnColorClick(object? sender, RoutedEventArgs e) => await EditIdentityAsync(sender, true);

    private async Task EditIdentityAsync(object? sender, bool editColor)
    {
        if (_editingIdentity || sender is not Button { DataContext: MixerChannelViewModel channel } || channel.Track == null) return;
        _editingIdentity = true;
        try
        {
            // 获取当前项目和轨道
            UProject project = DocManager.Inst.Project;
            UTrack track = channel.Track;
            string? value = editColor
                ? await TrackHeaderService.Inst.PickTrackColorAsync(track.TrackColor)
                : await TrackHeaderService.Inst.PickTrackNameAsync(track.TrackName);
            if (string.IsNullOrWhiteSpace(value)) return;
            value = value.Trim();
            // 弹窗关闭前可能已切换工程或删除轨道，不向失效对象提交命令。
            if (DocManager.Inst.Project != project || !project.tracks.Contains(track) ||
                value == (editColor ? track.TrackColor : track.TrackName)) return;
            DocManager.Inst.StartUndoGroup(editColor ? "改变轨道颜色" : "重命名轨道");
            try
            {
                DocManager.Inst.ExecuteCmd(editColor
                    ? new ChangeTrackColorCommand(project, track, value)
                    : new RenameTrackCommand(project, track, value));
            }
            finally
            {
                DocManager.Inst.EndUndoGroup();
            }
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Failed to edit mixer track identity.");
            ErrorDialogService.Show(new ErrorDialogViewModel(new ErrorMessageNotification(exception)));
        }
        finally
        {
            _editingIdentity = false;
        }
    }

    private void OnBackClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.IsDetailOpen = false;
        UpdateLayoutMode();
    }

    private void OnApplyEffectPreset(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string effect }) ViewModel.ApplyEffectPreset(effect);
    }

    private void OnDefaultPreset(object? sender, RoutedEventArgs e) => ViewModel.ApplyDefaultPreset();
    private void OnApplyUserPreset(object? sender, RoutedEventArgs e) => ViewModel.ApplyUserPreset();
    private void OnSaveUserPreset(object? sender, RoutedEventArgs e) => ViewModel.SaveUserPreset();
    private void OnDeleteUserPreset(object? sender, RoutedEventArgs e) => ViewModel.DeleteUserPreset();

    private void OnCloseClick(object? sender, RoutedEventArgs e) => CloseRequested?.Invoke();
}
