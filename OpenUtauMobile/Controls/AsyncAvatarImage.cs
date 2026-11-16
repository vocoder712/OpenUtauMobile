using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using OpenUtauMobile.Services;

namespace OpenUtauMobile.Controls;

/// <summary>后台解码歌手头像，控件只在 UI 线程接收和释放当前图片。</summary>
public sealed class AsyncAvatarImage : Image
{
    public static readonly StyledProperty<byte[]?> DataProperty =
        AvaloniaProperty.Register<AsyncAvatarImage, byte[]?>(nameof(Data));

    private CancellationTokenSource? _loading;
    private Bitmap? _bitmap;
    private bool _attached;

    /// <summary>已经预读到内存的只读头像数据，不触发歌手实例的文件加载。</summary>
    public byte[]? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DataProperty && _attached) RefreshImage();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        RefreshImage();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        ClearImage();
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>替换头像或卸载时取消旧请求，图片解绑后再释放。</summary>
    private void ClearImage()
    {
        _loading?.Cancel();
        _loading = null;
        Source = null;
        _bitmap?.Dispose();
        _bitmap = null;
    }

    /// <summary>只有已挂载的控件才请求解码，等待期间保留宿主的文字头像。</summary>
    private void RefreshImage()
    {
        ClearImage();
        if (Data is { Length: > 0 } data) _ = LoadAsync(data);
    }

    /// <summary>完成后检查请求身份，切换和卸载后返回的图片立即释放。</summary>
    private async Task LoadAsync(byte[] data)
    {
        using CancellationTokenSource loading = new();
        _loading = loading;
        try
        {
            Bitmap bitmap = await BitmapLoader.DecodeAsync(data, loading.Token);
            if (!_attached || loading.IsCancellationRequested || !ReferenceEquals(_loading, loading))
            {
                bitmap.Dispose();
                return;
            }
            _bitmap = bitmap;
            Source = bitmap;
        }
        catch (OperationCanceledException) when (loading.IsCancellationRequested) { }
        catch (Exception) { /* 无效图片继续使用宿主的文字头像。 */ }
        finally
        {
            if (ReferenceEquals(_loading, loading)) _loading = null;
        }
    }
}
