using System;
using System.ComponentModel;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Controls;

public enum MixerFxGraphKind { Equalizer, Compressor, Reverb }

/// <summary>轨道效果器曲线；拖动与下方滑杆共用参数和撤销组。</summary>
public sealed partial class MixerFxGraph : Control
{
    public static readonly StyledProperty<string> XAxisTitleProperty = AvaloniaProperty.Register<MixerFxGraph, string>(nameof(XAxisTitle), string.Empty);
    public static readonly StyledProperty<string> YAxisTitleProperty = AvaloniaProperty.Register<MixerFxGraph, string>(nameof(YAxisTitle), string.Empty);
    public string XAxisTitle { get => GetValue(XAxisTitleProperty); set => SetValue(XAxisTitleProperty, value); }
    public string YAxisTitle { get => GetValue(YAxisTitleProperty); set => SetValue(YAxisTitleProperty, value); }
    public static readonly StyledProperty<IBrush?> CurveBrushProperty = AvaloniaProperty.Register<MixerFxGraph, IBrush?>(nameof(CurveBrush));
    public static readonly StyledProperty<IBrush?> SecondaryBrushProperty = AvaloniaProperty.Register<MixerFxGraph, IBrush?>(nameof(SecondaryBrush));
    public static readonly StyledProperty<IBrush?> GridBrushProperty = AvaloniaProperty.Register<MixerFxGraph, IBrush?>(nameof(GridBrush));
    public static readonly StyledProperty<IBrush?> LabelBrushProperty = AvaloniaProperty.Register<MixerFxGraph, IBrush?>(nameof(LabelBrush));
    public IBrush? CurveBrush { get => GetValue(CurveBrushProperty); set => SetValue(CurveBrushProperty, value); }
    public IBrush? SecondaryBrush { get => GetValue(SecondaryBrushProperty); set => SetValue(SecondaryBrushProperty, value); }
    public IBrush? GridBrush { get => GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public IBrush? LabelBrush { get => GetValue(LabelBrushProperty); set => SetValue(LabelBrushProperty, value); }
    public MixerFxGraphKind Kind { get; set; }
    private MixerChannelViewModel? _channel;
    private MixerViewModel? _editingMixer;
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private double[][]? _reverb;
    private int _version;
    private bool _attached;
    private int _handle = -1;
    private Point _start;
    private double _startGain;
    private IPointer? _pointer;
    private Rect Plot => new(58, 14, Math.Max(1, Bounds.Width - (Kind == MixerFxGraphKind.Equalizer ? 106 : 72)), Math.Max(1, Bounds.Height - 62));

    static MixerFxGraph() => AffectsRender<MixerFxGraph>(CurveBrushProperty, SecondaryBrushProperty, GridBrushProperty, LabelBrushProperty, XAxisTitleProperty, YAxisTitleProperty);

    public MixerFxGraph()
    {
        ClipToBounds = true;
        DataContextChanged += (_, _) => Subscribe();
        _previewTimer.Tick += UpdateReverb;
        _audioTimer.Tick += UpdateAudio;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        Subscribe();
        if (Kind != MixerFxGraphKind.Reverb) _audioTimer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        _audioTimer.Stop();
        ReleaseAudio();
        EndDrag();
        _previewTimer.Stop();
        _version++;
        if (_channel != null) _channel.PropertyChanged -= OnParameterChanged;
        _channel = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void Subscribe()
    {
        ReleaseAudio();
        EndDrag();
        if (_channel != null) _channel.PropertyChanged -= OnParameterChanged;
        _channel = _attached ? DataContext as MixerChannelViewModel : null;
        if (_channel != null) _channel.PropertyChanged += OnParameterChanged;
        RefreshPreview();
    }

    private void OnParameterChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MixerChannelViewModel.LeftDb) or nameof(MixerChannelViewModel.RightDb)
            or nameof(MixerChannelViewModel.Volume) or nameof(MixerChannelViewModel.Pan) or nameof(MixerChannelViewModel.PanText)) return;
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        InvalidateVisual();
        if (Kind != MixerFxGraphKind.Reverb) return;
        _version++;
        _reverb = null;
        _previewTimer.Stop();
        if (_channel != null) _previewTimer.Start();
    }

    private async void UpdateReverb(object? sender, EventArgs e)
    {
        _previewTimer.Stop();
        if (_channel == null) return;
        int version = _version;
        UMixFx fx = _channel.Track?.MixFx?.Clone() ?? new UMixFx();
        fx.ReverbSize = _channel.ReverbSize;
        fx.ReverbWet = _channel.ReverbWet;
        fx.ReverbDamp = _channel.ReverbDamp;
        fx.ReverbPreDelayMs = _channel.PreDelayMs;
        double[][] response = await Task.Run(() => MixerFxResponse.Reverb(fx));
        if (!_attached || version != _version) return;
        _reverb = response;
        InvalidateVisual();
    }

    private double X(double value) => Plot.Left + value * Plot.Width;
    private double Y(double db) => Plot.Bottom - (Kind == MixerFxGraphKind.Equalizer ? (db + 24) / 36
        : Kind == MixerFxGraphKind.Compressor ? (db + 60) / (CompressorTop + 60) : (db + 90) / 90) * Plot.Height;
    private double FrequencyX(double hz) => X(Math.Log10(Math.Clamp(hz, 20, 20000) / 20) / 3);
    private double InputX(double db) => X((db + 60) / 72);
    private double Makeup => _channel?.MakeupDb ?? 2.5;
    private double CompressorTop => Math.Max(12, Math.Ceiling((6 + Makeup) / 12) * 12);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_channel == null) return;
        Pen grid = new(GridBrush, 1);
        double[] levels = Kind == MixerFxGraphKind.Equalizer ? [-24, -12, 0, 12]
            : Kind == MixerFxGraphKind.Compressor ? [-60, -36, -12, CompressorTop] : [-90, -60, -30, 0];
        foreach (double db in levels)
        {
            context.DrawLine(grid, new Point(Plot.Left, Y(db)), new Point(Plot.Right, Y(db)));
            Label(context, db.ToString("0", CultureInfo.InvariantCulture), new Point(29, Y(db) - 7));
        }
        double[] ticks = Kind == MixerFxGraphKind.Equalizer ? [20, 200, 2000, 20000]
            : Kind == MixerFxGraphKind.Compressor ? [-60, -36, -12, 12] : [0, 1, 2, 3, 4];
        foreach (double tick in ticks)
        {
            double x = Kind == MixerFxGraphKind.Equalizer ? FrequencyX(tick) : Kind == MixerFxGraphKind.Compressor ? InputX(tick) : X(tick / 4);
            context.DrawLine(grid, new Point(x, Plot.Top), new Point(x, Plot.Bottom));
            string label = Kind == MixerFxGraphKind.Equalizer && tick >= 1000 ? $"{tick / 1000:0}k" : tick.ToString("0", CultureInfo.InvariantCulture);
            Label(context, label, new Point(Math.Clamp(x - 10, Plot.Left - 6, Bounds.Width - 32), Plot.Bottom + 6));
        }
        FormattedText xTitle = AxisText(XAxisTitle);
        FormattedText yTitle = AxisText(YAxisTitle);
        context.DrawText(xTitle, new Point(Plot.Left + (Plot.Width - xTitle.Width) / 2, Bounds.Height - 18));
        using (context.PushTransform(Matrix.CreateRotation(-Math.PI / 2) * Matrix.CreateTranslation(7, Plot.Top + (Plot.Height + yTitle.Width) / 2)))
            context.DrawText(yTitle, default);
        if (Kind == MixerFxGraphKind.Equalizer)
        {
            Label(context, "dBFS", new Point(Plot.Right + 2, Bounds.Height - 18));
            foreach (double db in new double[] { 0, -30, -60, -90 })
                Label(context, db.ToString("0", CultureInfo.InvariantCulture), new Point(Plot.Right + 5, SpectrumY(db) - 7));
        }
        using (context.PushClip(Plot))
        {
            if (Kind == MixerFxGraphKind.Equalizer)
            {
                DrawSpectrum(context);
                Func<double, double> response = MixerFxResponse.Equalizer(_channel.LowDb, _channel.MidFrequency, _channel.MidDb, _channel.HighDb);
                Curve(context, t => new Point(X(t), Y(response(20 * Math.Pow(1000, t)))), CurveBrush);
            }
            else if (Kind == MixerFxGraphKind.Compressor)
            {
                DrawCompressorLevel(context);
                context.DrawLine(grid, new Point(InputX(-60), Y(-60)), new Point(InputX(12), Y(12)));
                Curve(context, t => new Point(X(t), Y(MixerFxResponse.Compressor(-60 + 72 * t, _channel.ThresholdDb, _channel.Ratio, Makeup))), CurveBrush);
            }
            else if (_reverb != null)
            {
                for (int channel = 0; channel < 2; channel++)
                {
                    double[] envelope = _reverb[channel];
                    Curve(context, t => new Point(X(t), Y(envelope[Math.Min(envelope.Length - 1, (int)(t * envelope.Length))])), channel == 0 ? CurveBrush : SecondaryBrush);
                }
            }
        }
        if (Kind != MixerFxGraphKind.Reverb)
            foreach (Point point in Handles()) context.DrawEllipse(CurveBrush, new Pen(LabelBrush, 1.5), point, 6, 6);
    }

    private void Label(DrawingContext context, string text, Point position) => context.DrawText(
        new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, 10, LabelBrush), position);

    private FormattedText AxisText(string text) => new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface.Default, 11, LabelBrush);

    private static void Curve(DrawingContext context, Func<double, Point> sample, IBrush? brush)
    {
        StreamGeometry geometry = new();
        using (StreamGeometryContext path = geometry.Open())
        {
            path.BeginFigure(sample(0), false);
            for (int i = 1; i <= 240; i++) path.LineTo(sample(i / 240.0));
            path.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(brush, 2), geometry);
    }

    private Point[] Handles()
    {
        if (_channel == null) return [];
        if (Kind == MixerFxGraphKind.Compressor)
            return [new Point(InputX(_channel.ThresholdDb), Y(MixerFxResponse.Compressor(_channel.ThresholdDb, _channel.ThresholdDb, _channel.Ratio, Makeup))),
                new Point(InputX(6), Y(MixerFxResponse.Compressor(6, _channel.ThresholdDb, _channel.Ratio, Makeup)))];
        Func<double, double> response = MixerFxResponse.Equalizer(_channel.LowDb, _channel.MidFrequency, _channel.MidDb, _channel.HighDb);
        // 超出显示范围的锚点留在边界，仍可拖回范围内。
        return [new Point(FrequencyX(200), Math.Clamp(Y(response(200)), Plot.Top, Plot.Bottom)),
            new Point(FrequencyX(_channel.MidFrequency), Math.Clamp(Y(response(_channel.MidFrequency)), Plot.Top, Plot.Bottom)),
            new Point(FrequencyX(8000), Math.Clamp(Y(response(8000)), Plot.Top, Plot.Bottom))];
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_pointer != null || _channel == null || !_channel.FxEnabled || Kind == MixerFxGraphKind.Reverb
            || (e.Pointer.Type == PointerType.Mouse && !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)) return;
        Point position = e.GetPosition(this);
        Point[] handles = Handles();
        double nearest = 26;
        for (int i = 0; i < handles.Length; i++)
        {
            double distance = Math.Sqrt(Math.Pow(handles[i].X - position.X, 2) + Math.Pow(handles[i].Y - position.Y, 2));
            if (distance < nearest) { nearest = distance; _handle = i; }
        }
        if (_handle < 0) return;
        _start = position;
        _startGain = _handle == 0 ? _channel.LowDb : _handle == 1 ? _channel.MidDb : _channel.HighDb;
        _editingMixer = this.FindAncestorOfType<MixerPanel>()?.ViewModel;
        _editingMixer?.BeginEdit();
        _pointer = e.Pointer;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_handle < 0 || _channel == null || e.Pointer != _pointer) return;
        Point position = e.GetPosition(this);
        if (Kind == MixerFxGraphKind.Equalizer)
        {
            double gain = Math.Clamp(_startGain + (_start.Y - position.Y) * 36 / Plot.Height, -12, 12);
            if (_handle == 0) _channel.LowDb = gain;
            else if (_handle == 2) _channel.HighDb = gain;
            else
            {
                _channel.MidFrequency = Math.Clamp(20 * Math.Pow(1000, (position.X - Plot.Left) / Plot.Width), 20, 20000);
                _channel.MidDb = gain;
            }
        }
        else if (_handle == 0)
            _channel.ThresholdDb = Math.Clamp(-60 + (position.X - Plot.Left) * 72 / Plot.Width, -48, 0);
        else
        {
            double output = -60 + (Plot.Bottom - position.Y) * (CompressorTop + 60) / Plot.Height;
            _channel.Ratio = Math.Clamp((6 - _channel.ThresholdDb) / Math.Max(.01, output - Makeup - _channel.ThresholdDb), 1, 10);
        }
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_handle >= 0) { EndDrag(); e.Handled = true; }
        base.OnPointerReleased(e);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        EndDrag();
        base.OnPointerCaptureLost(e);
    }

    private void EndDrag()
    {
        _handle = -1;
        IPointer? pointer = _pointer;
        _pointer = null;
        _editingMixer?.EndEdit();
        _editingMixer = null;
        pointer?.Capture(null);
    }
}
