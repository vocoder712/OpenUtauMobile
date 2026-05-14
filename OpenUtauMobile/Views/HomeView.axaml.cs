using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;

namespace OpenUtauMobile.Views;

public partial class HomeView : UserControl
{
    private bool _layoutInitialized;
    public HomeView()
    {
        InitializeComponent();
    }

    private bool _isLandscape;

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);

        if (e.NewSize.Width <= 0 || e.NewSize.Height <= 0)
        {
            return;
        }

        bool newIsLandscape = e.NewSize.Width > e.NewSize.Height;
        if (!_layoutInitialized)
        {
            _isLandscape = newIsLandscape;
            ApplyResponsiveLayout();
            _layoutInitialized = true;
            return;
        }

        if (newIsLandscape == _isLandscape)
        {
            return;
        }

        _isLandscape = newIsLandscape;
        ApplyResponsiveLayout();
    }

    private void ApplyResponsiveLayout()
    {
        if (_isLandscape)
        {
            MainLayoutGrid.RowDefinitions = new RowDefinitions("Auto, *, Auto");
            MainLayoutGrid.ColumnDefinitions = new ColumnDefinitions("Auto, *");

            Grid.SetRow(RecoveryHost, 0);
            Grid.SetColumn(RecoveryHost, 0);
            Grid.SetColumnSpan(RecoveryHost, 2);

            Grid.SetRow(SectionA, 1);
            Grid.SetColumn(SectionA, 0);
            Grid.SetColumnSpan(SectionA, 1);

            Grid.SetRow(SectionB, 1);
            Grid.SetColumn(SectionB, 1);
            Grid.SetColumnSpan(SectionB, 1);

            Grid.SetRow(FooterBrand, 2);
            Grid.SetColumn(FooterBrand, 0);
            Grid.SetColumnSpan(FooterBrand, 2);

            ActionButtonsPanel.Orientation = Orientation.Vertical;
            ActionButtonsScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            ActionButtonsScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
            return;
        }

        MainLayoutGrid.RowDefinitions = new RowDefinitions("Auto, Auto, *, Auto");
        MainLayoutGrid.ColumnDefinitions = new ColumnDefinitions("*");

        Grid.SetRow(RecoveryHost, 0);
        Grid.SetColumn(RecoveryHost, 0);
        Grid.SetColumnSpan(RecoveryHost, 1);

        Grid.SetRow(SectionA, 1);
        Grid.SetColumn(SectionA, 0);
        Grid.SetColumnSpan(SectionA, 1);

        Grid.SetRow(SectionB, 2);
        Grid.SetColumn(SectionB, 0);
        Grid.SetColumnSpan(SectionB, 1);

        Grid.SetRow(FooterBrand, 3);
        Grid.SetColumn(FooterBrand, 0);
        Grid.SetColumnSpan(FooterBrand, 1);

        ActionButtonsPanel.Orientation = Orientation.Horizontal;
        ActionButtonsScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
        ActionButtonsScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
    }
}