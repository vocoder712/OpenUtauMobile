using System.Collections.Generic;
using System.Reactive;
using Avalonia.Media;
using DynamicData.Binding;
using OpenUtauMobile.Themes.OpenUtauMobile.Runtime;
using ReactiveUI;

namespace OpenUtauMobile.ViewModels;

public class TrackColorPickerPopupViewModel : PopupViewModelBase
{
    public sealed record TrackColorItem(string Name, IBrush AccentColor);

    public ObservableCollectionExtended<TrackColorItem> Colors { get; } = [];

    public ReactiveCommand<string, Unit> SelectColorCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    public TrackColorPickerPopupViewModel(string? currentColorName)
    {
        foreach (TrackPalette.TrackColorInfo color in BuildOrderedColors(currentColorName))
        {
            Colors.Add(new TrackColorItem(color.Name, color.AccentColor));
        }

        SelectColorCommand = ReactiveCommand.Create<string>(RaiseClose);
        CancelCommand = ReactiveCommand.Create(() => RaiseClose(null));
    }

    private static IEnumerable<TrackPalette.TrackColorInfo> BuildOrderedColors(string? currentColorName)
    {
        foreach (TrackPalette.TrackColorInfo color in TrackPalette.TrackColors)
        {
            if (string.Equals(color.Name, currentColorName, System.StringComparison.Ordinal))
            {
                yield return color;
            }
        }

        foreach (TrackPalette.TrackColorInfo color in TrackPalette.TrackColors)
        {
            if (!string.Equals(color.Name, currentColorName, System.StringComparison.Ordinal))
            {
                yield return color;
            }
        }
    }

    public override void RequestBack()
    {
        RaiseClose(null);
    }
}
