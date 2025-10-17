using CommunityToolkit.Maui.Views;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Utils;
using OpenUtauMobile.ViewModels.Controls;
using SkiaSharp;

namespace OpenUtauMobile.Views.Controls;

public partial class SelectPhonemizerPopup : Popup
{
    private SelectPhonemizerPopupViewModel ViewModel { get; }
    public SelectPhonemizerPopup()
	{
		InitializeComponent();
        ViewModel = (SelectPhonemizerPopupViewModel)BindingContext;
        ViewModel.Load();
        //if (ViewModel.Groups.Count > 0 && LayoutGroups.Children.Count > 0)
        //{
        //    // 默认选中第一个分组
        //    if (LayoutGroups.Children[0] is Grid grid)
        //    {
        //        if (grid.Children.Count > 0)
        //        {
        //            foreach (IView child in grid.Children)
        //            {
        //                if (child is Button btn)
        //                {
        //                    btn.BackgroundColor = Color.Parse(ThemeColorsManager.Current.Primary.ToString());
        //                }
        //            }
        //        }
        //    }
        //}
    }

    private void ButtonSelectGroup_Clicked(object sender, EventArgs e)
    {
        // 重置所有按钮背景色
        //foreach (IView view in LayoutGroups.Children)
        //{
        //    if (view is Grid grid)
        //    {
        //        if (grid.Children.Count > 0)
        //        {
        //            foreach (IView child in grid.Children)
        //            {
        //                if (child is Button btn)
        //                {
        //                    btn.BackgroundColor = Colors.Transparent;
        //                }
        //            }
        //        }
        //    }
        //}
        if (sender is Button button && button.BindingContext is KeyValuePair<IGrouping<string, PhonemizerFactory>, string> keyValuePair)
        {
            //button.BackgroundColor = Color.Parse(ThemeColorsManager.Current.Primary.ToString());
            ViewModel.CurrentGroup = keyValuePair;
            ViewModel.LoadGroup();
        }
    }

    private void ButtonSelectPhonemizer_Clicked(object sender, EventArgs e)
    {
        if (sender is Button button && button.BindingContext is KeyValuePair<PhonemizerFactory, string> keyValuePair)
        {
            CloseAsync(keyValuePair.Key);
        }
    }

    private void OnCancelClicked(object sender, EventArgs e)
    {
        CloseAsync(null);
    }
}