using System;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia.Media;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Themes.OpenUtauMobile.Runtime;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtauMobile.ViewModels;

public class ThemeColorPickerDialogViewModel : PopupViewModelBase, IDisposable
{
    private readonly Action<Color>? _previewCallback;
    private readonly CompositeDisposable _disposables = new();
    private bool _suppressUpdate;

    [Reactive] public double Hue { get; set; }
    [Reactive] public double Saturation { get; set; }
    [Reactive] public double Value { get; set; }
    [Reactive] public string HexInput { get; set; } = "#FF0000";
    [Reactive] public IBrush PreviewBrush { get; set; } = new SolidColorBrush(Color.Parse("#FF0000"));
    [Reactive] public string ErrorText { get; set; } = string.Empty;
    [Reactive] public bool HasError { get; set; }

    public ReactiveCommand<Unit, Unit> ConfirmCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }
    public ReactiveCommand<Unit, Unit> ApplyHexCommand { get; }

    public ThemeColorPickerDialogViewModel(Color seed, Action<Color>? previewCallback = null)
    {
        _previewCallback = previewCallback;

        ColorMathHelper.RgbToHsv(seed, out double h, out double s, out double v);
        _suppressUpdate = true;
        Hue = h;
        Saturation = s * 100d;
        Value = v * 100d;
        HexInput = ThemeSeedResolver.ToHex(seed);
        PreviewBrush = new SolidColorBrush(seed);
        _suppressUpdate = false;

        this.WhenAnyValue(x => x.Hue, x => x.Saturation, x => x.Value)
            .Skip(1)
            .Subscribe(_ => UpdatePreviewFromHsv())
            .DisposeWith(_disposables);

        ConfirmCommand = ReactiveCommand.Create(OnConfirm);
        CancelCommand = ReactiveCommand.Create(() => RaiseClose(null));
        ApplyHexCommand = ReactiveCommand.Create(OnApplyHex);
    }

    public void Dispose()
    {
        _disposables.Dispose();
        GC.SuppressFinalize(this);
    }

    private void OnConfirm()
    {
        if (!ThemeSeedResolver.TryNormalizeHex(HexInput, out string normalized))
        {
            ErrorText = L.S("Settings.Appearance.ThemeColor.InvalidHex");
            HasError = true;
            return;
        }

        RaiseClose(normalized);
    }

    private void OnApplyHex()
    {
        if (!ThemeSeedResolver.TryNormalizeHex(HexInput, out string normalized) ||
            !ThemeSeedResolver.TryParseHexSeed(normalized, out Color color))
        {
            ErrorText = L.S("Settings.Appearance.ThemeColor.InvalidHex");
            HasError = true;
            return;
        }

        _suppressUpdate = true;
        ColorMathHelper.RgbToHsv(color, out double h, out double s, out double v);
        Hue = h;
        Saturation = s * 100d;
        Value = v * 100d;
        HexInput = normalized;
        PreviewBrush = new SolidColorBrush(color);
        _suppressUpdate = false;

        HasError = false;
        ErrorText = string.Empty;
        _previewCallback?.Invoke(color);
    }

    private void UpdatePreviewFromHsv()
    {
        if (_suppressUpdate)
        {
            return;
        }

        Color color = ColorMathHelper.HsvToRgb(Hue, Saturation / 100d, Value / 100d);
        HexInput = ThemeSeedResolver.ToHex(color);
        PreviewBrush = new SolidColorBrush(color);
        HasError = false;
        ErrorText = string.Empty;
        _previewCallback?.Invoke(color);
    }
}