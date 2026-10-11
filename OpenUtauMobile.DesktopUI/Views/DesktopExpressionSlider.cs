using System;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.DesktopUI.Views
{
    internal sealed class DesktopExpressionSlider : UserControl
    {
        private readonly Slider _slider;
        private readonly TextBox _number;
        private readonly Func<NotePropertyField, decimal, bool> _commit;
        private readonly Func<bool> _isCurrent;
        private readonly Func<NotePropertyField, decimal?, bool> _preview;
        private NotePropertyField _field;
        private bool _syncNumber;
        private IPointer? _pointer;
        private decimal? _startValue;
        private double _previewValue;
        private bool _previewing;
        private bool _keyboardEdit;
        private bool _subscribed;

        public bool IsEditing => _pointer != null || _keyboardEdit;

        public DesktopExpressionSlider(NotePropertyField field, TextBox number,
            Func<NotePropertyField, decimal, bool> commit, Func<bool> isCurrent,
            Func<NotePropertyField, decimal?, bool> preview)
        {
            _field = field;
            _commit = commit;
            _isCurrent = isCurrent;
            _preview = preview;
            _number = number;
            _number.Width = 64;
            _number.VerticalAlignment = VerticalAlignment.Center;
            _slider = new WheelPassthroughSlider { MinHeight = 24, VerticalAlignment = VerticalAlignment.Center };
            Grid layout = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6 };
            layout.Children.Add(_slider);
            Grid.SetColumn(_number, 1);
            layout.Children.Add(_number);
            Content = layout;
            _slider.PropertyChanged += OnSliderPropertyChanged;
            _number.PropertyChanged += OnNumberPropertyChanged;
            _slider.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, true);
            _slider.AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Bubble, true);
            _slider.PointerCaptureLost += OnPointerCaptureLost;
            _slider.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel, true);
            _slider.AddHandler(KeyDownEvent, OnKeyDownHandled, RoutingStrategies.Bubble, true);
            _slider.AddHandler(KeyUpEvent, OnKeyUp, RoutingStrategies.Bubble, true);
            _slider.LostFocus += OnLostFocus;
            AttachedToVisualTree += OnAttached;
            DetachedFromVisualTree += OnDetached;
            Rebind(field);
        }

        public void Rebind(NotePropertyField field)
        {
            if (IsEditing) CancelPreview();
            if (_subscribed) _field.PropertyChanged -= OnFieldChanged;
            _field = field;
            if (_subscribed) _field.PropertyChanged += OnFieldChanged;
            _slider.Minimum = field.SliderMinimum;
            _slider.Maximum = Math.Max(field.SliderMinimum, field.SliderMaximum);
            _slider.SmallChange = (double)field.Increment;
            _slider.LargeChange = Math.Max((double)field.Increment, (_slider.Maximum - _slider.Minimum) / 10d);
            _slider.IsEnabled = field.IsEnabled;
            ToolTip.SetTip(this, string.IsNullOrWhiteSpace(field.Hint) ? null : field.Hint);
            AutomationProperties.SetName(_slider, field.Label);
            AutomationProperties.SetHelpText(_slider, field.Hint);
            _previewing = true;
            try { _slider.Value = field.SliderValue; }
            finally { _previewing = false; }
            _previewValue = _slider.Value;
            UpdateReadout(field.Number);
        }

        public bool CommitPendingEdit()
        {
            if (!IsEditing) return true;
            return CommitPreview();
        }

        public void CancelPendingEdit()
        {
            if (IsEditing) CancelPreview();
        }

        private void OnSliderPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property != Slider.ValueProperty || _previewing) return;
            _previewValue = _slider.Value;
            if (IsEditing) UpdateReadout((decimal)_previewValue);
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!_slider.IsEnabled || !_isCurrent() ||
                e.Pointer.Type == PointerType.Mouse && !e.GetCurrentPoint(_slider).Properties.IsLeftButtonPressed) return;
            _keyboardEdit = false;
            _pointer = e.Pointer;
            _startValue = _field.Number;
            _previewValue = _slider.Value;
            UpdateReadout((decimal)_previewValue);
        }

        private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_pointer?.Id != e.Pointer.Id) return;
            CommitPreview();
        }

        private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        {
            if (_pointer?.Id != e.Pointer.Id) return;
            Dispatcher.UIThread.Post(() =>
            {
                if (_pointer?.Id == e.Pointer.Id) CancelPreview();
            }, DispatcherPriority.Input);
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (IsEditing && (e.Key == Key.Escape || e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Control)))
            {
                CancelPreview();
                e.Handled = true;
                return;
            }
            if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown)) return;
            if (!_slider.IsEnabled || !_isCurrent()) return;
            if (!_keyboardEdit)
            {
                _keyboardEdit = true;
                _startValue = _field.Number;
                _previewValue = _slider.Value;
                UpdateReadout((decimal)_previewValue);
            }
        }

        private void OnKeyUp(object? sender, KeyEventArgs e)
        {
            if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown)
            {
                if (_keyboardEdit) CommitPreview();
                e.Handled = true;
            }
        }

        private void OnKeyDownHandled(object? sender, KeyEventArgs e)
        {
            if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown or Key.Escape)
                e.Handled = true;
        }

        private void OnLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_keyboardEdit) CommitPreview();
        }

        private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
        {
            if (IsEditing) CancelPreview();
            if (_subscribed) _field.PropertyChanged -= OnFieldChanged;
            _subscribed = false;
        }

        private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
        {
            if (!_subscribed) _field.PropertyChanged += OnFieldChanged;
            _subscribed = true;
            RestorePosition();
        }

        private void OnFieldChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (!IsEditing && e.PropertyName == nameof(NotePropertyField.SliderValue)) RestorePosition();
            if (!IsEditing && !_syncNumber && e.PropertyName == nameof(NotePropertyField.NumberText))
                RestoreReadout();
        }

        private void OnNumberPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (_syncNumber || IsEditing || !_isCurrent()) return;
            if (e.Property != TextBox.TextProperty) return;
            _syncNumber = true;
            try
            {
                if (decimal.TryParse(_number.Text, NumberStyles.Number | NumberStyles.AllowExponent,
                    CultureInfo.CurrentCulture, out decimal value)) _field.Number = value;
                _field.NumberText = _number.Text;
            }
            finally { _syncNumber = false; }
            if (_field.HasInvalidInput) DataValidationErrors.SetErrors(_number, new[] { OpenUtauMobile.Helpers.L.S("NoteProperties.Invalid") });
            else DataValidationErrors.ClearErrors(_number);
        }

        private bool CommitPreview()
        {
            IPointer? pointer = _pointer;
            _pointer = null;
            _keyboardEdit = false;
            if (!_isCurrent())
            {
                _preview(_field, null);
                RestorePosition();
                RestoreReadout();
                pointer?.Capture(null);
                return false;
            }
            decimal candidate = RoundAndClamp(_previewValue, _field);
            if (_startValue.HasValue && _startValue.Value == candidate)
            {
                _preview(_field, null);
                RestoreReadout();
                pointer?.Capture(null);
                return true;
            }
            bool committed = _commit(_field, candidate);
            _preview(_field, null);
            RestoreReadout();
            pointer?.Capture(null);
            return committed;
        }

        private void CancelPreview()
        {
            IPointer? pointer = _pointer;
            _pointer = null;
            _keyboardEdit = false;
            _preview(_field, null);
            RestorePosition();
            RestoreReadout();
            pointer?.Capture(null);
        }

        private void RestoreReadout() => UpdateReadout(_field.Number);

        private void RestorePosition()
        {
            _previewing = true;
            try { _slider.Value = _field.SliderValue; }
            finally { _previewing = false; }
            _previewValue = _slider.Value;
        }

        private static decimal RoundAndClamp(double value, NotePropertyField field)
        {
            decimal candidate = (decimal)value;
            decimal increment = field.Increment <= 0 ? 1m : field.Increment;
            candidate = Math.Round(candidate / increment) * increment;
            decimal minimum = (decimal)field.SliderMinimum;
            decimal maximum = (decimal)field.SliderMaximum;
            return Math.Clamp(candidate, minimum, maximum);
        }

        private void UpdateReadout(decimal? value)
        {
            if (IsEditing && value.HasValue) value = RoundAndClamp((double)value.Value, _field);
            _syncNumber = true;
            try
            {
                _number.Text = IsEditing ? value?.ToString(CultureInfo.CurrentCulture) : _field.NumberText;
                if (!IsEditing && _field.HasInvalidInput)
                    DataValidationErrors.SetErrors(_number, new[] { OpenUtauMobile.Helpers.L.S("NoteProperties.Invalid") });
                else DataValidationErrors.ClearErrors(_number);
            }
            finally { _syncNumber = false; }
            if (IsEditing && !_preview(_field, value)) CancelPreview();
        }

        private sealed class WheelPassthroughSlider : Slider
        {
            protected override Type StyleKeyOverride => typeof(Slider);

            protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
            {
            }
        }
    }
}
