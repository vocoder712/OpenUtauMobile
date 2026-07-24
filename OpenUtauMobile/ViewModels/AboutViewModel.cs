using System.Linq;
using System.Reactive;
using System.Reflection;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services;
using ReactiveUI;

namespace OpenUtauMobile.ViewModels;

public class AboutViewModel : NavigateViewModelBase
{
    public ReactiveCommand<Unit, Unit> BackCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenHomepageCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenLicenseCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenCreditsCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenFeedbackCommand { get; }
    public string Version { get; }
    public string CoreVersion { get; }
    public string BuildNumber { get; }
    public string BuildTimestamp { get; }
    public string BuildMachine { get; }
    public string SourceCommit { get; }
    public string ActionRunId { get; }

    public AboutViewModel(MainViewModel navigator) : base(navigator)
    {
        BackCommand = ReactiveCommand.Create(OnBack);
        OpenHomepageCommand = ReactiveCommand.Create(() => OpenUrl("https://github.com/vocoder712/OpenUtauMobile"));
        OpenLicenseCommand = ReactiveCommand.Create(() => ToastService.Enqueue(L.S("About.Toast.LicenseNotImpl")));
        OpenCreditsCommand = ReactiveCommand.Create(() => ToastService.Enqueue(L.S("About.Toast.CreditsNotImpl")));
        OpenFeedbackCommand =
            ReactiveCommand.Create(() => OpenUrl("https://github.com/vocoder712/OpenUtauMobile/issues"));

        // 使用 typeof(...).Assembly 替代 GetEntryAssembly()，
        // 因为在 Android 等平台上 GetEntryAssembly() 可能无法正确识别入口程序集
        Assembly assembly = typeof(AboutViewModel).Assembly;

        Version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? L.S("Common.Unknown");

        AssemblyMetadataAttribute[] metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToArray();
        CoreVersion = GetMetadata(metadata, "CoreVersion");
        BuildNumber = GetMetadata(metadata, "BuildNumber");
        BuildTimestamp = GetMetadata(metadata, "BuildTimestamp");
        BuildMachine = GetMetadata(metadata, "BuildMachine");
        SourceCommit = GetMetadata(metadata, "SourceCommit");
        ActionRunId = GetMetadata(metadata, "ActionRunId");
    }

    private static string GetMetadata(AssemblyMetadataAttribute[] metadata, string key)
    {
        string? value = metadata.FirstOrDefault(attribute => attribute.Key == key)?.Value;
        return string.IsNullOrWhiteSpace(value) || value == "Unknown" ? L.S("Common.Unknown") : value;
    }

    private void OnBack()
    {
        Navigator.NavigateBack(this);
    }

    private static void OpenUrl(string url)
    {
        try
        {
            // TODO: 打开外部链接功能尚未实现
            ToastService.Enqueue(L.S("About.Toast.OpenLinkNotImpl"));
        }
        catch
        {
            ToastService.Enqueue(L.S("About.Toast.OpenLinkFailed"));
        }
    }
}
