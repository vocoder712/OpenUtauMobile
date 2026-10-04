using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Controls;

public partial class TrackSettingsPopup : PopupDialogControl
{
    protected override PopupDialogWidthPreset WidthPreset => PopupDialogWidthPreset.Regular;

    public TrackSettingsPopup()
    {
        InitializeComponent();
    }

    private void OnRendererSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is TrackSettingsPopupViewModel viewModel && sender is ComboBox { SelectedItem: string renderer })
            viewModel.SetRenderer(renderer);
    }

    private void OnResamplerSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is TrackSettingsPopupViewModel viewModel && sender is ComboBox { SelectedItem: TrackSettingsToolOption resampler })
            viewModel.SetResampler(resampler.Name);
    }

    private void OnWavtoolSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is TrackSettingsPopupViewModel viewModel && sender is ComboBox { SelectedItem: TrackSettingsToolOption wavtool })
            viewModel.SetWavtool(wavtool.Name);
    }
}
