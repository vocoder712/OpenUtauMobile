using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace OpenUtauMobile.Controls;

public partial class BulkLyricEditPopup : PopupDialogControl
{
    protected override PopupDialogWidthPreset WidthPreset => PopupDialogWidthPreset.Wide;

    public BulkLyricEditPopup()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(FocusAndSelectLyrics);
    }

    private void FocusAndSelectLyrics()
    {
        TextBox? input = this.FindControl<TextBox>("LyricsInput");
        if (input == null)
        {
            return;
        }

        input.Focus();
        input.SelectAll();
    }
}
