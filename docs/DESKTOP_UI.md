# Desktop presentation

`OpenUtauMobile.DesktopUI` is referenced by Windows, Linux and macOS hosts only. Its registration
provides `ServiceHub.DesktopWindowFactory` and enables native desktop file workflows. Android, iOS and
Browser compile the shared Mobile layer without this presentation assembly.

## Editing and session ownership

`ArrangementSurface` and `PianoRollSurface` contain the shared canvases. Both the mobile editor and
desktop workspace register these surfaces with `EditorInputController`, which owns pointer capture,
coordinate conversion, active edit area and keyboard routing. Desktop adds composition and inspectors,
not a second project model or editor session.

The desktop Notes tool combines selection, box selection and note movement. Ctrl/Cmd-click toggles
selection; Shift-click adds notes; dragging either note edge resizes it. Double-click empty space creates
a snapped note, and double-click a note edits lyrics inline. Middle-drag pans from any tool. Escape cancels the
current drag, and Ctrl/Cmd-wheel zooms around the mouse. Panning and zooming preserve playback position;
clicking or dragging either ruler seeks and reveals the position in the piano roll when it lies outside its viewport. Scrubbing keeps the press position fixed when the viewport follows the marker. Holding the pointer at either ruler edge scrolls continuously in that direction until release or cancellation. Arrangement clips and editing canvases do not seek on desktop clicks. The desktop playhead moves across the viewport and pages during playback. Arrangement and piano roll share the same automatic page-turn setting and smooth interpolation; ruler input suppresses follow until capture ends. Tuning keeps pitch anchors and vibrato together
and separate from note movement; pitch drawing remains a third tool. Its desktop left drag draws pitch without panning blank canvas; middle drag still pans. Disabled vibrato uses the same wave symbol in the on-surface color, switching to the accent when enabled. Mouse routing is opted into by the
desktop workspace; mobile gesture modes and hit-target sizes remain available.

`MainViewModel.ActiveEditor` identifies the owning session while utility pages are open.
`DesktopPageLocator` retains its workspace until the session closes or is replaced. Project replacement
uses existing unsaved-change confirmation, cancels the old project through existing notifications and
waits for queued tool maintenance before loading another project. Desktop utilities open in owned windows, retaining the central workspace and its editor. Escape and the native window close button return through the existing navigation stack. Utility dialogs and native file pickers use the active window. All desktop modal dialogs use separate owned windows through the optional PopupService presenter. They retain the shared validation/commands, use native close to cancel, support nested ownership, and release subscriptions on completion. Mobile keeps its DialogHost presentation. Event subscriptions follow attachment/disposal; track headers rebuild on reattachment.

`NotePropertyEditor` owns shared mixed-value drafts, validation and command generation. The mobile
popup retains Apply/Cancel. The desktop inspector commits text on Enter or focus loss, choices
immediately; Escape discards input. Drafts capture selection identity and reject
stale commits. Each valid edit is one undo group. Track render settings are cloned before commands are
created. Phoneme overrides use existing undoable commands.

## Layout, styles and files

`DesktopPresentationCatalog` is the exhaustive route registry. Every desktop utility has a desktop view factory and a centralized window profile; startup splash is explicitly shared. Unregistered routes show a localized unavailable surface and log the missing registration instead of falling back to a mobile page. `DesktopDialogCatalog` registers each desktop-reachable popup and owns its width, scroll and dense-field profile while preserving the shared popup view model, cancellation and result behavior.

`DesktopStyles.axaml` and `DesktopDensity` define the desktop geometry and typography roles. Feature views own their composition and business content; shared ControlThemes continue to own visual interaction states. Geometry resource keys used by shared controls keep mobile touch-size defaults and are overridden only by desktop roots. Detached dialog and mixer windows apply the desktop profile explicitly. `DesktopUtilityShell` provides a reusable utility-page shell. Only the editor shell has a shortcut/status bar. Utilities retain their feature-specific loading/progress/selection content; generic notifications route to the editor's message area.

Compact density retains OUM's rounded semantic surfaces, padded rows and heading hierarchy. Size action and toolbar roles explicitly; blanket descendant Button or TextBlock overrides can break embedded editor controls and headings. Reused views with local styles must receive desktop role overrides at the same style scope after their shared styles. Slider drawing and hit targets share the same geometry resources, including the thumb extent used for value positioning. Utility windows use their native title once; their shell adds a toolbar only when it contains actions. Home retains its initialized page and view model while utilities are open.

Singer Management separates the library and details with a fixed gap, preserves padded OUM identity/metadata cards, and keeps the uninstall action inside its danger section. The desktop installer owns its four step pages without a mobile carousel or app bar. Encoding previews have finite list viewports; navigation stays outside scrolling, and the busy state preserves the existing installation close restrictions. The third step uses the existing singer-information/type titles.

The styling correction was checked with offscreen light/dark Home captures at 800, 1280 and 1920 DIP widths, Settings and singer library captures at default/minimum sizes, injected slider drag/keyboard input, horizontal slider and vertical mixer-fader alignment, retained Home navigation/selection, and unchanged mobile slider dimensions. HiFiSampler's Suggestions flow exposes its consumed flags, preserves existing user-named flags and supports configuration undo. These checks do not establish native-host behavior at every monitor scale or language.

`DesktopLayoutStore` saves application-owned JSON separately from projects and USTX. Main, utility and mixer geometry, workspace splits and the mixer FX pane width are restored with monitor bounds and safe insets. The main window starts at 1440 × 900 logical pixels, with a 28% arrangement height and a 320-pixel inspector; the separate mixer starts closed. Standard utilities target 960 × 720 (minimum 560 × 400); About is 560 × 600 (minimum 400 × 320), Logs 800 × 600 (minimum 560 × 400), singer setup 800 × 640 (minimum 560 × 400), and Mixer 900 × 560 (minimum 600 × 420). Reset Layout restores workspace, utility and mixer geometry without changing projects or preferences.

Narrow main windows collapse the inspector before taking space from the central editor. Its divider can also collapse the visible width to zero while keeping a reopen handle. Contents retain a 260-pixel minimum width, anchor to the left, and clip at the viewport edge instead of shrinking. The inspector can expand up to the available width while retaining 640 logical pixels for the editor. Note Parameters reopens a collapsed inspector. View exposes separate checked toggles for Arrangement, Piano roll, Parameter panel and Inspector. Their visibility is saved alongside the split ratio and last positive bottom-lane height; reopening restores that height.

View → Mixer opens one non-modal owned window sharing the editor project. Desktop channel strips are 112 DIPs wide, the master is 80, and the resizable FX pane starts at 288. The FX pane opens for the selected channel; volume and pan have editable readouts. The mixer uses native window chrome and supports transport/undo shortcuts without taking keys from text input. Mixer popup ownership follows its active window. The arrangement/piano-roll divider reaches either endpoint and keeps its reopen handle; a hidden pane clips its contents. The bottom lane can fill the piano-roll area or collapse to zero with a reopen grip; its intentional translucent background remains visible over the underlying notes. The original advanced-phoneme and parameter backgrounds retain their 0.6 opacity; the simple phoneme canvas keeps its original opaque background. Resize clamps run after layout so navigation cannot erase its height.
Mobile exposes its existing mixer through the sliders button next to Save. Arrangement, phoneme and
inspector resizing use the same desktop grip. Navigation actions live in the top menus; snap/Auto sits on the right of the transport bar.

Desktop popup and utility windows open centered on screen and are clamped to the monitor’s usable area with a 12-DIP inset. Dialog widths are 360, 560 or 800 DIPs. Scroll-heavy dialogs initially use 560 DIPs with a 360-DIP minimum; content-driven dialogs measure before showing. Desktop fields and batch actions use compact sizing; selection summaries retain a top inset and icon backgrounds leave space around their glyphs. Window-hosted DialogShell uses compact actions aligned to the right, stacks action groups when a narrow window cannot fit them, and omits the mobile rounded overlay chrome. Mobile retains its original grouped touch actions, translucency and header.

The main window extends the menu row into the title bar where supported, retaining platform caption buttons and window hit-testing. The title centers across the whole window and trims symmetrically when menus or caption buttons need space. The existing decoration theme hides its duplicate title text and fullscreen button for the editor while retaining minimize, maximize/restore and close. Track knobs use a compact value readout and horizontal/vertical relative mouse adjustment with Shift fine control. Cursor recentering is implemented for Windows, macOS and X11; unsupported display servers, including Wayland, use captured mouse movement without recentering. Release, loss of capture and window deactivation restore the cursor. Touch adjustment and the mobile track HUD retain their existing presentation.

The editor follows the last selected part when multiple parts are selected. The inspector is one scrollable column ordered Track → Notes → Phoneme, with compact rows and collapsible sections. The Edit menu exposes batch and bulk lyric editing using the same dialogs as mobile. Desktop bulk lyrics always target the captured note selection; the mobile selection option remains unchanged. The status bar shows shortcuts for the hovered interaction or focused control. Hovering a pane changes pointer-gesture hints without moving keyboard edit focus; wheel routing continues to hit-test the actual pane. Held modifiers emphasize the existing shortcut words. Hints remain on the left while notifications appear independently on the right, including messages posted from background work. The upper-right toolbar retains dedicated rendering progress. The mobile toast overlay retains its existing behavior.

Transport and arrangement tempo fields edit inline using the existing BPM validation and undo command. Enter commits, Escape cancels, and focus loss commits valid input. Invalid or composing drafts block save/close and remain visible. The arrangement's tempo/meter/key pills fit inside the ruler row rather than being clipped by it.

The Track inspector exposes name/color, voicebank, phonemizer, renderer, compatible Classic tools and
mix controls, with editable dB and L/C/R values beside their labels. Track names edit directly in the inspector on Enter/focus loss or in an anchored input from the arrangement header; Escape discards the draft. Phonemizer controls use the same outlined picker geometry as adjacent inputs. Mute and Solo divide the full inspector row equally and highlight their explicit track state. The shared arrangement header groups Mute and Solo immediately to the right of the avatar in expanded headers and removes the corner triangle. Buttons highlight explicit Mute/Solo state; silenced track content dims without changing the M button state. Desktop uses compact controls; mobile uses larger controls. Both buttons hide when the header is collapsed, retaining its original 64-pixel width, and fit the minimum expanded track height. Inspector sliders send existing mix commands throughout a drag, synchronize with the track knobs and mixer, and keep the drag in one undo group; Escape restores its starting value. Pan uses the engine’s
-100…100 scale directly. Notes exposes the selected-note draft, presets, portamento, vibrato and only scalar expressions supported by the current renderer, matching official OpenUtau. The inspector expression-lane selector uses the same renderer-supported list and opens the bottom parameter lane for supported curve expressions. HiFiSampler’s renderer support list is honored when that renderer is active; its flags do not add unrelated project descriptors.
Phoneme exposes the selected note's phonemes, alias and supported timing overrides.

Desktop roles are scoped in `DesktopStyles.axaml`, use existing OPUM control themes and dynamic semantic
colors, and preserve localization. Shared mobile sizing tokens remain unchanged. Singer selection uses avatar/name/type rows in both the inspector dropdown and the track-header menu. Renderer selection uses dropdowns in the inspector and anchored menus from track headers. Phonemizers use an anchored search/language flyout. Other dialogs retain desktop viewport bounds. Individual lyrics edit over the note; phoneme aliases edit over their lane labels, with Enter/focus-loss commit, Escape cancellation and Tab navigation. Selection or part changes discard pending input. Escape cancels through the dialog’s existing close command after child controls have handled their own Escape actions.

Desktop file pickers use the native storage provider in both build configurations. Save destination
depends on file identity, not dirty state: existing USTX files save in place; unnamed, template and
imported projects require Save As. Drops and imports use existing project/audio services and commands. Singer archives open the existing installation wizard. Tools enter the existing serialized installation queue; ambiguous executable names require resampler/wavtool classification, as in original OpenUtau. Only one installer file is accepted per drop. Home contains recent projects, templates and recovery. Desktop always starts on Home; there is no startup-page setting. A single click selects a recent project or template; double-click or Enter opens it, and templates also have a visible create action. Home opened over a project closes back to it. Stop reveals its configured return position in both desktop viewports.

## Playback and selection performance

The desktop inspector retains its control tree across selections with the same property schema and rebinds it to a fresh, guarded draft. Selection changes still discard stale input, and external note commands still refresh values. Unchanged focus/transport updates do not rebuild note or phoneme controls. Shared canvases use a selected-note lookup, the property draft captures selection membership once, and playback state is synchronized once per position notification/timer tick. These shared optimizations also apply to mobile. While a desktop ruler owns the viewport, seek reveal and playback page-follow leave its continuous edge scrolling in control.

Windows WaveOut playback sets NAudio `DesiredLatency` to 60 ms and `NumberOfBuffers` to 3 in `OpenUtauMobile.Windows/Services/Audio/NAudioOutput.cs`. Audible dropout behavior on a physical audio device has not been verified.

## External tools

`DesktopToolsService` uses existing `ToolsManager` discovery and renderer compatibility APIs. It
serializes installation, removal, Wine changes and rescanning. Maintenance waits until the editor session
closes, since the current upstream engine has no application-facing global render-idle boundary.
Cancellation leaves queued operations unapplied. No upstream rendering or project-format changes are
included.

Folder installation preserves the package tree; file installation preserves same-basename companion
files and neighboring runtime libraries/configuration. Replacement collisions require confirmation. Uninstallation
rejects builtin or actively referenced tools, including resampler expression overrides. Shared libraries
and unrelated configuration are retained. Rescanning does not rewrite valid project selections.

Windows exposes external programs supported by the current engine. Linux/macOS expose executable
native resamplers and Windows programs through a configurable Wine path. Wine support is experimental;
native external wavtools are unavailable because existing concatenation generates Windows batch scripts.
Discovered, unavailable and experimental states describe prerequisites, not successful execution.
Execution errors retain existing engine diagnostics; management errors show detailed exceptions.

## Verification recorded for this change

- Compilation: shared Mobile, Windows x64 Debug and Android ARM64 on Windows, with Avalonia telemetry
  disabled. Linux/macOS builds require matching hosts and were not run here.
- Offscreen appearance: desktop light/dark themes, full shell, mixer, narrow layout and increased DPI;
  mobile portrait/landscape and mixer using shared surfaces.
- Simulated interaction: inspector Enter/Escape, stale-selection rejection, mixed edits and undo/redo,
  preset save/remove, mouse-opened preset dropdowns with keyboard selection and undo, external mix-command synchronization, context-menu targeting, vibrato-clear icon/handle synchronization, phoneme drag cancellation and part replacement, Delete/undo routing,
  note movement and both-edge resizing, reverse box selection, additive selection, double-click creation and hold-to-resize in one undo group,
  unified arrangement selection/movement, direct-scale pan with editable L/C/R readouts, rejected invalid values, live mix sliders with grouped undo and Escape rollback, bidirectional knob/slider synchronization, shared mobile/desktop solo state, independent pane visibility and restored parameter height, renderer-supported scalar expressions and curve-lane selection, shared smooth page turns and follow-off,
  middle-drag panning, cursor-anchored zoom with explicit headless frame pumping, drag scrubbing on both rulers without viewport-induced seeking, forward/reverse edge scrolling with explicit headless timer pumping, Escape rollback,
  combined anchor/vibrato dragging and single-step undo,
  owned utility/dialog windows, nested dialog cancellation and invalid-input retention, compact desktop knob horizontal movement and single-step undo with a simulated relative-pointer provider, bottom-lane full expansion/collapse, wide-inspector central-space constraints, Close/Escape navigation, idle and queued Tools cancellation, mode-dependent hints, visible log actions, repeated navigation, header reattachment, panel
  persistence, mixer minimum-size visibility, in-place Save and template identity, saving during a captured drag, renderer undo/redo labels,
  inline track-name, lyric and phoneme commits and cancellation, recent-project selection and Enter-to-open, compact confirmation spacing, readable batch/property window heights, searchable phonemizer choices, renderer dropdown undo, selected-only batch lyrics,
  full pane collapse, one-click vibrato enabling and handle expansion, advanced phoneme defaults, separate mixer ownership, Home native-close return,
  drop-workflow routing and all three Stop viewport-return behaviors,
  avatar reuse, phoneme selection retention, singer/dependency search selection, saved/dirty window titles,
  close-time drag cancellation, queued-tool cancellation and companion-file handling.
- Dialog audit: 29 real-view-model cases across all 26 popup control types, light/dark captures, minimum window sizes, footer reachability, property/batch tabs and setup steps. The short preset list shows both choices, while a 40-choice list scrolls. Shared export-dialog footers remain reachable in mobile portrait and landscape layouts. Native window placement and animation cadence remain unverified.
- Playback/selection probe: an isolated 1,200-note part with 30 single-note selection changes dropped from approximately 682 MB / 5.7 seconds to approximately 90 MB / 1.7 seconds on this machine in the latest run. The selection loop emitted no project edits, seek or pre-render notifications. This is an offscreen allocation/interaction result, not proof that every reported audio dropout is fixed. Captured piano-ruler seeks and playback synchronization no longer trigger page-follow at the right boundary. Desktop status routing, hidden toast overlay, background messages and restoring the latest hint passed; expiry was driven explicitly in the headless host.
- Decoration-template regression: explicitly constructing and applying the drawn-window template reproduced the `opum:WindowChrome` namespace exception. A resolved TemplateBinding replaces the reflection path. Template creation, default title visibility, hiding/restoring the title and fullscreen caption, and retaining caption controls pass in the isolated probe.
- Shared mobile fixes: last-selected-part editing, preset selection ordering and stale input replacement, consistent property section labels, menu hover states and mobile reset controls in both phoneme modes. Desktop resets use the phoneme context menu; mobile simple and advanced editing share the drag-reset circle. Cancelling capture, switching modes or replacing the editing part rolls back an unfinished phoneme drag. Timing lines remain editable when alias mapping is missing. Desktop phoneme input covers the actual labels; mouse hit regions are smaller than touch targets. Vibrato icon clicks toggle vibrato in one undo group. Desktop omits the drag-reset circle and reset toolbar; the mobile presentation retains them.
- Actual synthesis: builtin worldline and single-note straycat-rs/convergence with a generated vowel
  voicebank produced non-silent WAV output through the existing rendering pipeline, including spaced
  and non-ASCII paths. Concurrent straycat-rs `G0` invocations reproduced the reported EOF panic (1 of
  36 invocations); the upstream source cache lock covers copying but not external execution. A separately
  reviewed upstream change is required to address that race.

Native timer cadence and screen placement, title-bar hit-testing/caption buttons, cursor recentering and physical mouse locking, native file-dialog interaction, OS drops, physical touch/magnifier gestures, Android device behavior,
Linux/macOS interaction, Wine execution and general external resampler/wavtool compatibility remain
unverified. Offscreen probes are disposable verification artifacts, not a new permanent test suite.
