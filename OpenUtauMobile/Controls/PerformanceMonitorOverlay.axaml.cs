using Avalonia;
using Avalonia.Controls;
using OpenUtauMobile.Services.Performance;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Controls;

public partial class PerformanceMonitorOverlay : UserControl
{
    private readonly PerformanceMonitorViewModel _viewModel = new PerformanceMonitorViewModel();
    private IFrameRateProvider? _frameRateProvider;

    public PerformanceMonitorOverlay()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel != null)
        {
            _frameRateProvider = new AvaloniaFrameRateProvider(topLevel);
            PerformanceMonitorService.Instance.AttachFrameRateProvider(_frameRateProvider);
        }
        _viewModel.Activate();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_frameRateProvider != null)
        {
            PerformanceMonitorService.Instance.DetachFrameRateProvider(_frameRateProvider);
            _frameRateProvider = null;
        }
        _viewModel.Dispose();
        base.OnDetachedFromVisualTree(e);
    }
}

