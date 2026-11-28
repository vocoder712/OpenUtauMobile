using Avalonia.Controls;

namespace OpenUtauMobile.Controls
{
    public partial class ArrangementSurface : UserControl
    {
        public Grid LayoutGrid => TrackAreaGrid;
        public Border Ruler => ArrangementInputRuler;
        public PartsCanvas PartsCanvas => PartsInputCanvas;
        /// <summary>轨道头画布；它位于侧栏阴影容器内，不是 <see cref="LayoutGrid"/> 的直接子元素。</summary>
        public TrackHeaderCanvas TrackHeader => TrackHeaders;
        public void SetHeaderExpanded(bool expanded) => TrackAreaGrid.ColumnDefinitions[0].Width = new Avalonia.Controls.GridLength(expanded ? ViewConstants.TrackHeaderWidthExpanded : ViewConstants.TrackHeaderWidthCollapsed);

        public void SetDesktopChrome() { Ruler.IsVisible = true; FloatingActions.IsVisible = false; }
        public ArrangementSurface()
        {
            InitializeComponent();
        }
    }
}
