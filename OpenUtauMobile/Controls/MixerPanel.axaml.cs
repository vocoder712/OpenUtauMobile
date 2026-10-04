using OpenUtauMobile.Services.Tracks;
using OpenUtauMobile.Services.Dialogs;
using System;
using System.Diagnostics;
using System.Linq;
using OpenUtau.Core.SignalChain.Effects;
using OpenUtauMobile.Helpers;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUtau.Core;
using OpenUtau.Core.SignalChain;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.ViewModels;
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
    private double _detailPaneWidth = 288;
    private bool _desktopFxWidthLoaded;

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
        if (Classes.Contains("DesktopMixer") && ViewModel.SelectedChannel != null) ViewModel.IsDetailOpen = true;
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
        long audiblePosition = PlaybackManager.Inst.AudibleSamplePosition;
        SetMeter(ViewModel.Master, _meters?.Master, audiblePosition);
        foreach (MixerChannelViewModel channel in ViewModel.Channels)
        {
            StereoPeakMeter? meter = null;
            if (channel.Track != null) _meters?.Tracks.TryGetValue(channel.Track, out meter);
            SetMeter(channel, meter, audiblePosition);
        }
    }

    private static void SetMeter(MixerChannelViewModel channel, StereoPeakMeter? meter, long audiblePosition)
    {
        // 无新样本（暂停、停止、等待渲染）即静音，回落和峰值保持由电平控件处理。
        (channel.LeftDb, channel.RightDb) = meter?.Consume(audiblePosition)
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
        bool desktop = Classes.Contains("DesktopMixer");
        ViewModel.IsWide = e.NewSize.Width >= (desktop ? 600 : 760);
        ViewModel.ChannelHeight = desktop ? Math.Max(360, e.NewSize.Height - 24) : Math.Max(480, e.NewSize.Height - 68);
        UpdateLayoutMode();
    }

    private void UpdateLayoutMode()
    {
        if (!_desktopFxWidthLoaded && Classes.Contains("DesktopMixer") &&
            Resources.TryGetValue("MixerDetailPaneWidth", out object? storedWidth) && storedWidth is double width)
        {
            _detailPaneWidth = Math.Clamp(width, 220, 1200);
            _desktopFxWidthLoaded = true;
        }
        if (BodyGrid.ColumnDefinitions.Count > 2 && BodyGrid.ColumnDefinitions[2].Width.IsAbsolute && BodyGrid.ColumnDefinitions[2].Width.Value > 0)
            _detailPaneWidth = BodyGrid.ColumnDefinitions[2].Width.Value;
        bool detail = ViewModel.IsDetailOpen && ViewModel.SelectedChannel != null;
        bool splitDetail = ViewModel.IsWide && detail;
        ChannelsScroll.IsVisible = ViewModel.IsWide || !detail;
        OverviewScroll.IsVisible = ViewModel.IsWide || !detail;
        DetailBorder.IsVisible = detail;
        DetailSplitter.IsVisible = splitDetail && Classes.Contains("DesktopMixer");
        BodyGrid.ColumnDefinitions[1].Width = new GridLength(DetailSplitter.IsVisible ? 6 : 0);
        bool desktop = Classes.Contains("DesktopMixer");
        double maximumPane = Math.Max(220, BodyGrid.Bounds.Width - 180);
        BodyGrid.ColumnDefinitions[0].MinWidth = desktop && splitDetail ? 180 : 0;
        BodyGrid.ColumnDefinitions[2].MinWidth = desktop && splitDetail ? 220 : 0;
        BodyGrid.ColumnDefinitions[2].MaxWidth = desktop && splitDetail ? maximumPane : double.PositiveInfinity;
        BodyGrid.ColumnDefinitions[2].Width = new GridLength(splitDetail ? (desktop ? Math.Clamp(_detailPaneWidth, 220, maximumPane) : 320) : 0);
        Grid.SetColumn(DetailBorder, splitDetail ? 2 : 0);
    }

    public void ResetDesktopFxPaneWidth(double width)
    {
        if (!Classes.Contains("DesktopMixer")) return;
        _detailPaneWidth = Math.Clamp(width, 220, 1200);
        Resources["MixerDetailPaneWidth"] = _detailPaneWidth;
        if (BodyGrid.ColumnDefinitions.Count > 2 && DetailBorder.IsVisible)
            BodyGrid.ColumnDefinitions[2].Width = new GridLength(_detailPaneWidth);
        UpdateLayoutMode();
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

    private bool _presetDialogOpen;

    private async void OnLoadEffectPreset(object? sender, RoutedEventArgs e)
    {
        if (_presetDialogOpen || sender is not Button { Tag: string effect } button || ViewModel.SelectedChannel?.Track == null) return;
        string[] names = effect switch
        {
            "Eq" => FxPresets.EqPresetNames,
            "Comp" => FxPresets.CompPresetNames,
            "Reverb" => FxPresets.ReverbPresetNames,
            _ => []
        };
        if (names.Length == 0) return;
        UProject project = DocManager.Inst.Project;
        UTrack track = ViewModel.SelectedChannel.Track;
        _presetDialogOpen = true;
        if (!Classes.Contains("DesktopMixer"))
        {
            try
            {
                OptionConfirmPopupViewModel picker = new(L.S("Mixer.LoadPreset"), string.Empty,
                    names.Select(key => new[] { new OptionConfirmOption(L.S("Mixer.Preset." + key), key) }));
                string? key = await PopupService.Show<string>(new MixerPresetPopup(), picker);
                if (key != null && DocManager.Inst.Project == project && project.tracks.Contains(track) && ViewModel.SelectedChannel?.Track == track)
                    ViewModel.ApplyEffectPreset(effect, key);
            }
            finally { _presetDialogOpen = false; }
            return;
        }
        MenuFlyout flyout = new();
        foreach (string key in names)
        {
            MenuItem item = new() { Header = L.S("Mixer.Preset." + key) };
            item.Click += (_, _) =>
            {
                if (DocManager.Inst.Project == project && project.tracks.Contains(track) && ViewModel.SelectedChannel?.Track == track)
                    ViewModel.ApplyEffectPreset(effect, key);
            };
            flyout.Items.Add(item);
        }
        flyout.Closed += (_, _) => _presetDialogOpen = false;
        flyout.ShowAt(button);
    }

    private async void OnAddUserPreset(object? sender, RoutedEventArgs e)
    {
        if (_presetDialogOpen || ViewModel.SelectedChannel?.Track == null) return;
        UProject project = DocManager.Inst.Project;
        UTrack track = ViewModel.SelectedChannel.Track;
        _presetDialogOpen = true;
        try
        {
            string? name = await TextInputPopupService.ShowAsync(L.S("Mixer.NewPresetTitle"), L.S("Mixer.NewPresetHint"),
                L.S("Mixer.PresetName"), validate: ViewModel.ValidatePresetName);
            if (name != null && DocManager.Inst.Project == project && project.tracks.Contains(track) && ViewModel.SelectedChannel?.Track == track)
                ViewModel.AddUserPreset(name);
        }
        finally { _presetDialogOpen = false; }
    }

    private void OnUpdateUserPreset(object? sender, RoutedEventArgs e) => ViewModel.UpdateUserPreset();
    private void OnDeleteUserPreset(object? sender, RoutedEventArgs e) => ViewModel.DeleteUserPreset();

    private void OnCloseClick(object? sender, RoutedEventArgs e) => CloseRequested?.Invoke();
}
