using System;
using System.Linq;
using Avalonia.Interactivity;
using OpenUtauMobile.ViewModels;
using Avalonia;
using Avalonia.Controls;

namespace OpenUtauMobile.Controls;

public partial class PhonemeParamPanel : UserControl
{
    public event Action<Point>? RequestMagnifierOpen;
    public event Action<Point>? RequestMagnifierUpdate;
    public event Action? RequestMagnifierClose;

    public PhonemeParamPanel()
    {
        InitializeComponent();
        ParameterCurveCanvas.RequestMagnifierOpen += point => RequestMagnifierOpen?.Invoke(point);
        ParameterCurveCanvas.RequestMagnifierUpdate += point => RequestMagnifierUpdate?.Invoke(point);
        ParameterCurveCanvas.RequestMagnifierClose += () => RequestMagnifierClose?.Invoke();
    }

    private void OnResetPhonemes(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PianoRollViewModel { EditingVoicePart: { } part } vm)
            PhonemeCanvasActions.ResetTiming(part, part.phonemes.Where(p => vm.SelectedNotes.Contains(p.Parent)));
    }

    public Point? TranslateParameterPoint(Point point, Visual relativeTo)
    {
        return ParameterCurveCanvas.TranslatePoint(point, relativeTo);
    }
}