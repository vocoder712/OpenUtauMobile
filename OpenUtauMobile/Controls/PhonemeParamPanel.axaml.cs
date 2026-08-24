using System;
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

    public Point? TranslateParameterPoint(Point point, Visual relativeTo)
    {
        return ParameterCurveCanvas.TranslatePoint(point, relativeTo);
    }
}
