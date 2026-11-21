using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using OpenUtauMobile.ViewModels;
using OpenUtauMobile.Views;
using Serilog;

namespace OpenUtauMobile;

public class ViewLocator : IDataTemplate
{
    // 显式视图工厂让 AOT 编译器看到每个页面构造函数，避免 iOS 运行时反射 JIT。
    private static readonly IReadOnlyDictionary<Type, Func<Control>> ViewFactories = new Dictionary<Type, Func<Control>>
    {
        [typeof(AboutViewModel)] = static () => new AboutView(),
        [typeof(DependencyManagerViewModel)] = static () => new DependencyManagerView(),
        [typeof(DiffSingerSetupViewModel)] = static () => new DiffSingerSetupView(),
        [typeof(EditorViewModel)] = static () => new EditorView(),
        [typeof(EnunuSingerSetupViewModel)] = static () => new EnunuSingerSetupView(),
        [typeof(ExportLogsViewModel)] = static () => new ExportLogsView(),
        [typeof(ExpressionGraphEditorViewModel)] = static () => new ExpressionGraphEditorView(),
        [typeof(ExpressionGraphLibraryViewModel)] = static () => new ExpressionGraphLibraryView(),
        [typeof(HomeViewModel)] = static () => new HomeView(),
        [typeof(NeutrinoSingerSetupViewModel)] = static () => new NeutrinoSingerSetupView(),
        [typeof(OptionsViewModel)] = static () => new OptionsView(),
        [typeof(PluginDetailsViewModel)] = static () => new PluginDetailsView(),
        [typeof(PluginManagerViewModel)] = static () => new PluginManagerView(),
        [typeof(PluginSingerSetupViewModel)] = static () => new PluginSingerSetupView(),
        [typeof(PluginWebSettingsViewModel)] = static () => new PluginWebSettingsView(),
        [typeof(ResamplerManagerViewModel)] = static () => new ResamplerManagerView(),
        [typeof(SandboxFileManagerViewModel)] = static () => new SandboxFileManagerView(),
        [typeof(SettingsViewModel)] = static () => new SettingsView(),
        [typeof(SingerDetailViewModel)] = static () => new SingerDetailView(),
        [typeof(SingerManagementViewModel)] = static () => new SingerManagementView(),
        [typeof(SingleFileSingerSetupViewModel)] = static () => new SingleFileSingerSetupView(),
        [typeof(SplashScreenViewModel)] = static () => new SplashScreenView(),
        [typeof(UtauSingerSetupViewModel)] = static () => new UtauSingerSetupView(),
    };

    public Control? Build(object? data)
    {
        if (data is null)
        {
            return null;
        }

        Type viewModelType = data.GetType();
        if (ViewFactories.TryGetValue(viewModelType, out Func<Control>? createView))
        {
            try
            {
                return createView();
            }
            catch (Exception exception)
            {
                Log.Error(exception, "创建视图 {ViewModelType} 失败", viewModelType.FullName);
                return new TextBlock
                {
                    Text = $"Failed to create view: {viewModelType.FullName}\n{exception.GetBaseException().Message}"
                };
            }
        }

        return new TextBlock { Text = "Not Found: " + viewModelType.FullName };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
