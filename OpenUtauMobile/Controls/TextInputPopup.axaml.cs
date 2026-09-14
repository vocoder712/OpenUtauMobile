using Avalonia;
using Avalonia.Threading;

namespace OpenUtauMobile.Controls;

public partial class TextInputPopup : PopupDialogControl
{
    protected override PopupDialogWidthPreset WidthPreset => PopupDialogWidthPreset.Regular;
    public TextInputPopup() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(() => { Input.Focus(); Input.SelectAll(); });
    }
}
