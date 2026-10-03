using System;
using Avalonia.Controls;
using OpenUtauMobile.Controls;

namespace OpenUtauMobile.Services.Editor
{
    /// <summary>布局仅注册编辑面板；键盘和视口输入共用同一生命周期。</summary>
    public sealed partial class EditorInputController : IDisposable
    {
        public static bool IsComposing(Avalonia.Visual? source) => DialogKeyboard.IsComposing(source);
        private readonly Control _owner;
        private bool _disposed;
        private readonly PianoRollSurface _piano;
        private readonly ArrangementSurface _tracks;
        private readonly Func<bool> _pianoHidden;
        private readonly Func<Control?> _mixer;
        private readonly Func<bool>? _prepareSave;
        private NotesCanvas NotesInputCanvas => _piano.NotesCanvas;
        private PartsCanvas PartsInputCanvas => _tracks.PartsCanvas;
        private Border PianoInputRuler => _piano.Ruler;
        private Control PianoRollAreaGrid => _piano;
        private Control? _mixerPanel => _mixer();
        private bool IsMixerOpen => _pianoHidden();

        public EditorInputController(Control owner, ArrangementSurface tracks, PianoRollSurface piano,
            Func<bool> pianoHidden, Func<Control?> mixer, Func<bool>? prepareSave = null)
        {
            _owner = owner;
            _tracks = tracks;
            _piano = piano;
            _pianoHidden = pianoHidden;
            _mixer = mixer;
            _prepareSave = prepareSave;
            InitializeEditorInput();
        }

        public void Dispose() { _disposed = true; DetachEditorInput(); }
    }
}
