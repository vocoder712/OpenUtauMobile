using Avalonia.Controls;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Views;

public partial class SettingsView : UserControl
{
    public bool PersistentNavigation { get; set; }
    public SettingsView()
    {
        InitializeComponent();

        // 订阅控件尺寸变化，实现侧栏自适应展开/收起
        SizeChanged += OnSizeChanged;
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            bool shouldExpand = PersistentNavigation || e.NewSize.Width >= ViewConstants.SettingsSidebarBreakpoint;
            if (vm.IsNavExpanded != shouldExpand)
            {
                vm.IsNavExpanded = shouldExpand;
            }
        }
    }
}