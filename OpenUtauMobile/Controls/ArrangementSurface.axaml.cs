using Avalonia.Controls;

namespace OpenUtauMobile.Controls
{
    public partial class ArrangementSurface : UserControl
    {
        public Grid LayoutGrid => TrackAreaGrid;
        public Border Ruler => ArrangementInputRuler;
        public PartsCanvas PartsCanvas => PartsInputCanvas;
        public void SetHeaderExpanded(bool expanded) => TrackAreaGrid.ColumnDefinitions[0].Width = new Avalonia.Controls.GridLength(expanded ? ViewConstants.TrackHeaderWidthExpanded : ViewConstants.TrackHeaderWidthCollapsed);

        public void SetDesktopChrome() { Ruler.IsVisible = true; FloatingActions.IsVisible = false; ArrangementToolbar.IsVisible = false; }
        public ArrangementSurface()
        {
            InitializeComponent();
        }
    }
}
