using System;
using System.Linq;
using Avalonia;
using Avalonia.Input;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Controls
{
    public partial class PartsCanvas
    {
        private enum DesktopPartOperation { None, Pan, Box, Edit }
        private IPointer? _desktopPointer;
        private EditorViewModel? _desktopOwner;
        private DesktopPartOperation _desktopOperation;
        private Point _desktopPress, _desktopLast;
        private bool _desktopDragging;
        private bool _desktopMultiSelectTap;
        private bool _desktopTogglePartToolbar;
        private bool _lastDesktopPressWasLeft;
        private Rect? _desktopBox;
        private UPart[] _desktopSelection = [];

        private bool HandleDesktopPressed(PointerPressedEventArgs e)
        {
            if (DataContext is not EditorViewModel { UseDesktopInput: true } vm || e.Pointer.Type != PointerType.Mouse) return false;
            PointerUpdateKind kind = e.GetCurrentPoint(this).Properties.PointerUpdateKind;
            if (kind == PointerUpdateKind.RightButtonPressed) { _lastDesktopPressWasLeft = false; return true; }
            if (kind is not (PointerUpdateKind.LeftButtonPressed or PointerUpdateKind.MiddleButtonPressed) || _desktopPointer != null || DocManager.Inst.HasOpenUndoGroup) return true;
            bool left = kind == PointerUpdateKind.LeftButtonPressed;
            bool unmodified = e.KeyModifiers == KeyModifiers.None;
            bool doubleClick = left && unmodified && _lastDesktopPressWasLeft && e.ClickCount > 1;
            _lastDesktopPressWasLeft = left && unmodified && !doubleClick;
            _desktopOwner = vm;
            _desktopPress = _desktopLast = e.GetPosition(this);
            _desktopDragging = false;
            _desktopMultiSelectTap = false;
            bool toggle = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
            bool additive = toggle || e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            _desktopSelection = vm.SelectedParts.ToArray();
            // 标签绘制在分片内容上方，重叠时优先命中标签。
            UPart? hit = vm.HitTestMoveLabel(_desktopPress) ?? vm.HitTestPart(_desktopPress);
            // 在按下前记录是否已经选中，避免首次选中或拖动结束后误展开工具栏。
            _desktopTogglePartToolbar = left && unmodified && vm.TrackEditMode == TrackEditMode.Normal &&
                hit != null && vm.SelectedParts.Contains(hit);
            if (!left) { _desktopOperation = DesktopPartOperation.Pan; vm.BeginViewportInput(); }
            else if (vm.TrackEditMode == TrackEditMode.MultiSelect && !additive)
            {
                // 单击在松手后反选；拖动交给共用逻辑，空白和主体平移，手柄仍可编辑选区。
                _desktopMultiSelectTap = true;
                _desktopOperation = DesktopPartOperation.Edit;
            }
            else if (hit == null)
            {
                if (!additive) vm.SelectedParts.Clear();
                if (doubleClick) { vm.OnGestureDoubleTap(_desktopPress); _desktopOperation = DesktopPartOperation.None; }
                else { _desktopSelection = additive ? _desktopSelection : []; _desktopOperation = DesktopPartOperation.Box; }
            }
            else if (toggle && vm.SelectedParts.Contains(hit)) { vm.SelectedParts.Remove(hit); _desktopOperation = DesktopPartOperation.None; }
            else
            {
                if (!vm.SelectedParts.Contains(hit)) { if (!additive) vm.SelectedParts.Clear(); vm.SelectedParts.Add(hit); }
                _desktopOperation = DesktopPartOperation.Edit;
            }
            _desktopPointer = e.Pointer;
            e.Pointer.Capture(this);
            e.Handled = true;
            return true;
        }

        private bool HandleDesktopMoved(PointerEventArgs e)
        {
            if (_desktopPointer != e.Pointer || _desktopOwner is not { } vm) return DataContext is EditorViewModel { UseDesktopInput: true } && e.Pointer.Type == PointerType.Mouse;
            Point point = e.GetPosition(this);
            Vector total = point - _desktopPress;
            Vector step = point - _desktopLast;
            _desktopLast = point;
            if (_desktopOperation == DesktopPartOperation.Pan) vm.PanViewport(step);
            else if (_desktopOperation != DesktopPartOperation.None)
            {
                if (!_desktopDragging && (Math.Abs(total.X) >= 3 || Math.Abs(total.Y) >= 3))
                {
                    _desktopDragging = true;
                    _lastDesktopPressWasLeft = false;
                    if (_desktopOperation == DesktopPartOperation.Edit) vm.OnGestureDragBegin(_desktopPress);
                }
                if (_desktopDragging && _desktopOperation == DesktopPartOperation.Edit) vm.OnGestureDragUpdate(point, step, total, e.Timestamp);
                else if (_desktopDragging && _desktopOperation == DesktopPartOperation.Box)
                {
                    Rect box = new(Math.Min(_desktopPress.X, point.X), Math.Min(_desktopPress.Y, point.Y), Math.Abs(total.X), Math.Abs(total.Y));
                    _desktopBox = box;
                    UPart[] selected = _desktopSelection.Concat(DocManager.Inst.Project.parts.Where(p => box.Intersects(new Rect((p.position - vm.TickOffset) * vm.TickWidth, p.trackNo * vm.TrackHeight - vm.TrackOffset, p.Duration * vm.TickWidth, vm.TrackHeight)))).Distinct().ToArray();
                    foreach (UPart part in vm.SelectedParts.ToArray()) if (!selected.Contains(part)) vm.SelectedParts.Remove(part);
                    foreach (UPart part in selected) if (!vm.SelectedParts.Contains(part)) vm.SelectedParts.Add(part);
                    InvalidateVisual();
                }
            }
            e.Handled = true;
            return true;
        }

        internal void EndDesktopPointer(bool cancel, Point point = default, ulong timestamp = 0)
        {
            IPointer? pointer = _desktopPointer;
            _desktopPointer = null;
            EditorViewModel? vm = _desktopOwner;
            _desktopOwner = null;
            if (vm != null)
            {
                if (_desktopOperation == DesktopPartOperation.Pan) vm.EndViewportInput(false, cancel);
                else if (_desktopDragging && _desktopOperation == DesktopPartOperation.Edit)
                {
                    if (cancel && DocManager.Inst.HasOpenUndoGroup) DocManager.Inst.RollBackUndoGroup();
                    vm.OnGestureDragEnd(point, timestamp);
                }
                if (cancel && _desktopOperation == DesktopPartOperation.Box)
                {
                    vm.SelectedParts.Clear();
                    foreach (UPart part in _desktopSelection) vm.SelectedParts.Add(part);
                }
                if (!cancel && !_desktopDragging && _desktopMultiSelectTap) vm.OnGestureTap(point);
                if (!cancel && !_desktopDragging && _desktopTogglePartToolbar) vm.TogglePartToolbar();
            }
            _desktopBox = null;
            _desktopSelection = [];
            _desktopOperation = DesktopPartOperation.None;
            _desktopMultiSelectTap = false;
            _desktopTogglePartToolbar = false;
            if (cancel) _lastDesktopPressWasLeft = false;
            pointer?.Capture(null);
            InvalidateVisual();
        }
    }
}
