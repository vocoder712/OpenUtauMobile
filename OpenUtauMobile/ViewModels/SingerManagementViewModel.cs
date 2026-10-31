using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using DynamicData.Binding;
using IconPacks.Avalonia.PhosphorIcons;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Controls;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services;
using OpenUtauMobile.Services.Dialogs;
using OpenUtauMobile.Services.SingerPlugins;
using OpenUtauMobile.Storage;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtauMobile.ViewModels
{
    /// <summary>歌手管理及显式安装器路由，不根据文件内容选择安装器。</summary>
    public sealed class SingerManagementViewModel : NavigateViewModelBase, IDisposable
    {
        private const string moreId = "host:more-installers";
        private bool selecting;
        private readonly SemaphoreSlim refreshGate = new(1, 1);
        private readonly CancellationTokenSource lifetime = new();
        private bool disposed;
        public ReactiveCommand<Unit, Unit> BackCommand { get; }
        public ReactiveCommand<Unit, Unit> AddSingerCommand { get; }
        public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
        [Reactive] public bool IsRefreshing { get; private set; }
        public ReactiveCommand<USinger, Unit> OpenSingerCommand { get; }
        public ObservableCollectionExtended<USinger> Singers { get; } = [];
        public ObservableCollectionExtended<SingerCandidate> UnavailableCandidates { get; } = [];
        [Reactive] public IReadOnlyList<FabMenuAction> InstallerActions { get; private set; } = [];
        [Reactive] public bool IsMenuExpanded { get; set; }

        public SingerManagementViewModel(MainViewModel navigator) : base(navigator)
        {
            BackCommand = ReactiveCommand.Create(() => Navigator.NavigateBack(this));
            AddSingerCommand = ReactiveCommand.CreateFromTask(() => ChooseInstallerAsync());
            RefreshCommand = ReactiveCommand.CreateFromTask(ReloadSingersAsync, this.WhenAnyValue(item => item.IsRefreshing).Select(value => !value));
            OpenSingerCommand = ReactiveCommand.Create<USinger>(singer => Navigator.Navigate(new SingerDetailViewModel(Navigator, singer)));
            SingerDiscoveryService.Instance.Changed += RefreshSnapshot;
            RefreshSnapshot();
        }

        /// <summary>菜单只合并展示信息，内置业务不通过插件契约调用。</summary>
        private sealed record InstallChoice(string Id, string DisplayName, ImmutableArray<string> Extensions, string? IconKey = null);

        private static InstallChoice[] Installers() =>
        [
            .. BuiltinSingerInstallers.All.Select(item => new InstallChoice(item.Id, item.DisplayName, item.Extensions)),
            .. SingerPluginService.Registry.Snapshot.Installers.Values
                .OrderBy(item => item.Descriptor.DisplayName, StringComparer.CurrentCulture)
                .ThenBy(item => item.Descriptor.Id, StringComparer.Ordinal)
                .Select(item => new InstallChoice(item.Descriptor.Id, item.Descriptor.DisplayName,
                    item.Descriptor.Extensions, item.Descriptor.IconKey)),
        ];

        /// <summary>仅识别宿主能接收的拖放文件后缀，不选择安装器。</summary>
        public static bool IsInstallPackagePath(string path) => Installers().Any(installer =>
            installer.Extensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)));

        public override void OnNavigatedTo() => _ = ReloadSingersAsync();

        /// <summary>每次返回均排队重新扫描；手动刷新复用同一入口，不只重放旧快照。</summary>
        private async Task ReloadSingersAsync()
        {
            try
            {
                await refreshGate.WaitAsync(lifetime.Token);
                try
                {
                    IsRefreshing = true;
                    await SingerDiscoveryService.Instance.RefreshAsync(lifetime.Token);
                    if (!disposed) RefreshSnapshot();
                }
                finally
                {
                    IsRefreshing = false;
                    refreshGate.Release();
                }
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception exception) { if (!disposed) ShowError(exception); }
        }

        private void RefreshSnapshot()
        {
            Singers.Load(SingerManager.Inst.SingerGroups.Values.SelectMany(group => group));
            UnavailableCandidates.Load(SingerDiscoveryService.Instance.Candidates.Where(item => item.Entry.Availability != Plugin.Abstractions.SingerAvailability.Ready));
            InstallChoice[] installers = Installers();
            List<FabMenuAction> actions = [.. installers.Take(installers.Length > 6 ? 5 : 6).Select(item =>
                new FabMenuAction(item.Id, item.DisplayName,
                    Enum.TryParse(item.IconKey, out PackIconPhosphorIconsKind icon) && Enum.IsDefined(icon)
                        ? icon : PackIconPhosphorIconsKind.DownloadSimple))];
            if (installers.Length > 6) actions.Add(new(moreId, L.S("SingerSetup.MoreInstallers"), PackIconPhosphorIconsKind.DotsThree));
            InstallerActions = actions;
        }

        /// <summary>菜单直接指定安装器；更多项复用现有搜索列表。</summary>
        public Task InvokeInstallerAsync(string id) => id == moreId ? ChooseInstallerAsync(overflowOnly: true) : InstallSelectedAsync(id);

        /// <summary>桌面拖放也必须手动选择，不按后缀自动挑选安装器。</summary>
        public async Task ChooseInstallerAsync(string? sourcePath = null, bool overflowOnly = false)
        {
            if (selecting) return;
            selecting = true;
            try
            {
                InstallChoice[] options = overflowOnly ? [.. Installers().Skip(5)] : Installers();
                if (options.Length == 0) return;
                string? selected = null;
                using GraphItemPickerViewModel picker = new(L.S("SingerSetup.SelectInstaller"), "",
                    [.. options.Select(item => new GraphPickerItem(item.Id, item.DisplayName,
                        string.Join(", ", item.Extensions)))], item => { selected = item.Key; return null; },
                    searchHint: L.S("SingerSetup.SearchInstaller"));
                if (await PopupService.Show<bool>(new GraphItemPickerPopup(), picker) == true && selected != null)
                    await OpenInstallerAsync(selected, sourcePath);
            }
            catch (Exception exception) { ShowError(exception); }
            finally { selecting = false; }
        }

        private async Task InstallSelectedAsync(string id)
        {
            if (selecting) return;
            selecting = true;
            try { await OpenInstallerAsync(id, null); }
            catch (Exception exception) { ShowError(exception); }
            finally { selecting = false; }
        }

        private async Task OpenInstallerAsync(string id, string? sourcePath)
        {
            InstallChoice? choice = Installers().FirstOrDefault(item => item.Id == id);
            if (choice == null) return;
            string path = sourcePath ?? await FilePicker.PickSingleFileAsync(L.S("FilePicker.SelectSingerFile"),
                [.. choice.Extensions.Select(extension => "*" + extension)]);
            if (string.IsNullOrEmpty(path)) return;
            BuiltinSingerInstaller? builtin = BuiltinSingerInstallers.All.FirstOrDefault(item => item.Id == id);
            if (builtin != null)
            {
                NavigateViewModelBase setup = id switch
                {
                    "builtin:utau" => new UtauSingerSetupViewModel(Navigator, path),
                    "builtin:diffsinger" => new DiffSingerSetupViewModel(Navigator, path),
                    "builtin:enunu" => new EnunuSingerSetupViewModel(Navigator, path),
                    _ => new SingleFileSingerSetupViewModel(Navigator, builtin, path),
                };
                Navigator.Navigate(setup);
            }
            else if (SingerPluginService.Registry.Snapshot.Installers.TryGetValue(id, out RegisteredSingerInstaller? plugin))
            {
                Navigator.Navigate(new PluginSingerSetupViewModel(Navigator, plugin, path));
            }
        }

        private static void ShowError(Exception exception) => ErrorDialogService.Show(new ErrorDialogViewModel(new ErrorMessageNotification(exception)));
        public override void OnBackRequested()
        {
            if (IsMenuExpanded) IsMenuExpanded = false;
            else base.OnBackRequested();
        }
        public void Dispose()
        {
            disposed = true;
            lifetime.Cancel();
            SingerDiscoveryService.Instance.Changed -= RefreshSnapshot;
            BackCommand.Dispose(); AddSingerCommand.Dispose(); RefreshCommand.Dispose(); OpenSingerCommand.Dispose();
        }
    }
}
