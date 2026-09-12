using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenUtau.Core;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Controls;

public partial class MixerPanel : UserControl, ICmdSubscriber
{
    public event Action? CloseRequested;
    public MixerViewModel ViewModel { get; } = new();

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

    private void OnChannelClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: MixerChannelViewModel channel })
        {
            ViewModel.SelectedChannel = channel;
            ViewModel.IsDetailOpen = true;
            UpdateLayoutMode();
        }
    }

    private void OnBackClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.IsDetailOpen = false;
        UpdateLayoutMode();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => CloseRequested?.Invoke();
}
