using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace OpenUtauMobile.DesktopUI.Views
{
    internal sealed record DesktopEditorHint(string AreaKey, string? ToolKey, string Text);

    internal sealed class DesktopStatusBar : Border, IDisposable
    {
        private static readonly Regex ModifierWords = new(@"\b(Ctrl|Control|Cmd|Command|Shift|Alt|Option|Win)\b|⌘|⌥", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private readonly TextBlock _hint = new() { Name = "DesktopShortcutHint", Inlines = new InlineCollection(), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly Border _toastHost = new() { Padding = new Thickness(8, 2), CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
        private readonly TextBlock _toast = new() { Name = "DesktopStatusMessage", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 360 };
        private readonly DispatcherTimer _expiry = new();
        private readonly List<(Run Run, KeyModifiers Modifier)> _words = [];
        private readonly List<IDisposable> _sectionBindings = [];
        private readonly IDisposable _accentBinding;
        private readonly IDisposable _secondaryBinding;
        private DesktopEditorHint? _currentHint;
        private IBrush? _accent;
        private IBrush? _secondary;
        private KeyModifiers _held;
        private bool _disposed;

        public bool HasMessage => _toastHost.IsVisible;
        public event Action? MessageVisibilityChanged;

        public DesktopStatusBar()
        {
            Name = "DesktopStatusBar";
            Classes.Add("DesktopStatusBar");
            Padding = new Thickness(10, 4);
            MinHeight = 28;
            DesktopUi.Paint(this, BackgroundProperty, "Sem.Color.SurfaceContainer");
            DesktopUi.Paint(_hint, TextBlock.ForegroundProperty, "Sem.Color.OnSurfaceVariant");
            DesktopUi.Paint(_toastHost, BackgroundProperty, "Sem.Color.SecondaryContainer");
            DesktopUi.Paint(_toast, TextBlock.ForegroundProperty, "Sem.Color.OnSecondaryContainer");
            _accentBinding = this.GetResourceObservable("Sem.Color.Primary").Subscribe(value => { _accent = value as IBrush; UpdateWords(); });
            _secondaryBinding = this.GetResourceObservable("Sem.Color.OnSurfaceVariant").Subscribe(value => { _secondary = value as IBrush; UpdateWords(); });
            _toastHost.Child = _toast;
            Grid content = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 16 };
            content.Children.Add(_hint);
            Grid.SetColumn(_toastHost, 1);
            content.Children.Add(_toastHost);
            Child = content;
            _expiry.Tick += OnExpiry;
        }

        public void SetEditorHint(DesktopEditorHint? hint)
        {
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => SetEditorHint(hint)); return; }
            if (_disposed || _currentHint == hint) return;
            _currentHint = hint;
            _sectionBindings.ForEach(binding => binding.Dispose());
            _sectionBindings.Clear();
            _words.Clear();
            _hint.Inlines!.Clear();
            if (hint != null)
            {
                AddHeading(hint.AreaKey);
                if (hint.ToolKey is { } toolKey)
                {
                    _hint.Inlines.Add(new Run(" · "));
                    AddHeading(toolKey);
                }
                if (!string.IsNullOrEmpty(hint.Text))
                {
                    _hint.Inlines.Add(new Run("    "));
                    AppendText(hint.Text);
                }
            }
            ToolTip.SetTip(_hint, hint?.Text);
            UpdateWords();
        }

        private void AddHeading(string key)
        {
            Run header = new() { FontWeight = FontWeight.SemiBold };
            _sectionBindings.Add(header.Bind(Run.TextProperty, _hint.GetResourceObservable(key)));
            _hint.Inlines!.Add(header);
        }

        private void AppendText(string text)
        {
            int offset = 0;
            foreach (Match word in ModifierWords.Matches(text))
            {
                if (word.Index > offset) _hint.Inlines!.Add(new Run(text[offset..word.Index]));
                Run run = new(word.Value);
                KeyModifiers modifier = word.Value.ToLowerInvariant() switch
                {
                    "ctrl" or "control" => KeyModifiers.Control,
                    "shift" => KeyModifiers.Shift,
                    "alt" or "option" or "⌥" => KeyModifiers.Alt,
                    _ => KeyModifiers.Meta,
                };
                _words.Add((run, modifier));
                _hint.Inlines!.Add(run);
                offset = word.Index + word.Length;
            }
            if (offset < text.Length) _hint.Inlines!.Add(new Run(text[offset..]));
        }

        public void SetModifiers(KeyModifiers modifiers)
        {
            if (_held == modifiers) return;
            _held = modifiers;
            UpdateWords();
        }

        private void UpdateWords()
        {
            foreach ((Run run, KeyModifiers modifier) in _words)
            {
                bool active = (_held & modifier) != 0;
                run.Foreground = active ? _accent : _secondary;
                run.FontWeight = active ? FontWeight.SemiBold : FontWeight.Normal;
            }
        }

        public void ShowMessage(string message, double durationMs)
        {
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => ShowMessage(message, durationMs)); return; }
            if (_disposed) return;
            _expiry.Stop();
            _toast.Text = message;
            ToolTip.SetTip(_toastHost, message);
            _toastHost.IsVisible = !string.IsNullOrWhiteSpace(message);
            MessageVisibilityChanged?.Invoke();
            _expiry.Interval = TimeSpan.FromMilliseconds(double.IsFinite(durationMs) ? Math.Clamp(durationMs, 500, 30000) : 2000);
            _expiry.Start();
        }

        private void OnExpiry(object? sender, EventArgs e)
        {
            _expiry.Stop();
            if (!_disposed)
            {
                _toastHost.IsVisible = false;
                MessageVisibilityChanged?.Invoke();
            }
        }

        public void Dispose()
        {
            _disposed = true;
            _expiry.Stop();
            _expiry.Tick -= OnExpiry;
            _sectionBindings.ForEach(binding => binding.Dispose());
            _sectionBindings.Clear();
            _accentBinding.Dispose();
            _secondaryBinding.Dispose();
        }
    }
}
