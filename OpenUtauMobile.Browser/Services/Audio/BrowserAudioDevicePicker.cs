using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace OpenUtauMobile.Browser.Services.Audio;

/// <summary>通过用户点击请求浏览器设备授权，并展示切换成功后的实际路由。</summary>
internal sealed class BrowserAudioDevicePicker : Button
{
    /// <summary>复用设置页按钮样式，在控件存活期间监听系统设备变化。</summary>
    public BrowserAudioDevicePicker(Action onRouteChanged)
    {
        this.Bind(ContentProperty, this.GetResourceObservable("Settings.Render.OutputDevice"));
        Classes.Add("filled");
        Click += SelectOutput;
        Loaded += (_, _) => BrowserAudioInterop.DevicesChanged += onRouteChanged;
        Unloaded += (_, _) => BrowserAudioInterop.DevicesChanged -= onRouteChanged;
    }

    /// <summary>直接从点击事件调用浏览器，保留系统选择器要求的用户激活。</summary>
    private async void SelectOutput(object? sender, RoutedEventArgs args)
    {
        try
        {
            IsEnabled = false;
            await BrowserAudioInterop.SelectOutputAsync();
        }
        catch (Exception exception) { BrowserAudioInterop.ReportError(0, exception.Message); }
        finally { IsEnabled = true; }
    }
}
