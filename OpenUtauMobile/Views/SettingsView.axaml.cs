using Avalonia.Controls;
using OpenUtauMobile.ViewModels;
using OpenUtauMobile.Services;
using Avalonia.Interactivity;

namespace OpenUtauMobile.Views;

public partial class SettingsView : UserControl
{
    public bool PersistentNavigation { get; set; }
    public SettingsView()
    {
        InitializeComponent();

        // 订阅控件尺寸变化，实现侧栏自适应展开/收起
        SizeChanged += OnSizeChanged;
        Loaded += (_, _) =>
        {
            if (DataContext is SettingsViewModel vm)
            {
                _ = vm.InitializeAsync();
            }
        };
    }

    /// <summary>设置分类模板加载后，创建属于当前模板实例的系统设备控件。</summary>
    private void OnAudioDevicePickerLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is ContentControl { Content: null } host)
        {
            host.Content = ServiceHub.AudioDevicePickerFactory?.Invoke(RefreshAudioRoute);
        }
    }

    /// <summary>系统路由变化后同步设置页中的实际输出设备。</summary>
    private void RefreshAudioRoute()
    {
        if (DataContext is SettingsViewModel vm && vm.ArePreferencesInitialized)
        {
            _ = vm.RefreshAudioDevicesAsync();
        }
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
