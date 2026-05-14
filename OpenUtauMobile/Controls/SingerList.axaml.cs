using System.Collections.Generic;
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
            SingerItemsControl.ItemsSource = Singers;
        }
    }
}