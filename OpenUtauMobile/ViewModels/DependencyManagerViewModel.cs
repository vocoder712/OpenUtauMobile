using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using OpenUtau.Core;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtauMobile.ViewModels;

/// <summary>
/// 依赖项管理器 ViewModel
/// 管理 OpenUtau 依赖包的安装、卸载和查看
/// </summary>
public class DependencyManagerViewModel : NavigateViewModelBase
{
    // ═══════════════════════════════════════════════════
    //  Observable Properties
    // ═══════════════════════════════════════════════════

    /// <summary>可用的依赖包列表（从远程 registry 获取）</summary>
    [Reactive]
    public ObservableCollection<DependencyItemViewModel> AvailablePackages { get; set; } = new();

    /// <summary>已安装的依赖包列表</summary>
    [Reactive]
    public ObservableCollection<InstalledDependencyViewModel> InstalledPackages { get; set; } = new();

    /// <summary>当前选中的Tab: 0=可用, 1=已安装</summary>
    [Reactive]
    public int SelectedTabIndex { get; set; } = 0;

    /// <summary>是否正在加载远程数据</summary>
    [Reactive]
    public bool IsLoadingRegistry { get; set; }

    /// <summary>是否正在加载本地已安装列表</summary>
    [Reactive]
    public bool IsLoadingInstalled { get; set; }

    /// <summary>搜索关键词</summary>
    [Reactive]
    public string SearchText { get; set; } = string.Empty;

    /// <summary>排序方式: 0=名称, 1=开发者</summary>
    [Reactive]
    public int SortMode { get; set; } = 0;

    // ═══════════════════════════════════════════════════
    //  Commands
    // ═══════════════════════════════════════════════════

    public ReactiveCommand<Unit, Unit> BackCommand { get; }
    public ReactiveCommand<Unit, Unit> RefreshRegistryCommand { get; }
    public ReactiveCommand<Unit, Unit> RefreshInstalledCommand { get; }
    public ReactiveCommand<Unit, Unit> InstallFromFileCommand { get; }

    // ═══════════════════════════════════════════════════
    //  Constructor
    // ═══════════════════════════════════════════════════

    public DependencyManagerViewModel(MainViewModel navigator) : base(navigator)
    {
        BackCommand = ReactiveCommand.Create(OnBack);
        RefreshRegistryCommand = ReactiveCommand.CreateFromTask(LoadAvailablePackagesAsync);
        RefreshInstalledCommand = ReactiveCommand.CreateFromTask(LoadInstalledPackagesAsync);
        InstallFromFileCommand = ReactiveCommand.CreateFromTask(OnInstallFromFileAsync);

        // 自动加载数据
        Task.Run(async () =>
        {
            await LoadAvailablePackagesAsync();
            await LoadInstalledPackagesAsync();
        });

        // TODO: 实现搜索和排序功能
        // 监听 SearchText 和 SortMode 变化，自动过滤和排序列表
        // this.WhenAnyValue(x => x.SearchText, x => x.SortMode)
        //     .Throttle(TimeSpan.FromMilliseconds(300))
        //     .Subscribe(_ => ApplyFilterAndSort());
    }

    // ═══════════════════════════════════════════════════
    //  Command Handlers
    // ═══════════════════════════════════════════════════

    private void OnBack()
    {
        Navigator.NavigateBack(this);
    }

    /// <summary>从远程 registry 加载可用包列表</summary>
    private async Task LoadAvailablePackagesAsync()
    {
        IsLoadingRegistry = true;
        try
        {
            var packages = await PackageManager.Inst.FetchRegistryAsync();

            // 清空并重新填充
            AvailablePackages.Clear();
            foreach (var pkg in packages)
            {
                AvailablePackages.Add(new DependencyItemViewModel(pkg, this));
            }

            // 刷新已安装状态
            UpdateInstalledStatus();

            ToastService.Enqueue(string.Format(L.S("DependencyManager.Toast.Loaded"), packages.Count));
        }
        catch (Exception ex)
        {
            ToastService.Enqueue(string.Format(L.S("DependencyManager.Toast.LoadFailed"), ex.Message));
        }
        finally
        {
            IsLoadingRegistry = false;
        }
    }

    /// <summary>加载已安装的依赖包列表</summary>
    public async Task LoadInstalledPackagesAsync()
    {
        IsLoadingInstalled = true;
        try
        {
            var installed = await PackageManager.Inst.GetInstalledAsync();

            // 清空并重新填充
            InstalledPackages.Clear();
            foreach (var meta in installed)
            {
                InstalledPackages.Add(new InstalledDependencyViewModel(meta, this));
            }

            // 更新可用包的已安装状态
            UpdateInstalledStatus();
        }
        catch (Exception ex)
        {
            ToastService.Enqueue(string.Format(L.S("DependencyManager.Toast.InstalledListFailed"), ex.Message));
        }
        finally
        {
            IsLoadingInstalled = false;
        }
    }

    /// <summary>更新可用包的已安装状态</summary>
    private void UpdateInstalledStatus()
    {
        foreach (var pkg in AvailablePackages)
        {
            pkg.UpdateInstalledStatus(InstalledPackages);
        }
    }

    /// <summary>从本地文件安装依赖包</summary>
    private Task OnInstallFromFileAsync()
    {
        // TODO: 实现文件选择器
        // 1. 打开文件选择器,选择 .oudep 文件
        // 2. 调用 PackageManager.Inst.InstallFromFileAsync(filePath)
        // 3. 安装完成后刷新已安装列表
        ToastService.Enqueue(L.S("DependencyManager.Toast.InstallFromFileNotImpl"));
        return Task.CompletedTask;
    }

    /// <summary>卸载指定的依赖包</summary>
    public async Task UninstallAsync(string id)
    {
        try
        {
            await PackageManager.Inst.UninstallAsync(id);
            ToastService.Enqueue(string.Format(L.S("DependencyManager.Toast.Uninstalled"), id));
            await LoadInstalledPackagesAsync();
        }
        catch (Exception ex)
        {
            ToastService.Enqueue(string.Format(L.S("DependencyManager.Toast.UninstallFailed"), ex.Message));
        }
    }
}

// ═══════════════════════════════════════════════════
//  子 ViewModel: 单个可用依赖包
// ═══════════════════════════════════════════════════

public class DependencyItemViewModel : ReactiveObject
{
    private readonly RegistrySoftware _software;
    private readonly DependencyManagerViewModel _parent;

    public string Id => _software.id;
    public string Name => _software.LocalizedName();

    public string Developers => _software.developers != null && _software.developers.Length > 0
        ? string.Join(", ", _software.developers)
        : L.S("DependencyManager.UnknownDeveloper");

    public string Category => _software.category;
    public string LatestVersion => PackageManager.GetLatestVersionString(_software.versions);
    public string HomepageUrl => _software.homepage_url;
    public string[] Tags => _software.tags;

    /// <summary>是否正在安装</summary>
    [Reactive]
    public bool IsInstalling { get; set; }

    /// <summary>安装进度 (0-100)</summary>
    [Reactive]
    public int InstallProgress { get; set; }

    /// <summary>是否已安装</summary>
    [Reactive]
    public bool IsInstalled { get; set; }

    /// <summary>已安装的版本号</summary>
    [Reactive]
    public string InstalledVersion { get; set; } = string.Empty;

    /// <summary>是否有可用更新（已安装版本低于最新版本）</summary>
    [Reactive]
    public bool HasUpdate { get; set; }

    /// <summary>按钮文本</summary>
    public string ButtonText
    {
        get
        {
            if (IsInstalling) return L.S("DependencyManager.Installing");
            if (HasUpdate) return L.S("Common.Update");
            if (IsInstalled) return L.S("Common.Installed");
            return L.S("Common.Install");
        }
    }

    public ReactiveCommand<Unit, Unit> InstallCommand { get; }

    public DependencyItemViewModel(RegistrySoftware software, DependencyManagerViewModel parent)
    {
        _software = software;
        _parent = parent;

        InstallCommand = ReactiveCommand.CreateFromTask(OnInstallAsync,
            this.WhenAnyValue(x => x.IsInstalled).Select(installed => !installed || HasUpdate));

        // 监听状态变化，触发ButtonText更新
        this.WhenAnyValue(x => x.IsInstalling, x => x.IsInstalled, x => x.HasUpdate)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(ButtonText)));
    }

    /// <summary>更新已安装状态</summary>
    public void UpdateInstalledStatus(ObservableCollection<InstalledDependencyViewModel> installedPackages)
    {
        var installed = installedPackages.FirstOrDefault(p => p.Id == Id);
        IsInstalled = installed != null;
        InstalledVersion = installed?.Version ?? string.Empty;

        // 检查是否有更新：已安装且远程版本号更高
        if (IsInstalled && !string.IsNullOrEmpty(InstalledVersion) && !string.IsNullOrEmpty(LatestVersion))
        {
            if (Version.TryParse(InstalledVersion, out var installedVer) &&
                Version.TryParse(LatestVersion, out var latestVer))
            {
                HasUpdate = latestVer > installedVer;
            }
            else
            {
                // 如果无法解析为版本号，则按字符串比较
                HasUpdate = string.CompareOrdinal(LatestVersion, InstalledVersion) > 0;
            }
        }
        else
        {
            HasUpdate = false;
        }
    }

    private async Task OnInstallAsync()
    {
        IsInstalling = true;
        InstallProgress = 0;

        try
        {
            var progress = new Progress<int>(percent => { InstallProgress = percent; });

            await PackageManager.Inst.InstallAsync(_software, progress);
            ToastService.Enqueue(string.Format(L.S("DependencyManager.Toast.InstallSuccess"), Name));

            // 刷新已安装列表
            await _parent.LoadInstalledPackagesAsync();

            // 更新自己的状态
            UpdateInstalledStatus(_parent.InstalledPackages);
        }
        catch (Exception ex)
        {
            ToastService.Enqueue(string.Format(L.S("DependencyManager.Toast.InstallFailed"), ex.Message));
        }
        finally
        {
            IsInstalling = false;
            InstallProgress = 0;
        }
    }
}

// ═══════════════════════════════════════════════════
//  子 ViewModel: 单个已安装依赖包
// ═══════════════════════════════════════════════════

public class InstalledDependencyViewModel : ReactiveObject
{
    private readonly OudepMetadata _metadata;
    private readonly DependencyManagerViewModel _parent;

    public string Id => _metadata.id;
    public string Version => _metadata.version;
    public string Description => _metadata.description;
    public string Class => _metadata.@class;

    [Reactive] public bool IsUninstalling { get; set; }

    public ReactiveCommand<Unit, Unit> UninstallCommand { get; }

    public InstalledDependencyViewModel(OudepMetadata metadata, DependencyManagerViewModel parent)
    {
        _metadata = metadata;
        _parent = parent;

        UninstallCommand = ReactiveCommand.CreateFromTask(OnUninstallAsync);
    }

    private async Task OnUninstallAsync()
    {
        IsUninstalling = true;
        try
        {
            await _parent.UninstallAsync(Id);
        }
        finally
        {
            IsUninstalling = false;
        }
    }
}