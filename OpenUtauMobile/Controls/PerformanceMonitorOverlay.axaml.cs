using Avalonia;
using Avalonia.Controls;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Controls;

public partial class PerformanceMonitorOverlay : UserControl
{
    private readonly PerformanceMonitorViewModel _viewModel = new PerformanceMonitorViewModel();

    public PerformanceMonitorOverlay()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _viewModel.Activate();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _viewModel.Dispose();
        base.OnDetachedFromVisualTree(e);
    }
}

