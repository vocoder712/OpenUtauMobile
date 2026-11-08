using System.Collections.Generic;
using System.Reactive;
using System.Threading.Tasks;
using DynamicData.Binding;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Helpers;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtauMobile.ViewModels;

/// <summary>
/// 歌手选择弹窗 ViewModel。
/// 实现 <see cref="IDialogContext"/>，选中歌手后通过 <see cref="RequestClose"/> 返回选中的 <see cref="USinger"/>；
/// 点取消时返回 <c>null</c>。
/// 可被 <see cref="SingerManagementViewModel"/> 或任意需要选歌手的场景复用。
/// </summary>
public class SingerPickerViewModel : PopupViewModelBase
{
    [Reactive] public ObservableCollectionExtended<USinger> Singers { get; set; } = [];
    [Reactive] public bool IsLoading { get; set; } = true;

    /// <summary>选中歌手命令：触发关闭并返回该歌手。</summary>
    public ReactiveCommand<USinger, Unit> SelectSingerCommand { get; }

    /// <summary>取消命令：返回 null。</summary>
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    public SingerPickerViewModel()
    {
        SelectSingerCommand = ReactiveCommand.Create<USinger>(singer => { RaiseClose(singer); });

        CancelCommand = ReactiveCommand.Create(() => { RaiseClose(null); });

        _ = LoadSingersAsync();
    }

    private async Task LoadSingersAsync()
    {
        IsLoading = true;
        // 按歌手类型固定顺序展开，保证与歌手管理页顺序一致
        List<USinger> list = await Task.Run(SingerListHelper.GetOrderedSingers);
        Singers.Load(list);
        IsLoading = false;
    }

    public override void RequestBack()
    {
        RaiseClose(null);
    }
}