using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Globalization;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using ReactiveUI;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUtau.Core;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using OpenUtauMobile.DesktopUI.Services;
using IconPacks.Avalonia.PhosphorIcons;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services.Editor;
using OpenUtauMobile.Services;
using OpenUtauMobile.Controls;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.DesktopUI.Views
{
    internal sealed class DesktopInspector : UserControl, ICmdSubscriber, IDisposable
    {
        private readonly EditorViewModel _editor;
        private readonly DesktopLayoutStore _layout;
        public event Action? RequestShowParameters;
        private readonly List<IDisposable> _paletteSubscriptions = [];
        private readonly ScrollViewer _scroll;
        private readonly Expander _notesSection = new();
        private readonly DesktopTrackInspector _trackInspector;
        private readonly DesktopNoteInspector _notesInspector;
        private readonly DesktopPhonemeInspector _phonemeInspector;
        private bool _queued;
        private bool _phonemesChanged = true;
        private bool _attached;
        private bool _disposed;
        private IDisposable? _languageSubscription;

        public DesktopInspector(EditorViewModel editor, DesktopLayoutStore layout)
        {
            _editor = editor;
            _layout = layout;
            Classes.Add("DesktopInspector");
            MinWidth = 260;
            DesktopDensity.ApplyInspector(this);
            _notesSection.Header = DesktopUi.Label("Desktop.Notes");
            _trackInspector = new DesktopTrackInspector(editor, QueueRefresh);
            _notesInspector = new DesktopNoteInspector(editor, layout, QueueRefresh);
            _phonemeInspector = new DesktopPhonemeInspector(editor);
            _notesInspector.RequestShowParameters += () => RequestShowParameters?.Invoke();
            _notesSection.Content = _notesInspector;
            RememberSection(_notesSection, "Notes", true);
            Content = _scroll = DesktopUi.Scroll(new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    Section("Track", DesktopUi.Label("Desktop.Track"), _trackInspector, true),
                    _notesSection,
                    Section("Phoneme", DesktopUi.Label("Desktop.Phoneme"), _phonemeInspector, false)
                }
            });
            _notesSection.HorizontalAlignment = HorizontalAlignment.Stretch;
            AttachedToVisualTree += (_, _) => Attach();
            DetachedFromVisualTree += (_, _) => Detach();
            LostFocus += (_, _) => QueueRefresh();
        }
        private Expander Section(string key, object header, Control content, bool expanded)
        {
            Expander section = new() { Header = header, Content = content, HorizontalAlignment = HorizontalAlignment.Stretch };
            RememberSection(section, key, expanded);
            return section;
        }
        private void RememberSection(Expander section, string key, bool expanded)
        {
            section.Tag = key;
            section.IsExpanded = _layout.State.InspectorSections.GetValueOrDefault(key, expanded);
            section.PropertyChanged += (_, e) =>
            {
                if (e.Property != Expander.IsExpandedProperty) return;
                _layout.State.InspectorSections[key] = section.IsExpanded;
                _layout.Save();
            };
        }
        public void ResetSections()
        {
            foreach (Expander section in this.GetVisualDescendants().OfType<Expander>().ToArray())
            {
                if (section.Tag is string key) section.IsExpanded = key is "Track" or "Notes" or "Notes.Basic";
            }
        }
        public bool CommitPendingInput()
        {
            if (!_notesInspector.CommitPendingInput()) return false;
            return !this.GetVisualDescendants().OfType<Control>().Any(DataValidationErrors.GetHasErrors);
        }
        public void SelectNotes()
        {
            _notesSection.IsExpanded = true;
            Dispatcher.UIThread.Post(() =>
            {
                _notesSection.BringIntoView();
                if (_scroll.Content is Visual content && _notesSection.TranslatePoint(default, content) is Point point)
                    _scroll.Offset = new Vector(0, Math.Clamp(point.Y, 0, Math.Max(0, _scroll.Extent.Height - _scroll.Viewport.Height)));
            }, DispatcherPriority.Loaded);
        }
        private void Attach()
        {
            if (_attached || _disposed) return;
            _attached = true;
            _notesInspector.SetAttached(true);
            foreach ((string role, string semantic) in new[]
            {
                ("ExpanderContentBackground", "Sem.Color.SurfaceContainerLow"),
                ("ExpanderContentBorderBrush", "Sem.Color.OutlineVariant"),
                ("ExpanderHeaderBackground", "Sem.Color.SurfaceContainer"),
                ("ExpanderHeaderBackgroundPointerOver", "Sem.Color.SurfaceContainerHigh"),
                ("ExpanderHeaderBackgroundPressed", "Sem.Color.SurfaceContainerHighest"),
                ("ExpanderHeaderBorderBrush", "Sem.Color.OutlineVariant")
            }) _paletteSubscriptions.Add(this.GetResourceObservable(semantic).Subscribe(value => { if (value != null && (!Resources.TryGetValue(role, out object? existing) || !Equals(existing, value))) Resources[role] = value; }));
            DocManager.Inst.AddSubscriber(this);
            _editor.PropertyChanged += OnEditorChanged;
            _editor.PianoRollViewModel.PropertyChanged += OnEditorChanged;
            _editor.PianoRollViewModel.SelectedNotes.CollectionChanged += OnSelectionChanged;
            _editor.SelectedParts.CollectionChanged += OnSelectionChanged;
            DesktopToolsService.Instance.Changed += OnToolsChanged;
            _languageSubscription = this.GetResourceObservable("Desktop.Notes").Subscribe(_ => { _trackInspector.Invalidate(); _notesInspector.MarkExternallyChanged(cancelPreviews: false); _phonemesChanged = true; QueueRefresh(); });
            QueueRefresh();
        }
        private void Detach()
        {
            _notesInspector.SetAttached(false);
            if (!_attached) return;
            _attached = false;
            foreach (IDisposable subscription in _paletteSubscriptions) subscription.Dispose();
            _paletteSubscriptions.Clear();
            _languageSubscription?.Dispose();
            _languageSubscription = null;
            DocManager.Inst.RemoveSubscriber(this);
            _editor.PropertyChanged -= OnEditorChanged;
            _editor.PianoRollViewModel.PropertyChanged -= OnEditorChanged;
            _editor.PianoRollViewModel.SelectedNotes.CollectionChanged -= OnSelectionChanged;
            _editor.SelectedParts.CollectionChanged -= OnSelectionChanged;
            DesktopToolsService.Instance.Changed -= OnToolsChanged;
        }
        private void OnEditorChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(PianoRollViewModel.EditingVoicePart) or nameof(PianoRollViewModel.EditingWavePart))
            {
                _notesInspector.CancelPreviews();
                QueueRefresh();
            }
        }
        private void OnToolsChanged()
        {
            _trackInspector.Invalidate();
            QueueRefresh();
        }
        private void OnSelectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            _notesInspector.OnSelectionChanged();
            QueueRefresh();
        }
        public void OnNext(UCommand cmd, bool isUndo)
        {
            if (cmd is SetPlayPosTickNotification or SeekPlayPosTickNotification or ProgressBarNotification) return;
            if (cmd is MixCommand) { _trackInspector.RefreshMix(); return; }
            if (!_notesInspector.IsCommitting && (cmd is not UNotification || cmd is LoadProjectNotification))
                _notesInspector.MarkExternallyChanged(cancelPreviews: true);
            // 音素生成结果也会改变继承的表达式值，不能只刷新音素面板。
            if (cmd is PhonemizedNotification phonemized && phonemized.part == _notesInspector.CurrentPart)
                _notesInspector.MarkExternallyChanged(cancelPreviews: false);
            if (cmd is NoteCommand or PartCommand or PhonemizedNotification or TrackCommand or LoadProjectNotification) _phonemesChanged = true;
            if (cmd is not UNotification || cmd is LoadProjectNotification or PhonemizedNotification or PreRenderNotification) QueueRefresh();
        }
        private void QueueRefresh()
        {
            if (_queued || _disposed) return;
            _queued = true;
            Dispatcher.UIThread.Post(() =>
            {
                _queued = false;
                if (!_attached || _disposed || DocManager.Inst.HasOpenUndoGroup) return;
                _trackInspector.Refresh();
                // 输入中的草稿保留；选择改变则立即失效，不能写入下一次选择。
                bool same = _notesInspector.RefreshIfNeeded();
                if (!same) _phonemesChanged = true;
                if (_phonemesChanged && !_phonemeInspector.HasInputFocus()) { _phonemeInspector.Refresh(); _phonemesChanged = false; }
            }, DispatcherPriority.Background);
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Detach();
            _notesInspector.Dispose();
            _trackInspector.Dispose();
        }
    }
}
