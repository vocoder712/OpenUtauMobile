using System;
using AVFoundation;
using AVKit;
using Avalonia.Controls;
using Avalonia.iOS;
using Avalonia.Platform;
using Avalonia.Threading;
using Foundation;
using UIKit;

namespace OpenUtauMobile.iOS.Services.Audio;

/// <summary>承载 iOS 系统音频路由选择器，并在设备变化时刷新设置页。</summary>
internal sealed class IosAudioRoutePicker : NativeControlHost
{
    private readonly Action onRouteChanged;
    private NSObject? routeChangeObserver;

    /// <summary>创建具有标准触控尺寸的系统设备选择入口。</summary>
    public IosAudioRoutePicker(Action onRouteChanged)
    {
        this.onRouteChanged = onRouteChanged;
        Width = 44;
        Height = 44;
    }

    /// <summary>创建原生控件，并在控件存活期间监听系统路由变化。</summary>
    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        AVRoutePickerView picker = new()
        {
            PrioritizesVideoDevices = false,
            TintColor = UIColor.Label,
            ActiveTintColor = UIColor.SystemBlue,
        };
        routeChangeObserver = AVAudioSession.Notifications.ObserveRouteChange((_, _) =>
            Dispatcher.UIThread.Post(onRouteChanged));
        return new UIViewControlHandle(picker);
    }

    /// <summary>移除系统通知订阅，并交由平台释放原生视图。</summary>
    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        routeChangeObserver?.Dispose();
        routeChangeObserver = null;
        base.DestroyNativeControlCore(control);
    }
}