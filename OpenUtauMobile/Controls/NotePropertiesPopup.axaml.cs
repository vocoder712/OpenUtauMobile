using System;
using System.Linq;
using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Controls;

public partial class NotePropertiesPopup : PopupDialogControl
{
    public static FuncValueConverter<double, double> TabWidthConverter { get; } = new(width => Math.Max(0, Math.Floor(width / 4)));
    protected override PopupDialogWidthPreset WidthPreset => PopupDialogWidthPreset.Regular;

    public NotePropertiesPopup()
    {
        InitializeComponent();
    }

    private void OnApply(object? sender, RoutedEventArgs e)
    {
        // 输入框仍有解析错误时保留草稿，避免提交上一次有效数值。
        if (this.GetVisualDescendants().OfType<Control>().Any(c => c.IsVisible && DataValidationErrors.GetHasErrors(c)
            && c.DataContext is not NotePropertyField { ResetRequested: true }))
        {
            return;
        }
        if (DataContext is NotePropertiesViewModel viewModel)
        {
            viewModel.ApplyCommand.Execute().Subscribe();
        }
    }
}
