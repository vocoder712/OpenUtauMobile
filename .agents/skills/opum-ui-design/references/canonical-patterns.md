# Select a canonical source

Paths below are relative to `OpenUtauMobile/`. Read the matching entry only, then the
current source and relevant bindings/code-behind. These are **examples/current patterns**,
not a catalog or permanent specification of feature layouts. If no entry matches, search
the feature's control usages and nearest behavioral sibling before creating an abstraction.

## Navigation/options page

**Source:** `Views/OptionsView.axaml`; role owners
`Themes/OpenUtauMobile/Styles/Components/TopBar.axaml` and `OptionEntry.axaml`.

Use for a titled, scrollable page of command-driven destinations. Reuse TopBar,
TopBarBtn/TopBarTitle and OptionEntry roles, semantic paint, DynamicResource labels,
parent-owned gaps and the padded content Border inside ScrollViewer.
The icon/title/subtitle/trailing-action composition is a useful starting point, not a
new reusable-control requirement. Separator offsets, chevron size, truncation choices,
individual commands and page inset are **feature-specific**, not global metrics.

## Small editing modal

**Source:** `Controls/TrackRenamePopup.axaml`, its `.axaml.cs`,
`ViewModels/TrackRenamePopupViewModel.cs`.

Use for a small input with cancel/confirm. Reuse PopupDialogControl width selection,
root height profile, DialogShell, DialogBodyBorder and shared TextBox theme.
Do not copy the domain binding, literal font size, or profile without checking the new
content. For new footers use explicit `DialogActionRow`, not this example's compatibility
`DialogActions` spelling. See architecture's modal section for the controlling contract.

## Scroll-heavy modal / generated decisions

Choose one:

- `Controls/ImportTracksPopup.axaml` + `.axaml.cs` and `ViewModels/ImportTracksViewModel.cs`:
  wide, content-driven dialog with scrolling body and fixed footer; padded Border is inside
  ScrollViewer. Reuse that measurement/composition pattern. Nested import source/track
  models, selection commands, content inset and warnings belong to import, not Dialog chrome.
- `Controls/OptionConfirmPopup.axaml` and `ViewModels/OptionConfirmPopupViewModel.cs`:
  dynamic decision actions. Reuse typed two-dimensional option rows and nested ItemsControls
  with DialogActionRows/Row, parent command binding and semantic action classes.
  Preserve result/cancel behavior. The message's local scroll cap is an **example**, not a
  recommended global dialog height or new token.

## Browser/list picker

**Source:** `Controls/FilePickerPopup.axaml`, `.axaml.cs`,
`Controls/Styles/FilePickerPopup.axaml`, `ViewModels/FilePickerPopupViewModel.cs`.

Use for hierarchical browsing or mode-dependent selection, not every short choice list.
Reuse separation of navigation, loading/empty/error states, scrollable entries and a footer
whose root binds HasFooter; role-only entry styles use base button interaction.
Folder/open/save/multi-select modes, path editing and filename validation are **feature
behavior**. Do not reproduce that state machine for a simple chooser or force immediate
selection into a confirm footer. Trace existing picker callers before adding another API.

## Standard tabs or sliders inside a feature

**Source:** `Controls/EditorMorePopup.axaml` (ordinary TabControl/TabItem);
`Controls/ThemeColorPickerDialog.axaml` (ordinary Sliders).

Use to see standard controls composed without feature-level template replacements.
Read only the relevant tab or slider section, then the corresponding theme if changing
its state/geometry. Tab-content Padding and color-channel ranges/bindings are **feature
values**, not reasons to alter shared indicators/thumbs or couple Settings AccentBtn
to this dialog's swatches.

## Editor controls and non-modal overlays

**Source:** `Views/EditorView.axaml` + `.axaml.cs`; choose the affected child only:

- Mode actions: `Controls/PianoRollEditModeSwitcher.axaml`,
  `Controls/Styles/EditModeSwitcher.axaml`, `Controls/Tokens/EditModeSwitcherTokens.cs`.
  Reuse the feature-shared compact sizing and standard Button states; mode semantics and
  placement belong to the editor. **Feature exception:** compact buttons are not governed
  by dialog action minimums; do not enlarge every Button globally.
- Context actions: `Controls/CollapsibleContextMenu.axaml` + `.axaml.cs` and its feature
  style. Reuse only for the editor's collapsible action capsule. Despite its name this is
  not Avalonia ContextMenu and not a modal DialogShell. Ordinary context menus/dropdowns
  should start with their own base ControlThemes.
- Parameter/piano-roll surfaces: `Controls/PhonemeParamPanel.axaml` + `.axaml.cs`,
  `Controls/ParameterCanvas.cs`; inspect coordinate bindings and pointer/magnifier routing
  in EditorView before changing layout. Their domain geometry and editing gestures are
  not shared theme tokens or generic button interaction.
- Mixer input: `Controls/TrackHeader.axaml` and `Controls/DawKnob.cs`, with
  `Themes/OpenUtauMobile/Styles/Components/DawKnob.axaml`. Reuse when the same knob
  interaction is wanted; volume/pan conversion and ranges remain mixer-owned.

For noninteractive notifications, inspect `Controls/ToastOverlay.axaml` and its code-behind
plus its placement above DialogHost in `Views/MainView.axaml`; do not turn notification
layout into dialog chrome or intercept editor input. Its elevation/timing is feature-owned.
