using System.Collections.Generic;
using System.Reactive;
using System.Threading.Tasks;
using DynamicData.Binding;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services;
using OpenUtauMobile.Storage;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtauMobile.ViewModels;

public class SingerManagementViewModel : NavigateViewModelBase
{
    public ReactiveCommand<Unit, Unit> BackCommand { get; set; }

    /// <summary>
    /// 打开文件选择器选择歌手安装包，然后导航到对应的安装向导。
    /// </summary>
    public ReactiveCommand<Unit, Unit> AddSingerCommand { get; set; }

    /// <summary>
    /// Navigates to singer detail page when a card is tapped.
    /// </summary>
    public ReactiveCommand<USinger, Unit> OpenSingerCommand { get; set; }

    public SingerManagementViewModel(MainViewModel mainViewModel) : base(mainViewModel)
    {
        BackCommand = ReactiveCommand.Create(OnBack);
        AddSingerCommand = ReactiveCommand.Create(OnAddSinger);
        OpenSingerCommand = ReactiveCommand.Create<USinger>(OnOpenSinger);
    }

    private void OnBack()
    {
        Navigator.NavigateBack(this);
    }

    private async void OnAddSinger()
    {
        // 打开文件选择器，支持多种压缩包格式
        string[] filters = new[] { "*.zip", "*.rar", "*.uar", "*.vogen" };
        string filePath = await FilePicker.PickSingleFileAsync(L.S("FilePicker.SelectSingerFile"), filters);

        if (string.IsNullOrEmpty(filePath))
        {
            return; // 用户取消了选择
        }

        // 根据文件扩展名决定导航到哪个ViewModel
        string extension = System.IO.Path.GetExtension(filePath).ToLowerInvariant();

        if (extension == ".vogen")
        {
            // TODO: 导航到 VogenSingerSetupViewModel
            ToastService.Enqueue(L.S("SingerManagement.VogenNotImplemented"));
        }
        else if (extension == ".zip" || extension == ".rar" || extension == ".uar")
        {
            // 导航到 Classic 歌手安装向导
            ClassicSingerSetupViewModel setupVM = new ClassicSingerSetupViewModel(Navigator);
            setupVM.ArchiveFilePath = filePath;
            Navigator.Navigate(setupVM);
        }
    }

    private void OnOpenSinger(USinger singer)
    {
        Navigator.Navigate(new SingerDetailViewModel(Navigator, singer));
    }

    [Reactive] public ObservableCollectionExtended<USinger> Singers { get; set; } = [];

    public override void OnNavigatedTo()
    {
        _ = RefreshSingersAsync();
    }

    private async Task RefreshSingersAsync()
    {
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
    }
}