using CommunityToolkit.Mvvm.ComponentModel;
using DynamicData.Binding;
using OpenUtau.Core;
using OpenUtau.Core.Util;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtauMobile.ViewModels
{
    public partial class DependencyManageViewModel : ReactiveObject
    {
        [Reactive] public ObservableCollectionExtended<DependencyInfo> InstalledDependencies { get; set; } = []; // 已安装的依赖项列表
        [Reactive] public bool IsBusy { get; set; } = false; // 遮罩层状态

        public DependencyManageViewModel()
        {
            Task.Run(RefreshInstalledDependencies);
        }
        /// <summary>
        /// 刷新已安装的依赖项列表
        /// </summary>
        public async Task RefreshInstalledDependencies()
        {
            IsBusy = true;
            try
            {
                var deps = await Task.Run(DependencyManager.Inst.ListInstalled);
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    InstalledDependencies.Clear();
                    InstalledDependencies.AddRange(deps);
                });
            }
            finally
            {
                IsBusy = false;
            }
        }
        /// <summary>
        /// 异步删除依赖项
        /// </summary>
        /// <param name="dependency"></param>
        /// <returns></returns>
        public async Task<bool> RemoveDependencyAsync(DependencyInfo dependency)
        {
            if (dependency == null || string.IsNullOrWhiteSpace(dependency.Name))
            {
                return false;
            }

            IsBusy = true;
            try
            {
                bool result = await Task.Run(() => DependencyManager.Inst.Delete(dependency.Name));
                return result;
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, $"删除依赖失败: {dependency.Name}");
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification("删除依赖失败", ex));
                return false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task InstallDependencyAsync(string archivePath)
        {
            IsBusy = true;
            try
            {
                await Task.Run(() => DependencyInstaller.Install(archivePath));
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, $"安装依赖失败");
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification("安装依赖失败", ex));
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
