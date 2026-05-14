using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Controls;

/// <summary>
/// 轨道头画布。管理 TrackHeader 子控件的布局。
/// 纵向偏移由 TrackOffset 属性驱动，与 PartsCanvas 同步。
/// TODO: 赋予类似 ScrollViewer 的平滑滚动行为
/// </summary>
public class TrackHeaderCanvas : Panel, ICmdSubscriber
{
    #region 属性封装
    // 单个轨道高度
    public static readonly StyledProperty<double> TrackHeightProperty =
        AvaloniaProperty.Register<TrackHeaderCanvas, double>(nameof(TrackHeight));
    // 偏移
    public static readonly StyledProperty<double> TrackOffsetProperty =
        AvaloniaProperty.Register<TrackHeaderCanvas, double>(nameof(TrackOffset));
    // 是否展开详情
    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<TrackHeaderCanvas, bool>(nameof(IsExpanded));

    public double TrackHeight
    {
        get => GetValue(TrackHeightProperty);
        set => SetValue(TrackHeightProperty, value);
    }

    public double TrackOffset
    {
        get => GetValue(TrackOffsetProperty);
        set => SetValue(TrackOffsetProperty, value);
    }

    public bool IsExpanded
    {
        get => GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }
    #endregion

    #region 内部字段

    private readonly Dictionary<UTrack, TrackHeader> _trackHeaders = new();
    private TrackAdder? _trackAdder;

    #endregion

    static TrackHeaderCanvas()
    {
        ClipToBoundsProperty.OverrideDefaultValue<TrackHeaderCanvas>(false);
    }

    #region 轨道管理
    /// <summary>
    /// 全量更新
    /// </summary>
    private void FullUpdate()
    {
        Reset();
        _trackAdder = null;
        IList<UTrack> tracks = DocManager.Inst.Project.tracks;
        foreach (UTrack track in tracks)
        {
            TrackHeaderViewModel vm = new(track);
            TrackHeader view = new(vm)
            {
                IsExpanded = IsExpanded
            };
            Children.Add(view);
            _trackHeaders.Add(track, view);
        }

        _trackAdder = new TrackAdder
        {
            IsExpanded = IsExpanded
        };
        Children.Add(_trackAdder);
        InvalidateArrange();
    }
    /// <summary>
    /// 添加一个轨道头
    /// </summary>
    /// <param name="track"></param>
    private void Add(UTrack track)
    {
        TrackHeaderViewModel vm = new(track);
        TrackHeader view = new(vm)
        {
            IsExpanded = IsExpanded
        };
        Children.Add(view);
        _trackHeaders.Add(track, view);
        InvalidateArrange();
    }
    /// <summary>
    /// 删除指定轨道头
    /// </summary>
    private void Remove(UTrack track)
    {
        if (!_trackHeaders.TryGetValue(track, out TrackHeader? header)) return;
        header.Dispose();
        _trackHeaders.Remove(track);
        Children.Remove(header);
    }
    /// <summary>
    /// 通知指定的轨道头刷新自身属性
    /// </summary>
    /// <param name="track"></param>
    private void Refresh(UTrack track)
    {
        if (!_trackHeaders.TryGetValue(track, out TrackHeader? header)) return;
        header.ViewModel.Refresh();
    }
    /// <summary>
    /// 完全清空重置，销毁轨道头
    /// </summary>
    private void Reset()
    {
        foreach (UTrack t in new List<UTrack>(_trackHeaders.Keys))
            Remove(t);
        if (_trackAdder != null)
        {
            Children.Remove(_trackAdder);
            _trackAdder = null;
        }
        InvalidateArrange();
    }
    #endregion

    #region 集合订阅与生命周期
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        DocManager.Inst.AddSubscriber(this);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        DocManager.Inst.RemoveSubscriber(this);
        Reset();
    }
    #endregion

    public void OnNext(UCommand cmd, bool isUndo)
    {
        switch (cmd)
        {
            case LoadProjectNotification:
                FullUpdate();
                break;
            case AddTrackCommand addTrackCommand:
                Add(addTrackCommand.track);
                break;
            case RemoveTrackCommand removeTrackCommand:
                Remove(removeTrackCommand.track);
                break;
            case MoveTrackCommand:
                FullUpdate();
                break;
            case RenameTrackCommand renameTrackCommand:
                Refresh(renameTrackCommand.track);
                break;
            case TrackChangeSingerCommand:
                break;
            case TrackChangePhonemizerCommand:
                break;
            case TrackChangeRenderSettingCommand:
                break;
            case ChangeTrackColorCommand changeTrackColorCommand:
                Refresh(changeTrackColorCommand.track);
                break;
        }
    }

    #region 属性变化 → 重新布局
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TrackOffsetProperty || change.Property == TrackHeightProperty)
            InvalidateArrange();

        if (change.Property == IsExpandedProperty)
        {
            bool expanded = IsExpanded;
            foreach (TrackHeader header in _trackHeaders.Values)
                header.IsExpanded = expanded;
            _trackAdder?.IsExpanded = expanded;
        }
    }
    #endregion

    #region 布局

    /// <summary>
    /// avalonia问控件：我给你可用空间有这么多，实际上你需要多少？
    /// </summary>
    /// <param name="availableSize">可用</param>
    /// <returns>需要</returns>
    protected override Size MeasureOverride(Size availableSize)
    {
        // availableSize.Width 在 Auto 列下可能是无穷大，用有限宽度保护
        double w = double.IsInfinity(availableSize.Width) ? Bounds.Width : availableSize.Width;
        if (w <= 0) w = ViewConstants.TrackHeaderWidthCollapsed;
        foreach (Control child in Children)
            child.Measure(new Size(w, TrackHeight));
        return new Size(w, TrackHeight * (DocManager.Inst.Project.tracks.Count + 1));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double trackHeight = TrackHeight;
        double trackOffset = TrackOffset;
        int trackCount = DocManager.Inst.Project.tracks.Count;
        List<UTrack> tracks = DocManager.Inst.Project.tracks;

        for (int i = 0; i < tracks.Count; i++)
        {
            UTrack track = tracks[i];
            if (_trackHeaders.TryGetValue(track, out TrackHeader? header))
            {
                double y = i * trackHeight - trackOffset;
                header.Arrange(new Rect(0, y, finalSize.Width, trackHeight));
            }
        }

        // TrackAdder 紧跟最后一个轨道下方
        _trackAdder?.Arrange(new Rect(0, trackCount * trackHeight - trackOffset, finalSize.Width,
            trackHeight));

        return finalSize;
    }
    #endregion
}