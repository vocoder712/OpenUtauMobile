using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Interactivity;
using System;
using DialogHostAvalonia;
using OpenUtauMobile.Controls;
using OpenUtauMobile.Services;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        TopLevel? topLevel = AppService.GetTopLevel();
        ConfigureAndroidSystemBars(topLevel);
        topLevel?.BackRequested += OnBackRequested;
        ToastService.Register(ToastOverlay.ConsumeAsync);
        ErrorDialogService.Register(async vm => { await PopupService.Show<object>(new ErrorDialogPopup(), vm); });
    }

    /// <summary>
    /// 由 Avalonia 统一管理 Android 系统栏，避免原生窗口与 Avalonia 使用不同的输入坐标系。
    /// </summary>
    private static void ConfigureAndroidSystemBars(TopLevel? topLevel)
    {
        if (!OperatingSystem.IsAndroid())
        {
            return;
        }

        IInsetsManager? insetsManager = topLevel?.InsetsManager;
        if (insetsManager != null)
        {
            insetsManager.IsSystemBarVisible = false;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        TopLevel? topLevel = AppService.GetTopLevel();
        topLevel?.BackRequested -= OnBackRequested;
        ToastService.Unregister();
        ErrorDialogService.Unregister();
    }

    private void OnBackRequested(object? sender, RoutedEventArgs e)
    {
        if (DialogHost.IsDialogOpen(null))
        {
            DialogSession? session = DialogHost.GetDialogSession(null);
            if (session != null)
            {
                IPopupContext? popup = session.Content switch
                {
                    IPopupContext context => context, // 使用ViewLocator解析的Dialog会直接是IDialogContext
                    ContentControl { DataContext: IPopupContext pvm } => pvm, // 手动绑定
                    _ => null
                };
                popup?.RequestBack();
            }

            e.Handled = true;
            return;
        }

        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        vm.OnBackRequested();
        e.Handled = true;
    }
}
