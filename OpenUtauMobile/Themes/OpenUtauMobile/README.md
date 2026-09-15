# OPUM independent control theme

`OpenUtauMobileTheme.axaml` is the only base-theme entry point. It owns complete
`ControlTheme` templates, not overrides on an installed Fluent theme. Neither
`Avalonia.Themes.Fluent` nor the unused `Semi.Avalonia` package is referenced.
DialogHost and IconPacks compatibility styles remain separate integrations.

## Source and coverage

The starting point is the entire XAML theme from [Avalonia 12.1.0](https://github.com/AvaloniaUI/Avalonia/tree/12.1.0/src/Avalonia.Themes.Fluent),
including all 79 control dictionaries, their resource graph, invariant strings,
and the optional compact-density dictionary. `UPSTREAM.json` records the exact
commit, original file hashes, resource keys, named parts and selectors. The MIT
license is retained in `licenses/Avalonia.Themes.Fluent.MIT.txt` at repository root.
Upstream explanatory comments are retained in the adapted source.

**Fallback means local source**, not another loaded theme: uncommon controls keep
their upstream templates in this same directory. They can be edited here and do
not require the Fluent assembly. Default density is used; the imported
`DensityStyles/Compact.axaml` is not automatically loaded.

| Family | Owned implementation / preserved contracts |
| --- | --- |
| Button, RepeatButton, ToggleButton, HyperlinkButton | Shared Button template; checked, indeterminate, visited and accent roles |
| DropDownButton, SplitButton / ToggleSplitButton | Shared button states; arrow, primary/secondary buttons and flyout tags retained |
| TextBox, AutoCompleteBox, NumericUpDown / ButtonSpinner | TextPresenter, IME/preedit, selection, caret, clear/reveal buttons, editable/read-only/error states, inner content, desktop/mobile context flyouts |
| CheckBox, RadioButton, ToggleSwitch | Checked/unchecked/mixed/disabled states, glyphs, switch drag/knob transitions and on/off content |
| ComboBox / ComboBoxItem | Editable input, placeholder, popup, light dismiss, item presenter, selection and validation |
| ListBox, TreeView, menus, tabs | Item/container themes, virtualization, expand/collapse, selection, access keys and all tab placements |
| Slider, ScrollBar, ScrollViewer | Both orientations, reversed tracks, ticks, drag/step buttons, inertia and auto-hide |
| ProgressBar | Determinate and indeterminate templates/animations, both orientations and progress text |
| Other controls | Calendar/date/time pickers, flyouts/tooltips, validation adorners, notifications, split views, managed file chooser, window decorations, Avalonia 12 pages/command bars/table views and refresh controls remain fully sourced locally |

## Where to edit

- `Controls/*.axaml`: complete control templates, private helper themes and nested
  state styles. `Controls/ControlThemes.axaml` imports every control dictionary.
- `Accents/BaseResources.axaml`: shared brushes, converters and baseline metrics.
- `Accents/ControlResources.axaml`: per-control brush definitions. These are edited
  **in place** to use semantic colors. `COLOR_ROLES.json` indexes these mappings.
- `Runtime/Generation/ThemeGenerator.cs`: generated semantic colors and state
  composites. `Sem.Color.*` is a brush; `Sem.Value.*` is its typed Color counterpart.
- `Runtime/Resources/SemanticThemeResources.cs`: complete startup/designer palettes
  for Default, Light and Dark. Default is essential for brushes instantiated from
  upstream Default dictionaries before the first variant-change event.
- `Runtime/Resources/ThemeResourceBridge.cs`: mutable per-variant palettes. Seed
  changes update existing controls and preserve mixed Light/Dark ThemeVariantScope
  trees. Legacy aliases are retained only for older consumers.
- `Tokens/`: shared metrics; see [ownership rules](Tokens/README.md).
- `Styles/Components/`: application components and button roles/layout, not base
  control templates. Page/feature styles stay with their existing owners.

## Button contract

Normal/hover/pressed/keyboard-focus/disabled states share one implementation.
The state layer uses the button's **own Foreground** at 8% / 12%, rather than
replacing semantic backgrounds with a system accent. Content keeps its original
contrast. Keyboard focus has a separate 2dp outline; disabled content fades once
to 38%, suppresses interaction layers, and uses an arrow cursor. Enabled action
controls use a hand; text input retains an I-beam. Specialized scrollbar/splitter
cursors remain owned by their own templates.

`Primary`, `Secondary`, `accent`, `DialogPrimary`, `DialogDestructive`, FAB,
navigation, card and icon-button classes retain their public names. Existing
commands, parameters, bindings and explicit compact editor sizes are retained.
The singer-card and track-color buttons now use ordinary content rather than
replacing Button.Template. Reusable states must not be reintroduced as page-level
`/template/ ContentPresenter` patches or local cursor assignments.

Normal button padding is 16,10; content determines its height. This intentionally
does not impose a new minimum on compact editor controls. Dialog actions retain
their 48dp minimum. Shape/layout differences between component roles are deliberate.

## Verification and maintenance

Build with `AVALONIA_TELEMETRY_OPTOUT=1`; no unit tests are created or run.
The migration was checked using an isolated Avalonia headless runtime diagnostic:
all 122 keyed themes resolve, common templates measure, 70 button role/state/variant
combinations are inspected, and Light/Dark rendering and live seed changes are
reviewed. Runtime diagnostic details are in [VERIFICATION.md](VERIFICATION.md).

When updating Avalonia, compare the pinned source contract in `UPSTREAM.json`
against the new upstream templates, especially required parts, bindings,
pseudoclasses and private helper themes. Do not replace this directory with a
short list of global setters. Retain upstream functionality while changing the
template/resource owner directly. Preserve zero ScrollViewer.Padding; use content
Borders for scrollable insets. Device touch/IME/screen-reader and real populated
page acceptance remain manual checks rather than claims made by build success.
