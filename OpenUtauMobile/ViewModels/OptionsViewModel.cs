using System.Reactive;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services;
using ReactiveUI;

namespace OpenUtauMobile.ViewModels;

public class OptionsViewModel : NavigateViewModelBase
{
    public ReactiveCommand<Unit, Unit> BackCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenSettingsCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenDependencyManagerCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenHelpCommand { get; }
    public ReactiveCommand<Unit, Unit> ExportLogCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenAboutCommand { get; }

    public OptionsViewModel(MainViewModel navigator) : base(navigator)
    {
        BackCommand = ReactiveCommand.Create(OnBack);
        OpenSettingsCommand = ReactiveCommand.Create(OnOpenSettings);
        OpenDependencyManagerCommand = ReactiveCommand.Create(OnOpenDependencyManager);
        OpenHelpCommand = ReactiveCommand.Create(OnOpenHelp);
        ExportLogCommand = ReactiveCommand.Create(OnExportLog);
        OpenAboutCommand = ReactiveCommand.Create(OnOpenAbout);
    }

    private void OnBack()
    {
        Navigator.NavigateBack(this);
    }

    private void OnOpenSettings()
    {
        Navigator.Navigate(new SettingsViewModel(Navigator));
    }

    private void OnOpenDependencyManager()
    {
        Navigator.Navigate(new DependencyManagerViewModel(Navigator));
    }

    private void OnOpenHelp()
    {
        // TODO: Navigator.Navigate(new HelpViewModel(Navigator));
        ToastService.Enqueue(L.S("Options.Toast.HelpNotImpl"));
    }

    private void OnExportLog()
    {
        // TODO: 导出日志
        ToastService.Enqueue(L.S("Options.Toast.ExportLogNotImpl"));
    }

    private void OnOpenAbout()
    {
        Navigator.Navigate(new AboutViewModel(Navigator));
    }
}