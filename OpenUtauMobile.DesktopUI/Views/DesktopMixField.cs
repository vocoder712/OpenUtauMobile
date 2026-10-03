using System;
using System.Globalization;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.DesktopUI.Views
{
    internal sealed class DesktopMixField : UserControl
    {
        private readonly bool _volume;
        private readonly TrackHeaderViewModel _model;
        private readonly Func<bool> _isCurrent;
        private readonly Slider _slider;
        private readonly TextBox _input;
        private double _accepted;
        private bool _preview;
        private bool _ownsEdit;
        private double _start;
        private IPointer? _pointer;

        public DesktopMixField(UTrack track, TrackHeaderViewModel model, bool volume, Func<bool> isCurrent)
        {
            _volume = volume;
            _model = model;
            _isCurrent = isCurrent;
            _accepted = volume ? track.Volume : track.Pan;
            string prefix = volume ? "TrackVolume" : "TrackPan";
            _input = new TextBox { Name = prefix + "Value", Text = Format(_accepted), Width = 100, HorizontalContentAlignment = HorizontalAlignment.Right };
            _slider = new Slider { Name = prefix + "Slider", Minimum = volume ? -24 : -100, Maximum = volume ? 12 : 100, Value = _accepted };
            ToolTip.SetTip(_input, DesktopUi.Label(volume ? "TrackHeader.Volume" : "TrackHeader.Pan"));
            Grid heading = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6 };
            heading.Children.Add(DesktopUi.Label(volume ? "TrackHeader.Volume" : "TrackHeader.Pan"));
            Grid.SetColumn(_input, 1); heading.Children.Add(_input);
            Content = new StackPanel { Spacing = 2, Children = { heading, _slider } };
            _slider.PropertyChanged += (_, e) =>
            {
                if (e.Property != Slider.ValueProperty || _preview) return;
                Commit(Math.Round(_slider.Value, 1));
            };
            _slider.AddHandler(PointerPressedEvent, (_, e) =>
            {
                if (e.GetCurrentPoint(_slider).Properties.IsLeftButtonPressed) { _pointer = e.Pointer; BeginEdit(); }
            }, RoutingStrategies.Tunnel, handledEventsToo: true);
            _slider.AddHandler(PointerReleasedEvent, (_, _) => EndEdit(false), RoutingStrategies.Bubble, handledEventsToo: true);
            _slider.PointerCaptureLost += (_, _) => EndEdit(false);
            AttachedToVisualTree += (_, _) => { _model.PropertyChanged += OnModelChanged; Restore(); };
            DetachedFromVisualTree += (_, _) => { EndEdit(false); _model.PropertyChanged -= OnModelChanged; };
            _slider.KeyUp += (_, e) => { if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown) CommitSlider(); };
            _slider.AddHandler(KeyDownEvent, (_, e) => { if (e.Key == Key.Escape) { IPointer? pointer = _pointer; EndEdit(true); pointer?.Capture(null); Restore(); e.Handled = true; } }, RoutingStrategies.Tunnel, handledEventsToo: true);
            _input.LostFocus += (_, _) => CommitText();
            _input.KeyDown += (_, e) =>
            {
                if (OpenUtauMobile.Services.Editor.EditorInputController.IsComposing(e.Source as Visual)) return;
                if (e.Key == Key.Enter) { CommitText(); e.Handled = true; }
                else if (e.Key == Key.Escape) { IPointer? pointer = _pointer; EndEdit(true); pointer?.Capture(null); Restore(); e.Handled = true; }
            };
        }

        private string Format(double value) => _volume
            ? value.ToString("0.#", CultureInfo.CurrentCulture) + " dB"
            : Math.Abs(value) < .05 ? "C" : (value < 0 ? "L" : "R") + Math.Abs(value).ToString("0.#", CultureInfo.CurrentCulture);

        private bool TryRead(out double value)
        {
            value = 0;
            string text = (_input.Text ?? string.Empty).Trim();
            int direction = 0;
            if (_volume)
            {
                if (text.EndsWith("dB", StringComparison.OrdinalIgnoreCase)) text = text[..^2].Trim();
            }
            else
            {
                if (text.Equals("C", StringComparison.OrdinalIgnoreCase)) return true;
                if (text.StartsWith("L", StringComparison.OrdinalIgnoreCase)) { direction = -1; text = text[1..].Trim(); }
                else if (text.StartsWith("R", StringComparison.OrdinalIgnoreCase)) { direction = 1; text = text[1..].Trim(); }
                else if (text.EndsWith("L", StringComparison.OrdinalIgnoreCase)) { direction = -1; text = text[..^1].Trim(); }
                else if (text.EndsWith("R", StringComparison.OrdinalIgnoreCase)) { direction = 1; text = text[..^1].Trim(); }
            }
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) || !double.IsFinite(value) || direction != 0 && value < 0) return false;
            if (direction != 0) value *= direction;
            return value >= _slider.Minimum && value <= _slider.Maximum;
        }

        private void CommitText()
        {
            if (!_isCurrent() || DocManager.Inst.HasOpenUndoGroup && !_ownsEdit) return;
            if (!TryRead(out double value)) { DataValidationErrors.SetError(_input, new ArgumentException(L.S("BatchEdit.Validation.Number"))); return; }
            Commit(value);
        }
        private void CommitSlider() => Commit(Math.Round(_slider.Value, 1));
        private void BeginEdit()
        {
            if (_ownsEdit || !_isCurrent() || DocManager.Inst.HasOpenUndoGroup) return;
            _start = _volume ? _model.Volume : _model.Pan;
            DocManager.Inst.StartUndoGroup("调整混音参数");
            _ownsEdit = true;
        }
        private void EndEdit(bool cancel)
        {
            _pointer = null;
            if (!_ownsEdit) return;
            if (cancel && _isCurrent()) Commit(_start);
            _ownsEdit = false;
            if (DocManager.Inst.HasOpenUndoGroup) DocManager.Inst.EndUndoGroup();
        }
        private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == (_volume ? nameof(TrackHeaderViewModel.Volume) : nameof(TrackHeaderViewModel.Pan))) Restore();
        }
        private void Commit(double value)
        {
            if (!_isCurrent() || DocManager.Inst.HasOpenUndoGroup && !_ownsEdit) return;
            // 声像与轨道旋钮、引擎共用 -100 到 100，不能再次缩放。
            if (value != (_volume ? _model.Volume : _model.Pan))
            {
                if (_volume) _model.Volume = value; else _model.Pan = value;
            }
            _accepted = value;
            Restore();
        }
        private void Restore()
        {
            _accepted = _volume ? _model.Volume : _model.Pan;
            _preview = true;
            try { _slider.Value = _accepted; _input.Text = Format(_accepted); DataValidationErrors.ClearErrors(_input); }
            finally { _preview = false; }
        }
    }
}
