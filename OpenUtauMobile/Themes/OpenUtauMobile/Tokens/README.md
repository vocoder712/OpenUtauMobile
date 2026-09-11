# Static UI token ownership

## Start here: where should a value go?

Paths below are relative to `OpenUtauMobile/`.

| Value / intent | Owner | Examples |
| --- | --- | --- |
| App-wide spacing, corners, animation durations | `Themes/OpenUtauMobile/Tokens/Foundation/` | `LayoutTokens`, `ShapeTokens`, `MotionTokens` |
| Meaningful text/icon/touch/state roles | `Themes/OpenUtauMobile/Tokens/Semantic/` | `TypographyTokens`, `IconTokens`, `InteractionTokens`, `StateOpacityTokens` |
| Shared component dimensions and behavior | `Themes/OpenUtauMobile/Tokens/Components/` | `DialogTokens`, `InputTokens`, `CardTokens`, `FabTokens`, `OptionEntryTokens`, `SliderTokens`, `TabItemTokens` |
| Control-feature internals | `Controls/Tokens/` | `BatchEditTokens` parameter widths, `PhonemeCanvasTokens`, `EditModeSwitcherTokens`, `ToastTokens` |
| Page-feature internals | `Views/Tokens/` | `EditorTokens`, `HomeTokens`, `DependencyManagerTokens` |
| Theme-dependent color | `Themes/OpenUtauMobile/Runtime/Generation/ThemeGenerator.cs` | `Sem.Color.SurfaceContainerHigh`; use `DynamicResource`, not a static color constant |
| One-off local geometry / zero / design-preview size | Owning AXAML or local style | A drawing's radius or `d:DesignHeight`; no token solely to hide a literal |

Before adding a token, search for its **role**, not merely its number. Reuse an
existing component contract; shared styles apply its values to views. Do not put
dialog frame sizes in BatchEdit/Editor tokens, or move feature-only values into a
global catch-all file. Equal values with different meanings need not be coupled.

## Dialog sizing: choose a profile instead of repeating dimensions

Numbers live in [`Components/DialogTokens.cs`](Components/DialogTokens.cs), styles
in [`../Styles/Components/Dialog.axaml`](../Styles/Components/Dialog.axaml), and
viewport coercion in `Controls/PopupDialogControl.cs`. These are complementary
owners, not three places to independently configure the same size.

| Root class (choose at most one height profile) | Min / max height, DIP | Use |
| --- | --- | --- |
| `PopupDialogRoot` only | 0 / 640 | Content-driven dialogs without reserved minimum space |
| `DialogHeightCompact` | 180 / 320 | Short text-entry forms |
| `DialogHeightRegular` | 280 / 640 | Regular editing forms |
| `DialogHeightList` | 280 / 600 | Scrollable selection lists |
| `DialogHeightExpanded` | 400 / 640 | Multi-action/workspace dialogs |

```xml
<UserControl ... Classes="PopupDialogRoot DialogHeightExpanded">
    <dialogs:DialogShell ... />
</UserControl>
```

For example, EditorMore and BatchEdit use Expanded; LyricEdit, PhonemeEdit and
TrackRename use Compact; BulkLyricEdit uses Regular; Singer, Renderer, Phonemizer
and TrackColor pickers use List. Unprofiled dialogs retain content-driven sizing.
These are application choices, not MD3-prescribed heights. Changing one token
updates every consumer of the corresponding profile.

All dimensions cover the complete dialog, including header and footer. Fixed
height is a deliberate exception: set root `Height` and `MaxHeight` from the
same semantic token, and avoid a conflicting minimum profile. A genuinely unique
feature size belongs beside that feature; a reusable dialog size belongs here.
Local/bound `MinHeight`/`MaxHeight` values override profile defaults. Effective
minimum and maximum shrink to preserve the host's 24dp viewport margins and
recover their original values when the window grows.

Width selection remains the independent C# `PopupDialogWidthPreset`:
Compact/Regular/Wide maxima are 360/420/560, all defined in DialogTokens.
Do not use height profile names to infer or change width.

For composition and layout see [DialogShell](../../../Controls/DialogShell.cs),
[PopupDialogControl](../../../Controls/PopupDialogControl.cs) and the shared
[Dialog styles](../Styles/Components/Dialog.axaml). Source examples:
[TrackRenamePopup](../../../Controls/TrackRenamePopup.axaml) for a short form,
[ImportTracksPopup](../../../Controls/ImportTracksPopup.axaml) for scrolling content,
and [OptionConfirmPopup](../../../Controls/OptionConfirmPopup.axaml) for explicit
dynamic action rows.

## Rules

- There is no commitment to a complete design-token specification. Keep only
  consumed definitions; remove dead alias chains as well as dead leaves.
- Foundation: reusable spacing, shape and motion values. Semantic: typography
  roles and state opacity. Components: existing shared control contracts.
- Features own their values in `Controls/Tokens`, `Views/Tokens` or their local
  AXAML/style. Import flow and editor mode switchers are feature-local sharing,
  not application-wide components.
- A local literal is preferred for a one-off margin, padding, zero or independent
  piece of geometry. A local style Setter already centralizes its value; it does
  not need a one-to-one C# wrapper. Keep typed tokens where AXAML and C# share a
  contract or multiple dimensions must change together.
- Equal numbers do not imply shared semantics. Do not tie hit targets, visual
  sizes, viewport margins, font sizes and list limits to one spacing number.
- Typography has one public role entry point (`TypographyTokens`), not mirrored
  Base/Sem aliases. Do not classify arbitrary literal font sizes by value alone.
- `StateOpacityTokens.Hover/Focus/Pressed` describe shared state layers. Dialog
  actions now use the same Button ControlTheme instead of fading their content.
- Settings and ThemeColorPicker independently own their specialized styling.
  Do not introduce a shared AccentBtn/Placeholder token contract between them.
- Global tokens and theme includes must not depend on feature tokens/styles.
  Static definitions use `x:Static`; dynamic color keys and resource lookup are
  unchanged. The independent control theme is documented in
  `../README.md`; shared Button/Input/Slider/TabItem metrics are consumed there.
- Use 8dp baseline spacing and 4dp details without rounding typography, motion,
  strokes or drawing geometry to that grid. Compact editor targets retain
  their separate sizing contracts.

## Layout ownership

- Sibling gaps belong to `Spacing`, `ColumnSpacing`, `RowSpacing`, or wrapping
  panel `ItemSpacing`/`LineSpacing`. Container insets belong to `Padding`.
  Keep Margin for isolated section separation, overlays and optical offsets.
- Exception: keep `ScrollViewer.Padding` zero on Avalonia 12.1.0. Use a padded
  content Border inside it so inset contributes to the scroll extent, or an
  outer Border for fixed viewport inset. Presenter padding can clip the last
  content even at maximum scroll offset; a successful build does not catch this.
- Dialog body insets are `Border.DialogBodyBorder`; DialogActionRow and
  DialogActionRows own their gaps. Never restore per-action margins.

## Verification

Use source/value comparison and builds, not unit tests. Set
`$env:AVALONIA_TELEMETRY_OPTOUT='1'` before `dotnet build`.
Device visual acceptance remains separate: review light/dark themes, interaction
states, localized labels, dialog footers and popup host resize/re-attachment.
