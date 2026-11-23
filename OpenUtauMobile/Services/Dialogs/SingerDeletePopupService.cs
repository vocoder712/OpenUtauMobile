using System;
using System.Threading.Tasks;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services.SingerPlugins;
using OpenUtauMobile.ViewModels;
using Serilog;

namespace OpenUtauMobile.Services.Dialogs;

/// <summary>内置和插件歌手共用卸载确认、加载弹窗和结果提示。</summary>
public static class SingerDeletePopupService
{
    /// <summary>按歌手归属选择后端，提供方缺失时不能回退到内置递归卸载。</summary>
    public static Task<bool> ShowAsync(USinger singer) => ShowAsync(singer.LocalizedName, () =>
        BuiltinSingerMaintenance.CanUninstall(singer) ? BuiltinSingerMaintenance.UninstallAsync(singer)
            : SingerDeletionService.DeleteAsync(SingerDeletionService.Find(singer)
                ?? throw new InvalidOperationException("歌手条目已改变，请刷新列表。")));

    /// <summary>没有可用歌手实例的候选也沿用同一卸载交互。</summary>
    public static Task<bool> ShowAsync(SingerCandidate candidate) =>
        ShowAsync(candidate.Entry.DisplayName, () => SingerDeletionService.DeleteAsync(candidate));

    private static async Task<bool> ShowAsync(string name, Func<Task> uninstall)
    {
        string? choice = await OptionConfirmPopupService.ShowAsync(L.S("SingerDetail.UninstallConfirmTitle"),
            string.Format(L.S("SingerDetail.UninstallConfirmMessage"), name),
            [new OptionConfirmOption(L.S("Common.Cancel"), "cancel", isDefault: true), new(L.S("Common.Uninstall"), "uninstall", isDestructive: true)]);
        if (choice != "uninstall") return false;
        try
        {
            await LoadingPopupService.RunAsync(L.S("SingerDetail.Uninstalling"), _ => uninstall());
            ToastService.Enqueue(string.Format(L.S("SingerDetail.UninstallSucceeded"), name));
            return true;
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Failed to uninstall singer {Singer}", name);
            ErrorDialogService.Show(new ErrorDialogViewModel(new ErrorMessageNotification(
                string.Format(L.S("SingerDetail.UninstallFailed"), name), exception)));
            return false;
        }
    }
}
