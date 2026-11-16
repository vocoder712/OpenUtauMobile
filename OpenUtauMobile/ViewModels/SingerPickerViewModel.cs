using System;
using System.Linq;
using System.Reactive;
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
public class SingerPickerViewModel : PopupViewModelBase, IDisposable
{
    [Reactive] public ObservableCollectionExtended<USinger> Singers { get; set; } = [];
    [Reactive] public bool IsLoading { get; set; } = true;

    /// <summary>选中歌手命令：触发关闭并返回该歌手。</summary>
    public ReactiveCommand<USinger, Unit> SelectSingerCommand { get; }

    /// <summary>取消命令：返回 null。</summary>
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    public SingerPickerViewModel()
    {
        SelectSingerCommand = ReactiveCommand.Create<USinger>(singer =>
        {
            if (SingerManager.Inst.Singers.TryGetValue(singer.Id, out USinger? current)) RaiseClose(current);
        });

        CancelCommand = ReactiveCommand.Create(() => { RaiseClose(null); });

        Services.SingerPlugins.SingerDiscoveryService.Instance.Changed += LoadSingers;
        LoadSingers();
    }

    /// <summary>在 UI 线程读取已完成的目录，避免后台枚举可变字典。</summary>
    private void LoadSingers()
    {
        Singers.Load(SingerManager.Inst.SingerGroups.Values.SelectMany(group => group));
        IsLoading = false;
    }

    public void Dispose()
    {
        Services.SingerPlugins.SingerDiscoveryService.Instance.Changed -= LoadSingers;
        SelectSingerCommand.Dispose(); CancelCommand.Dispose();
    }

    public override void RequestBack()
    {
        RaiseClose(null);
    }
}