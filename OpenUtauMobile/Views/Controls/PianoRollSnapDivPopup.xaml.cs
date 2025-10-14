using CommunityToolkit.Maui.Views;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.ViewModels.Controls;

namespace OpenUtauMobile.Views.Controls;

public partial class PianoRollSnapDivPopup : Popup
{
    private PianoRollSnapDivPopupViewModel ViewModel { get; }
    public PianoRollSnapDivPopup(int currentSnapDiv, int[] snapDivs, string title)
	{
		InitializeComponent();
        ViewModel = (PianoRollSnapDivPopupViewModel)BindingContext;
        ViewModel.Initialize(currentSnapDiv, snapDivs);
        LabelTitle.Text = title;
    }

    private void ButtonSelect_Clicked(object sender, EventArgs e)
    {
        if (sender is Button button && button.BindingContext is int snapDiv)
        {
            CloseAsync(snapDiv);
        }
    }
}