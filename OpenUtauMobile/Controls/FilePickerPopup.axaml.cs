namespace OpenUtauMobile.Controls;

public partial class FilePickerPopup : PopupDialogControl
{
    private const double MultiSelectVerticalViewportInset = 48d;

    protected override PopupDialogWidthPreset WidthPreset => PopupDialogWidthPreset.Wide;

    public FilePickerPopup()
    {
        InitializeComponent();
    }

    protected override void UpdateResponsiveSize(Avalonia.Controls.TopLevel host)
    {
        base.UpdateResponsiveSize(host);
        // 多选导入沿用原来的视口高度限制，其他模式保持原尺寸策略。
        MaxHeight = DataContext is ViewModels.MultiFilePickerViewModel
            ? System.Math.Max(0, host.ClientSize.Height - MultiSelectVerticalViewportInset)
            : double.PositiveInfinity;
    }
}
