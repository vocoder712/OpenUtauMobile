using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Services.Editor
{
    public static class EditorShortcuts
    {
        public static bool IsCommandModifier(KeyModifiers modifiers, bool isMacOS) =>
            modifiers == KeyModifiers.Control || isMacOS && modifiers == KeyModifiers.Meta;

        public static bool IsTextInput(Visual? source) =>
            source?.GetSelfAndVisualAncestors().Any(node => node is TextBox or ComboBox) == true;

        public static ICommand? GetAction(EditorViewModel editor, Key key, KeyModifiers modifiers, bool isMacOS)
        {
            if (IsCommandModifier(modifiers, isMacOS))
                return key switch { Key.Z => editor.UndoCommand, Key.Y => editor.RedoCommand, _ => null };
            if (key == Key.Z && modifiers.HasFlag(KeyModifiers.Shift) &&
                IsCommandModifier(modifiers & ~KeyModifiers.Shift, isMacOS)) return editor.RedoCommand;
            return key == Key.Space && modifiers == KeyModifiers.None ? editor.PlayPauseCommand : null;
        }
    }
}
