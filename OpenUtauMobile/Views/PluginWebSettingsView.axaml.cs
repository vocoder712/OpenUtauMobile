using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Views;

/// <summary>延迟创建系统 WebView，离开视觉树时释放浏览器适配器。</summary>
public partial class PluginWebSettingsView : UserControl
{
    private NativeWebView? webView;
    private PluginWebSettingsViewModel? model;

    public PluginWebSettingsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => BindModel();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        BindModel();
    }

    private void BindModel()
    {
        if (model != null) model.PropertyChanged -= ModelChanged;
        model = DataContext as PluginWebSettingsViewModel;
        if (model != null) model.PropertyChanged += ModelChanged;
        UpdateAddress();
    }

    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PluginWebSettingsViewModel.Address)) UpdateAddress();
    }

    private void UpdateAddress()
    {
        if (!this.IsAttachedToVisualTree() || model?.Address == null) return;
        try
        {
            if (webView == null)
            {
                webView = new NativeWebView();
                webView.NavigationCompleted += (_, e) => { if (!e.IsSuccess) model?.ReportWebViewFailure(); };
                WebViewHost.Content = webView;
            }
            webView.Source = model.Address;
        }
        catch (Exception) { model.ReportWebViewFailure(); }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (model != null) model.PropertyChanged -= ModelChanged;
        WebViewHost.Content = null;
        webView = null;
        base.OnDetachedFromVisualTree(e);
    }
}
