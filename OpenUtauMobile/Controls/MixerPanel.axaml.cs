using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenUtau.Core;
using OpenUtauMobile.ViewModels;
using OpenUtauMobile.Services;
using Serilog;

namespace OpenUtauMobile.Controls;

public partial class MixerPanel : UserControl, ICmdSubscriber
{
    public event Action? CloseRequested;
    public MixerViewModel ViewModel { get; } = new();
    private bool _editingIdentity;

    public MixerPanel()
    {
        InitializeComponent();
        DataContext = ViewModel;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ViewModel.Refresh(DocManager.Inst.Project);
        UpdateLayoutMode();
        DocManager.Inst.AddSubscriber(this);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        DocManager.Inst.RemoveSubscriber(this);
        base.OnDetachedFromVisualTree(e);
    }

    public void OnNext(UCommand cmd, bool isUndo)
    {
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
        ViewModel.ChannelHeight = Math.Max(280, e.NewSize.Height - 108);
        UpdateLayoutMode();
    }

    private void UpdateLayoutMode()
    {
        bool detail = ViewModel.IsDetailOpen && ViewModel.SelectedChannel != null;
        ChannelsScroll.IsVisible = ViewModel.IsWide || !detail;
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

    private async void OnRenameClick(object? sender, RoutedEventArgs e) => await EditIdentityAsync(sender, false);

    private async void OnColorClick(object? sender, RoutedEventArgs e) => await EditIdentityAsync(sender, true);

    private async Task EditIdentityAsync(object? sender, bool editColor)
    {
        if (_editingIdentity || sender is not Button { DataContext: MixerChannelViewModel channel }) return;
        _editingIdentity = true;
        try
        {
            var project = DocManager.Inst.Project;
            var track = channel.Track;
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

    private void OnCloseClick(object? sender, RoutedEventArgs e) => CloseRequested?.Invoke();
}
