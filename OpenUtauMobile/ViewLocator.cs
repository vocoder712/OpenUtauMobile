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
        [typeof(ClassicSingerSetupViewModel)] = static () => new ClassicSingerSetupView(),
        [typeof(DependencyManagerViewModel)] = static () => new DependencyManagerView(),
        [typeof(EditorViewModel)] = static () => new EditorView(),
        [typeof(ExportLogsViewModel)] = static () => new ExportLogsView(),
        [typeof(ExpressionGraphEditorViewModel)] = static () => new ExpressionGraphEditorView(),
        [typeof(ExpressionGraphLibraryViewModel)] = static () => new ExpressionGraphLibraryView(),
        [typeof(HomeViewModel)] = static () => new HomeView(),
        [typeof(OptionsViewModel)] = static () => new OptionsView(),
        [typeof(PluginManagerViewModel)] = static () => new PluginManagerView(),
        [typeof(SandboxFileManagerViewModel)] = static () => new SandboxFileManagerView(),
        [typeof(SettingsViewModel)] = static () => new SettingsView(),
        [typeof(SingerDetailViewModel)] = static () => new SingerDetailView(),
        [typeof(SingerManagementViewModel)] = static () => new SingerManagementView(),
        [typeof(SplashScreenViewModel)] = static () => new SplashScreenView(),
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
