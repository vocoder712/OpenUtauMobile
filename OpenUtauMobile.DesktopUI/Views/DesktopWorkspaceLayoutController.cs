using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.DesktopUI.Services;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.DesktopUI.Views
{
    internal sealed class DesktopWorkspaceLayoutController
    {
        private readonly EditorViewModel _editor;
        private readonly DesktopLayoutStore _layout;
        private readonly Grid _workspaceGrid;
        private readonly Grid _editingGrid;
        private readonly Control _arrangementPane;
        private readonly Control _pianoPane;
        private readonly Control _arrangementSplitter;
        private readonly Control _inspectorHost;
        private readonly Control _inspectorSplitter;
        private readonly DesktopInspector _inspector;
        private readonly DesktopMixerWindow _mixer;
        private readonly Func<bool> _isAttached;
        private readonly Func<Size> _getWorkspaceSize;
        private readonly Action _cancelInput;
        private bool _applyingLayout;

        public DesktopWorkspaceLayoutController(
            EditorViewModel editor,
            DesktopLayoutStore layout,
            Grid workspaceGrid,
            Grid editingGrid,
            Control arrangementPane,
            Control pianoPane,
            Control arrangementSplitter,
            Control inspectorHost,
            Control inspectorSplitter,
            DesktopInspector inspector,
            DesktopMixerWindow mixer,
            Func<bool> isAttached,
            Func<Size> getWorkspaceSize,
            Action cancelInput)
        {
            _editor = editor;
            _layout = layout;
            _workspaceGrid = workspaceGrid;
            _editingGrid = editingGrid;
            _arrangementPane = arrangementPane;
            _pianoPane = pianoPane;
            _arrangementSplitter = arrangementSplitter;
            _inspectorHost = inspectorHost;
            _inspectorSplitter = inspectorSplitter;
            _inspector = inspector;
            _mixer = mixer;
            _isAttached = isAttached;
            _getWorkspaceSize = getWorkspaceSize;
            _cancelInput = cancelInput;

            _editingGrid.RowDefinitions[0].MinHeight = 0;
            _editingGrid.RowDefinitions[2].MinHeight = 0;
            _workspaceGrid.ColumnDefinitions[0].MinWidth = 600;
        }

        public bool IsArrangementVisible => _layout.State.ArrangementVisible && _layout.State.ArrangementRatio > 0;
        public bool IsPianoRollVisible => _layout.State.PianoRollVisible && _layout.State.ArrangementRatio < 1;
        public bool IsParametersVisible => _layout.State.ParametersVisible && _editor.PianoRollViewModel.PhonemePanelHeight > 0;
        public bool IsInspectorRequested => _layout.State.InspectorVisible;

        public void ToggleInspector()
        {
            _layout.State.InspectorVisible = !_layout.State.InspectorVisible;
            if (_layout.State.InspectorVisible && _layout.State.InspectorWidth < 1) _layout.State.InspectorWidth = 320;
            UpdateInspectorVisibility();
            _layout.Save();
        }

        public void ShowInspector()
        {
            _layout.State.InspectorVisible = true;
            if (_layout.State.InspectorWidth < 260) _layout.State.InspectorWidth = 320;
            UpdateInspectorVisibility();
        }

        public void ToggleArrangement()
        {
            _cancelInput();
            _layout.State.ArrangementVisible = !IsArrangementVisible;
            if (_layout.State.ArrangementVisible && _layout.State.ArrangementRatio == 0) _layout.State.ArrangementRatio = .28;
            ApplyLayout();
            _layout.Save();
        }

        public void TogglePianoRoll()
        {
            _cancelInput();
            _layout.State.PianoRollVisible = !IsPianoRollVisible;
            if (_layout.State.PianoRollVisible && _layout.State.ArrangementRatio == 1) _layout.State.ArrangementRatio = .28;
            ApplyLayout();
            _layout.Save();
        }

        public void ToggleParameters()
        {
            _cancelInput();
            _layout.State.ParametersVisible = !IsParametersVisible;
            ApplyLayout();
            _layout.Save();
        }

        public void ShowParameters()
        {
            _layout.State.PianoRollVisible = _layout.State.ParametersVisible = true;
            if (_layout.State.ArrangementRatio == 1) _layout.State.ArrangementRatio = .28;
            ApplyLayout();
            _layout.Save();
        }

        public void ResetLayout(Control ownerControl)
        {
            _layout.Reset();
            _inspector.ResetSections();
            _mixer.ResetWindowGeometry(ownerControl);
            ApplyLayout();
        }

        public void ApplyLayout()
        {
            _applyingLayout = true;
            try
            {
                bool tracks = IsArrangementVisible, piano = IsPianoRollVisible;
                _arrangementPane.IsVisible = tracks;
                _pianoPane.IsVisible = piano;
                _arrangementSplitter.IsVisible = tracks && piano;
                _editingGrid.RowDefinitions[0].Height = new GridLength(tracks ? piano ? _layout.State.ArrangementRatio : 1 : 0, GridUnitType.Star);
                _editingGrid.RowDefinitions[1].Height = new GridLength(tracks && piano ? 6 : 0);
                _editingGrid.RowDefinitions[2].Height = new GridLength(piano ? tracks ? 1 - _layout.State.ArrangementRatio : 1 : 0, GridUnitType.Star);
                _editor.PianoRollViewModel.PhonemePanelHeight = _layout.State.ParametersVisible ? _layout.State.ParameterHeight : 0;
                UpdateInspectorVisibility();
            }
            finally { _applyingLayout = false; }
        }

        public void UpdateInspectorVisibility()
        {
            bool visible = _layout.State.InspectorVisible;
            _inspectorHost.IsVisible = _inspectorSplitter.IsVisible = visible;
            _workspaceGrid.ColumnDefinitions[2].MinWidth = 0;
            _workspaceGrid.ColumnDefinitions[1].Width = new GridLength(visible ? 6 : 0);
            _workspaceGrid.ColumnDefinitions[2].Width = new GridLength(visible ? _layout.State.InspectorWidth : 0);
        }

        public void SavePanels()
        {
            Size size = _getWorkspaceSize();
            if (!_isAttached() || size.Height <= 0) return;
            double tracks = _editingGrid.RowDefinitions[0].ActualHeight;
            double piano = _editingGrid.RowDefinitions[2].ActualHeight;
            if (_layout.State.ArrangementVisible && _layout.State.PianoRollVisible && tracks + piano > 0)
                _layout.State.ArrangementRatio = Math.Clamp(tracks / (tracks + piano), 0, 1);
            if (_inspectorHost.IsVisible) _layout.State.InspectorWidth = Math.Max(0, _inspectorHost.Bounds.Width);
            _layout.Save();
        }

        public void OnEditorChanged(PropertyChangedEventArgs e)
        {
            if (_applyingLayout || e.PropertyName != nameof(PianoRollViewModel.PhonemePanelHeight)) return;
            double height = _editor.PianoRollViewModel.PhonemePanelHeight;
            _layout.State.ParametersVisible = height > 0;
            if (height > 0) _layout.State.ParameterHeight = height;
        }
    }
}
