using System;
using System.Linq;
using Avalonia;
using Avalonia.Input;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using ReactiveUI;

namespace OpenUtauMobile.ViewModels
{
    public partial class PianoRollViewModel
    {
        // 桌面鼠标只改变操作入口；命令、拖动状态和移动端手势仍共用。
        private bool _useDesktopMouseInput;
        public bool UseDesktopMouseInput
        {
            get => _useDesktopMouseInput;
            set
            {
                if (_useDesktopMouseInput == value) return;
                _useDesktopMouseInput = value;
                this.RaisePropertyChanged(nameof(UseDesktopMouseInput));
                this.RaisePropertyChanged(nameof(IsDesktopTuning));
                ApplyViewportLimits();
                RebuildPianoRollContextActions();
                RequestInvalidateVisual?.Invoke();
            }
        }
        public void RevealDesktopPlaybackPosition(int tick, bool onlyIfOutside = false)
        {
            if (!UseDesktopMouseInput || TickWidth <= 0 || _viewportInputActive) return;
            if (onlyIfOutside && TickToPointX(tick) >= 0 && TickToPointX(tick) <= _noteAreaWidth) return;
            _playbackFollow.Reset();
            TickOffset = tick - PlayMarkerX / TickWidth;
            ApplyViewportLimits();
            RequestInvalidateVisual?.Invoke();
        }
        public bool IsDesktopTuning => UseDesktopMouseInput && EditMode == PianoRollEditMode.Anchor;
        public Rect? DesktopSelectionRect { get; private set; }
        public Rect DesktopVibratoToggleRect(UNote note)
        {
            Rect rect = DesktopNoteRect(note);
            return new Rect(rect.Right - 22, rect.Bottom + 2, 20, 16);
        }
        private UNote? HitDesktopVibratoToggle(Point point) => EditingVoicePart?.notes.FirstOrDefault(n => DesktopVibratoToggleRect(n).Contains(point));
        public bool IsDesktopVibratoExpanded(UNote note) => SelectedNotes.Contains(note) && note.vibrato.length > 0;
        private enum DesktopOperation { None, Pan, Box, Create, Move, ResizeEnd, ResizeStart, Anchor, Curve, Vibrato, Pitch }
        private DesktopOperation _desktopOperation;
        private Point _desktopPress, _desktopLast;
        private bool _desktopDragging;
        internal bool IsDesktopMouseDragging => _desktopDragging;
        private UNote? _desktopCreatedNote;
        private UNote[] _desktopSelection = [];
        private (UNote Note, int Position, int Duration)[] _desktopStartOrigins = [];

        private Rect DesktopNoteRect(UNote note) => new(
            TickPitchToPoint(EditingVoicePart!.position + note.position, note.AdjustedTone),
            TickToneToSize(note.duration, 1));

        public StandardCursorType DesktopCursorAt(Point point)
        {
            if (EditMode == PianoRollEditMode.PitchPen) return StandardCursorType.Cross;
            if (IsDesktopTuning) return HitDesktopVibratoToggle(point) != null || HitTestPitchPoint(point) != null || HitTestVibratoControl(point) != null ? StandardCursorType.Hand : StandardCursorType.Arrow;
            if (HitTestNote(point) is not { } note) return StandardCursorType.Arrow;
            Rect rect = DesktopNoteRect(note);
            double edge = Math.Min(5, rect.Width / 4);
            return point.X <= rect.Left + edge || point.X >= rect.Right - edge ? StandardCursorType.SizeWestEast : StandardCursorType.SizeAll;
        }

        public bool BeginDesktopMouse(Point point, KeyModifiers modifiers, int clickCount, bool pan)
        {
            if (!UseDesktopMouseInput || !CanNavigateViewport || DocManager.Inst.HasOpenUndoGroup) return false;
            if (!pan && EditingVoicePart == null) return false;
            InterruptPanMotionIfRunning();
            _desktopPress = _desktopLast = point;
            _desktopDragging = false;
            DesktopSelectionRect = null;
            if (pan)
            {
                _desktopOperation = DesktopOperation.Pan;
                BeginViewportInput();
                return true;
            }
            bool toggle = modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Meta);
            bool additive = toggle || modifiers.HasFlag(KeyModifiers.Shift);
            UNote? hit = HitTestNote(point);
            if (IsDesktopTuning)
            {
                if (HitDesktopVibratoToggle(point) is { } toggleNote)
                {
                    ToggleVibrato(toggleNote);
                    SelectedNotes.Clear(); SelectedNotes.Add(toggleNote);
                    _desktopOperation = DesktopOperation.None;
                    RequestInvalidateVisual?.Invoke();
                    return true;
                }
                if (HitTestPitchPoint(point) is { } anchor)
                {
                    if (!additive || !SelectedNotes.Contains(anchor.Note)) SelectSingleAnchor(anchor.Note, anchor.Point);
                    else if (toggle && SelectedAnchors.Contains(anchor.Point)) { SelectedAnchors.Remove(anchor.Point); _desktopOperation = DesktopOperation.None; return true; }
                    else if (!SelectedAnchors.Contains(anchor.Point)) SelectedAnchors.Add(anchor.Point);
                    _desktopOperation = DesktopOperation.Anchor;
                }
                else if (HitTestVibratoControl(point) is { } vibrato)
                {
                    _desktopOperation = DesktopOperation.Vibrato;
                    if (clickCount == 2) { ToggleVibrato(vibrato.Note); _desktopOperation = DesktopOperation.None; }
                }
                else if (HitTestPitchCurve(point) != null) _desktopOperation = DesktopOperation.Curve;
                else
                {
                    SelectedAnchors.Clear();
                    if (!additive) SelectedNotes.Clear();
                    if (hit != null && !SelectedNotes.Contains(hit)) SelectedNotes.Add(hit);

                    _desktopOperation = DesktopOperation.None;
                }
            }
            else if (EditMode == PianoRollEditMode.PitchPen)
            {
                _desktopOperation = DesktopOperation.Pitch;
            }
            else if (hit != null)
            {
                SelectedAnchors.Clear();
                if (toggle && SelectedNotes.Contains(hit)) { SelectedNotes.Remove(hit); _desktopOperation = DesktopOperation.None; return true; }
                if (!SelectedNotes.Contains(hit)) { if (!additive) SelectedNotes.Clear(); SelectedNotes.Add(hit); }
                Rect rect = DesktopNoteRect(hit);
                double edge = Math.Min(5, rect.Width / 4);
                _desktopOperation = point.X <= rect.Left + edge ? DesktopOperation.ResizeStart : point.X >= rect.Right - edge ? DesktopOperation.ResizeEnd : DesktopOperation.Move;
                if (clickCount == 2) { OnGestureDoubleTap(point); _desktopOperation = DesktopOperation.None; }
            }
            else
            {
                _desktopSelection = additive ? SelectedNotes.ToArray() : [];
                if (!additive) SelectedNotes.Clear();
                SelectedAnchors.Clear();
                _desktopOperation = DesktopOperation.Box;
                if (clickCount == 2)
                {
                    int tick = SnapToFloor(PointXToTick(point.X));
                    if (tick >= EditingVoicePart!.position && tick < EditingVoicePart.End)
                    {
                        int duration = ResolveSnapUnit();
                        if (duration <= 0) duration = DocManager.Inst.Project.resolution;
                        UNote created = DocManager.Inst.Project.CreateNote(PointYToToneInt(point.Y), tick - EditingVoicePart.position, duration);
                        DocManager.Inst.StartUndoGroup(deferValidate: true);
                        DocManager.Inst.ExecuteCmd(new AddNoteCommand(EditingVoicePart, created));
                        _desktopCreatedNote = created;
                        _desktopOperation = DesktopOperation.Create;
                        _desktopDragging = true;
                        _inputState = PianoRollInputState.ResizingNotes;
                        SelectedNotes.Clear(); SelectedNotes.Add(created);
                        PlayCreatedNotePreview(created);
                    }
                    if (_desktopCreatedNote == null) _desktopOperation = DesktopOperation.None;
                }
            }
            RequestInvalidateVisual?.Invoke();
            return true;
        }

        public void UpdateDesktopMouse(Point point, ulong timestamp)
        {
            Vector total = point - _desktopPress;
            Vector step = point - _desktopLast;
            _desktopLast = point;
            if (_desktopOperation == DesktopOperation.Pan) { PanViewport(step); return; }
            if (_desktopOperation == DesktopOperation.None) return;
            if (_desktopOperation == DesktopOperation.Create)
            {
                if (EditingVoicePart != null && _desktopCreatedNote is { } created)
                {
                    int minimum = Math.Max(1, ResolveSnapUnit());
                    int duration = Math.Max(minimum, SnapToRound(PointXToTick(point.X)) - EditingVoicePart.position - created.position);
                    if (duration != created.duration) DocManager.Inst.ExecuteCmd(new ResizeNoteCommand(EditingVoicePart, created, duration - created.duration));
                }
                RequestInvalidateVisual?.Invoke();
                return;
            }
            if (!_desktopDragging)
            {
                if (Math.Abs(total.X) < 3 && Math.Abs(total.Y) < 3) return;
                _desktopDragging = true;
                switch (_desktopOperation)
                {
                    case DesktopOperation.Move: StartToMoveNotes(); break;
                    case DesktopOperation.ResizeEnd:
                        if (HitTestNote(_desktopPress) is { } resize) StartToResizeNotes(SelectedNotes.IndexOf(resize));
                        break;
                    case DesktopOperation.ResizeStart:
                        UNote? startNote = HitTestNote(_desktopPress);
                        _desktopStartOrigins = SelectedNotes.OrderBy(n => n == startNote ? 0 : 1).Select(n => (n, n.position, n.duration)).ToArray();
                        _inputState = PianoRollInputState.ResizingNotes;
                        DocManager.Inst.StartUndoGroup(deferValidate: true);
                        break;
                    case DesktopOperation.Anchor: StartToMoveAnchors(); break;
                    case DesktopOperation.Curve:
                        if (HitTestPitchCurve(_desktopPress) is { } curve) StartToInsertAndMoveAnchor(curve);
                        break;
                    case DesktopOperation.Vibrato:
                        if (HitTestVibratoControl(_desktopPress) is { } vibrato) StartToEditVibrato(vibrato);
                        break;
                    case DesktopOperation.Pitch: OnGestureDragBegin(_desktopPress); break;
                }
            }
            if (_desktopOperation == DesktopOperation.Box)
            {
                Rect box = new(Math.Min(_desktopPress.X, point.X), Math.Min(_desktopPress.Y, point.Y), Math.Abs(total.X), Math.Abs(total.Y));
                DesktopSelectionRect = box;
                if (EditingVoicePart != null) ReplaceSelectedNotes(_desktopSelection.Concat(EditingVoicePart.notes.Where(n => box.Intersects(DesktopNoteRect(n)))).Distinct());
            }
            else if (_desktopOperation == DesktopOperation.ResizeStart) UpdateDesktopStartResize(total);
            else OnGestureDragUpdate(_desktopPress, step, total, point, timestamp);
            RequestInvalidateVisual?.Invoke();
        }

        private void UpdateDesktopStartResize(Vector total)
        {
            if (EditingVoicePart == null || _desktopStartOrigins.Length == 0) return;
            int minimum = Math.Min(_desktopStartOrigins.Min(n => n.Duration), Math.Max(1, ResolveSnapUnit()));
            int position = _desktopStartOrigins[0].Position;
            int delta = SnapToRound(EditingVoicePart.position + position + (int)(total.X / TickWidth)) - EditingVoicePart.position - position;
            delta = Math.Clamp(delta, -_desktopStartOrigins.Min(n => n.Position), _desktopStartOrigins.Min(n => n.Duration) - minimum);
            foreach ((UNote target, int start, int length) in _desktopStartOrigins)
            {
                int movement = start + delta - target.position;
                if (movement == 0) continue;
                // 保持结束位置不变，所有选中音符共享一个撤销组。
                DocManager.Inst.ExecuteCmd(new ResizeNoteCommand(EditingVoicePart, target, -movement));
                DocManager.Inst.ExecuteCmd(new MoveNoteCommand(EditingVoicePart, target, movement, 0));
            }
        }

        public void EndDesktopMouse(Point point, ulong timestamp, bool cancel = false)
        {
            DesktopOperation operation = _desktopOperation;
            _desktopOperation = DesktopOperation.None;
            if (operation == DesktopOperation.Create)
            {
                if (cancel) { DocManager.Inst.RollBackUndoGroup(); SelectedNotes.Clear(); }
                DocManager.Inst.EndUndoGroup();
                _inputState = PianoRollInputState.Idle;
                _desktopCreatedNote = null;
            }
            else if (operation == DesktopOperation.Pan) EndViewportInput(false, cancel);
            else if (_desktopDragging && operation != DesktopOperation.Box)
            {
                if (cancel && DocManager.Inst.HasOpenUndoGroup) DocManager.Inst.RollBackUndoGroup();
                OnGestureDragEnd(point, timestamp);
            }
            else if (operation == DesktopOperation.Curve && !cancel && HitTestPitchCurve(point) is { } curve) InsertAnchorAtCurveHit(curve);
            if (cancel && operation == DesktopOperation.Box) ReplaceSelectedNotes(_desktopSelection);
            DesktopSelectionRect = null;
            _desktopSelection = []; _desktopStartOrigins = []; _desktopDragging = false;
            RequestInvalidateVisual?.Invoke();
        }

        public bool CanDeleteDesktopSelection => IsDesktopTuning ? SelectedAnchors.Any(IsDeletableAnchor) : SelectedNotes.Count > 0;

        public void DeleteDesktopSelection()
        {
            if (!CanDeleteDesktopSelection) return;
            if (IsDesktopTuning && SelectedAnchors.Count > 0) DeleteSelectedAnchors();
            else if (!IsDesktopTuning) DeleteSelectedNotes();
        }
    }
}
