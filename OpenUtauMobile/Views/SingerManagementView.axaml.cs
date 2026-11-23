using Avalonia.Controls;

namespace OpenUtauMobile.Views;

public partial class SingerManagementView : UserControl
{
    public SingerManagementView()
    {
        InitializeComponent();
        InstallerMenu.ActionInvoked += async action =>
        {
            if (DataContext is ViewModels.SingerManagementViewModel model) await model.InvokeInstallerAsync(action.Id);
        };
    }
}