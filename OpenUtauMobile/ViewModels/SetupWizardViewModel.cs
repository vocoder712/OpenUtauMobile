using System;
using System.Reactive;
using System.Threading.Tasks;
using OpenUtau.Core.Util;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services;
using OpenUtauMobile.Storage;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtauMobile.ViewModels;

/// <summary>
/// 权限卡片条目。可扩展以支持更多权限类型。
/// </summary>
public class PermissionItem : ViewModelBase
{
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    [Reactive] public bool IsApplicable { get; set; } = true;
    [Reactive] public bool IsGranted { get; set; }
    [Reactive] public string ButtonText { get; set; } = string.Empty;

    public ReactiveCommand<Unit, Unit> GrantCommand { get; init; } = null!;
}

/// <summary>
/// 首次启动设置向导 ViewModel（4 步弹窗）。
/// </summary>
public class SetupWizardViewModel : PopupViewModelBase
{
    [Reactive] public int Step { get; set; } = 0;
    [Reactive] public int StepDisplay { get; set; } = 1;
    [Reactive] public bool IsFirstStep { get; set; } = true;
    [Reactive] public bool IsLastStep { get; set; }

    [Reactive] public bool UseExternalPath { get; set; }
    [Reactive] public string ExternalPath { get; set; } = string.Empty;

    public PermissionItem StoragePermission { get; }

    public ReactiveCommand<Unit, Unit> CloseCommand { get; }
    public ReactiveCommand<Unit, Unit> NextCommand { get; }
    public ReactiveCommand<Unit, Unit> BackCommand { get; }
    public ReactiveCommand<Unit, Unit> SkipCommand { get; }
    public ReactiveCommand<Unit, Unit> FinishCommand { get; }
    public ReactiveCommand<Unit, Unit> SelectBuiltinCommand { get; }
    public ReactiveCommand<Unit, Unit> SelectExternalCommand { get; }
    public ReactiveCommand<Unit, Unit> ChangeFolderCommand { get; }

    public SetupWizardViewModel()
    {
        ExternalPath = Preferences.Default.AdditionalSingerPath;
        UseExternalPath = !string.IsNullOrEmpty(ExternalPath);

        bool isAndroid = OperatingSystem.IsAndroid();
        StoragePermission = new PermissionItem
        {
            Title = L.S("SetupWizard.Step1.Card.Storage"),
            Description = L.S("SetupWizard.Step1.Card.Storage.Desc"),
            IsApplicable = isAndroid,
            IsGranted = isAndroid && HasStoragePermission(),
            ButtonText = isAndroid
                ? (HasStoragePermission() ? L.S("SetupWizard.Step1.PermissionGranted") : L.S("SetupWizard.Step1.Grant"))
                : string.Empty,
            GrantCommand = ReactiveCommand.CreateFromTask(GrantStoragePermissionAsync)
        };

        CloseCommand = ReactiveCommand.Create(RequestBack);
        NextCommand = ReactiveCommand.Create(OnNext);
        BackCommand = ReactiveCommand.Create(OnBack);
        SkipCommand = ReactiveCommand.Create(OnSkip);
        FinishCommand = ReactiveCommand.Create(OnFinish);
        SelectBuiltinCommand = ReactiveCommand.Create(OnSelectBuiltin);
        SelectExternalCommand = ReactiveCommand.Create(OnSelectExternal);
        ChangeFolderCommand = ReactiveCommand.CreateFromTask(ChangeFolderAsync);

        this.WhenAnyValue(x => x.Step)
            .Subscribe(step =>
            {
                StepDisplay = step + 1;
                IsFirstStep = step == 0;
                IsLastStep = step == 3;
            });
    }

    public override void RequestBack()
    {
        RaiseClose(null);
    }

    private void OnNext()
    {
        if (Step < 3) Step++;
    }

    private void OnBack()
    {
        if (Step > 0) Step--;
    }

    private void OnSkip()
    {
        Preferences.Default.SetupWizardCompleted = true;
        Preferences.Save();
        RaiseClose(null);
    }

    private void OnFinish()
    {
        if (UseExternalPath && !string.IsNullOrEmpty(ExternalPath))
            Preferences.Default.AdditionalSingerPath = ExternalPath;
        else if (!UseExternalPath)
            Preferences.Default.AdditionalSingerPath = string.Empty;

        Preferences.Default.SetupWizardCompleted = true;
        Preferences.Save();
        RaiseClose(null);
    }

    private void OnSelectBuiltin()
    {
        UseExternalPath = false;
    }

    private async void OnSelectExternal()
    {
        UseExternalPath = true;
        if (string.IsNullOrEmpty(ExternalPath))
            await SelectFolderAsync();
    }

    private async Task ChangeFolderAsync()
    {
        await SelectFolderAsync();
    }

    private async Task SelectFolderAsync()
    {
        string path = await FilePicker.PickFolderAsync(L.S("FilePicker.SelectSingerDir"));
        if (!string.IsNullOrEmpty(path))
        {
            ExternalPath = path;
            UseExternalPath = true;
        }
        else if (string.IsNullOrEmpty(ExternalPath))
        {
            UseExternalPath = false;
        }
    }

    private static bool HasStoragePermission()
    {
        return ServiceHub.ExternalStorageService?.HasManageExternalStoragePermissionAsync() ?? false;
    }

    private async Task GrantStoragePermissionAsync()
    {
        ServiceHub.ExternalStorageService?.RequestManageExternalStoragePermission();
        await Task.Delay(500);
        for (int i = 0; i < 20; i++)
        {
            if (HasStoragePermission())
            {
                StoragePermission.IsGranted = true;
                StoragePermission.ButtonText = L.S("SetupWizard.Step1.PermissionGranted");
                return;
            }
            await Task.Delay(300);
        }

        if (HasStoragePermission())
        {
            StoragePermission.IsGranted = true;
            StoragePermission.ButtonText = L.S("SetupWizard.Step1.PermissionGranted");
        }
    }
}
