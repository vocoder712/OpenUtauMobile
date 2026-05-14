using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
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
        topLevel?.BackRequested += OnBackRequested;
        ToastService.Register(ToastOverlay.ConsumeAsync);
        ErrorDialogService.Register(async vm => { await PopupService.Show<object>(new ErrorDialogPopup(), vm); });
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