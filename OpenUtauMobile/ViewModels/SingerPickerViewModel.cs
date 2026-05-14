using System.Collections.Generic;
using System.Reactive;
using System.Threading.Tasks;
using DynamicData.Binding;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
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
        List<USinger> list = await Task.Run(() =>
        {
            List<USinger> singers = [];
            foreach (var group in SingerManager.Inst.SingerGroups.Values)
            {
                if (group == null) continue;
                foreach (var singer in group)
                {
                    if (singer != null)
                    {
                        singers.Add(singer);
                    }
                }
            }

            return singers;
        });
        Singers.Load(list);
        IsLoading = false;
    }

    public override void RequestBack()
    {
        RaiseClose(null);
    }
}