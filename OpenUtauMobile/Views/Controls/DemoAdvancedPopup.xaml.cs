using CommunityToolkit.Maui.Views;

namespace OpenUtauMobile.Views.Controls;

/// <summary>
/// A self-contained demo popup that exercises the advanced features of
/// <see cref="CommunityToolkit.Maui.Views.Popup"/> supported directly by the framework:
/// <list type="bullet">
///   <item>Transparent overlay (<c>Color="Transparent"</c>)</item>
///   <item>Non-center positioning (<c>HorizontalOptions/VerticalOptions</c>)</item>
///   <item>Rounded-corner content (<c>StrokeShape="RoundRectangle N"</c>)</item>
///   <item>Semi-transparent content background (alpha in the <c>Background</c> color)</item>
///   <item>Dismissal control (<c>CanBeDismissedByTappingOutsideOfPopup</c>)</item>
///   <item>Drag-to-reposition via <see cref="PanGestureRecognizer"/> + <c>TranslationX/Y</c></item>
///   <item>Resize via <see cref="PanGestureRecognizer"/> + <c>WidthRequest/HeightRequest</c></item>
/// </list>
/// </summary>
public partial class DemoAdvancedPopup : Popup
{
    // ── drag state ───────────────────────────────────────────────────────────
    private double _dragOffsetX;
    private double _dragOffsetY;

    // ── resize state ─────────────────────────────────────────────────────────
    private double _baseWidth  = 320;
    // Initial height is captured after the first layout pass; 0 means "not yet measured".
    private double _baseHeight;

    public DemoAdvancedPopup()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Capture the true height once MAUI has performed the first layout pass,
    /// so the resize gesture starts from the correct baseline.
    /// </summary>
    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (_baseHeight == 0 && PopupCard.Height > 0)
        {
            _baseHeight = PopupCard.Height;
        }
    }

    // ── close button ─────────────────────────────────────────────────────────

    private void OnCloseClicked(object sender, EventArgs e)
    {
        CloseAsync(null);
    }

    // ── drag: pan gesture on the title bar ───────────────────────────────────

    /// <summary>
    /// Translates <see cref="PopupCard"/> when the user pans the drag handle.
    /// Because <see cref="Popup"/> does not expose a position property, we
    /// use <c>TranslationX/Y</c> on the content view to visually reposition it.
    /// </summary>
    private void OnDragHandlePanUpdated(object sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Running:
                PopupCard.TranslationX = _dragOffsetX + e.TotalX;
                PopupCard.TranslationY = _dragOffsetY + e.TotalY;
                break;

            case GestureStatus.Completed:
                // Persist the accumulated offset so the next drag starts from here.
                _dragOffsetX = PopupCard.TranslationX;
                _dragOffsetY = PopupCard.TranslationY;
                break;
        }
    }

    // ── resize: pan gesture on the bottom-right corner handle ────────────────

    private const double MinWidth  = 200;
    private const double MinHeight = 140;

    /// <summary>
    /// Adjusts the <see cref="PopupCard"/> dimensions when the user pans the
    /// resize handle in the bottom-right corner.
    /// </summary>
    private void OnResizeHandlePanUpdated(object sender, PanUpdatedEventArgs e)
    {
        // If the first layout hasn't completed yet, fall back to WidthRequest.
        if (_baseHeight == 0)
        {
            _baseHeight = PopupCard.HeightRequest > 0 ? PopupCard.HeightRequest : PopupCard.Height;
        }

        switch (e.StatusType)
        {
            case GestureStatus.Running:
                PopupCard.WidthRequest  = Math.Max(MinWidth,  _baseWidth  + e.TotalX);
                PopupCard.HeightRequest = Math.Max(MinHeight, _baseHeight + e.TotalY);
                break;

            case GestureStatus.Completed:
                _baseWidth  = PopupCard.WidthRequest;
                _baseHeight = PopupCard.HeightRequest;
                break;
        }
    }
}
