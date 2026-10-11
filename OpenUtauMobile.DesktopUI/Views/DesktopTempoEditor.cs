using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services.Editor;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.DesktopUI.Views
{
    /// <summary>曲速草稿仅在确认或失焦时提交；无效输入保持可见并阻止保存。</summary>
    internal sealed class DesktopTempoEditor : IDisposable
    {
        private readonly TextBox _input;
        private readonly EditorViewModel _editor;
        private readonly Control _workspace;
        private bool _refreshing;
        private string _committedText = string.Empty;
        private readonly IDisposable _nameBinding;

        public DesktopTempoEditor(TextBox input, EditorViewModel editor, Control workspace)
        {
            _input = input;
            _editor = editor;
            _workspace = workspace;
            _nameBinding = input.Bind(AutomationProperties.NameProperty, input.GetResourceObservable("ProjectEdit.TempoLabel"));
            Refresh();
            input.TextChanged += OnTextChanged;
            input.LostFocus += OnLostFocus;
            input.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
            editor.PropertyChanged += OnEditorChanged;
        }

        private void Refresh()
        {
            _refreshing = true;
            try { _committedText = _editor.ProjectBpm; _input.Text = _committedText; }
            finally { _refreshing = false; }
            DataValidationErrors.ClearErrors(_input);
        }

        private void OnTextChanged(object? sender, TextChangedEventArgs e)
        {
            if (_refreshing) return;
            if (ProjectInfoEditViewModel.TryParseBpm(_input.Text, out _)) DataValidationErrors.ClearErrors(_input);
        }

        public bool Commit()
        {
            if (_input.Text == _committedText) return true;
            if (EditorInputController.IsComposing(_input)) return false;
            if (!_editor.TrySetProjectBpm(_input.Text))
            {
                DataValidationErrors.SetErrors(_input, [L.S("ProjectEdit.BpmRange")]);
                return false;
            }
            Refresh();
            return true;
        }

        public void Focus() => _input.Focus();
        private void OnLostFocus(object? sender, RoutedEventArgs e) => Commit();
        private void OnEditorChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(_editor.ProjectBpm) && _input.Text == _committedText) Refresh();
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Handled || EditorInputController.IsComposing(_input)) return;
            if (e.Key == Key.Escape)
            {
                Refresh();
                e.Handled = true;
                _workspace.Focus();
            }
            else if (e.Key == Key.Enter)
            {
                e.Handled = true;
                if (Commit()) _workspace.Focus();
            }
        }

        public void Dispose()
        {
            _input.TextChanged -= OnTextChanged;
            _input.LostFocus -= OnLostFocus;
            _input.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
            _editor.PropertyChanged -= OnEditorChanged;
            _nameBinding.Dispose();
        }
    }
}
