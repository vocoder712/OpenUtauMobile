using System.Linq;
using Avalonia.Input;
using OpenUtau.Core;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Services.Editor
{
    public sealed partial class EditorInputController
    {
        public bool CanExecuteSelectionAction(Key key)
            => CanExecuteSelectionAction(ActiveArea, key);

        public bool CanExecuteSelectionAction(EditArea area, Key key)
        {
            if (!_owner.IsEffectivelyVisible || !_owner.IsEffectivelyEnabled || IsModalOpen || _editorPointers.Count != 0 || DocManager.Inst.HasOpenUndoGroup ||
                _owner.DataContext is not EditorViewModel { IsLoadingProject: false } vm || area == EditArea.Mixer) return false;
            bool piano = area == EditArea.PianoRoll;
            if (piano ? IsMixerOpen || !vm.PianoRollViewModel.CanNavigateViewport || vm.PianoRollViewModel.EditingVoicePart == null : !vm.CanNavigateViewport) return false;
            bool selected = piano ? vm.PianoRollViewModel.SelectedNotes.Count > 0 : vm.SelectedParts.Count > 0;
            return key switch
            {
                Key.C or Key.X => selected,
                Key.Delete => piano && vm.PianoRollViewModel.UseDesktopMouseInput ? vm.PianoRollViewModel.CanDeleteDesktopSelection : selected,
                Key.A => piano ? vm.PianoRollViewModel.EditingVoicePart!.notes.Count > 0 : DocManager.Inst.Project.parts.Count > 0,
                Key.V => piano ? DocManager.Inst.NotesClipboard?.Any() == true : DocManager.Inst.PartsClipboard?.Any() == true,
                _ => false
            };
        }

        public void ExecuteSelectionAction(Key key)
        {
            if (!CanExecuteSelectionAction(key) || _owner.DataContext is not EditorViewModel vm) return;
            if (ActiveArea == EditArea.PianoRoll)
            {
                switch (key)
                {
                    case Key.C: vm.PianoRollViewModel.CopySelectedNotes(); break;
                    case Key.X: vm.PianoRollViewModel.CutSelectedNotes(); break;
                    case Key.V: vm.PianoRollViewModel.PasteNotes(); break;
                    case Key.A: vm.PianoRollViewModel.SelectAllNotes(); break;
                    case Key.Delete:
                        if (vm.PianoRollViewModel.UseDesktopMouseInput) vm.PianoRollViewModel.DeleteDesktopSelection();
                        else vm.PianoRollViewModel.DeleteSelectedNotes();
                        break;
                }
            }
            else
            {
                switch (key)
                {
                    case Key.C: vm.CopySelectedParts(); break;
                    case Key.X: vm.CutSelectedParts(); break;
                    case Key.V: vm.PasteParts(); break;
                    case Key.A: vm.SelectAllParts(); break;
                    case Key.Delete: vm.DeleteSelectedParts(); break;
                }
            }
        }
    }
}
