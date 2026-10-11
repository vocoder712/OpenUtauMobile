using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using OpenUtau.Core.Ustx;

namespace OpenUtauMobile.Controls;

/// <summary>
/// 歌手列表控件。负责展示歌手卡片，点击时触发 <see cref="SelectSingerCommand"/>。
/// 数据源通过 <see cref="Singers"/> 属性注入，命令由宿主（管理页 / 弹窗）分别绑定不同语义。
/// </summary>
public partial class SingerList : UserControl
{
    private INotifyCollectionChanged? _subscribedSingers;
    private bool _attached;
    #region 数据源
    public static readonly StyledProperty<IEnumerable<USinger>?> SingersProperty =
        AvaloniaProperty.Register<SingerList, IEnumerable<USinger>?>(nameof(Singers));
    #endregion

    public IEnumerable<USinger>? Singers
    {
        get => GetValue(SingersProperty);
        set => SetValue(SingersProperty, value);
    }

    public static readonly StyledProperty<ICommand?> SelectSingerCommandProperty =
        AvaloniaProperty.Register<SingerList, ICommand?>(nameof(SelectSingerCommand));

    public ICommand? SelectSingerCommand
    {
        get => GetValue(SelectSingerCommandProperty);
        set => SetValue(SelectSingerCommandProperty, value);
    }

    public SingerList()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SingersProperty)
        {
            SubscribeToSingers();
            RefreshSearch();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        SubscribeToSingers();
        RefreshSearch();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        SubscribeToSingers();
        base.OnDetachedFromVisualTree(e);
    }

    private void SubscribeToSingers()
    {
        if (_subscribedSingers != null)
        {
            _subscribedSingers.CollectionChanged -= OnSingersChanged;
        }
        _subscribedSingers = _attached ? Singers as INotifyCollectionChanged : null;
        if (_subscribedSingers != null)
        {
            _subscribedSingers.CollectionChanged += OnSingersChanged;
        }
    }

    private void OnSingersChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshSearch();

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e) => RefreshSearch();

    private void RefreshSearch()
    {
        string query = SingerSearch.Text ?? string.Empty;
        // 与桌面一致，按本地化名称、标识和作者进行不区分大小写的匹配。
        USinger[] filtered = (Singers ?? []).Where(singer =>
            (singer.LocalizedName + " " + singer.Id + " " + singer.Author)
                .Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToArray();
        SingerItemsControl.ItemsSource = filtered;
        SingerEmptyMessage.IsVisible = query.Length > 0 && filtered.Length == 0;
    }
}