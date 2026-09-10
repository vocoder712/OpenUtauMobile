# UI metrics and layout ownership review — 2026-09-10

Branch: `feature/import-track`. Baseline: `0ce4c84781b86424c02930d230ec2dbc87398272`.
The initial working tree was clean. Core, Plugin, FluentTheme and theme loading
order are unchanged. No ControlTheme rewrite or unit tests are part of this work.

## Token changes

| Layer | Change | Consumers / intent |
| --- | --- | --- |
| Foundation / Layout | SpaceSM=12, SpaceL=24, InsetSM=12, InsetL=24 | Intermediate detail gaps, section spacing and uniform insets; reuse existing 4/8/16 steps |
| Foundation / Shape | CornerFull | Circular/pill geometry independent of control size; existing 4/8/12/16 roles replace arbitrary 6/10/14/18 corners |
| Foundation / Motion | DurationShort=150ms, DurationExit=167ms, DurationMedium=200ms, DurationProgress=500ms | Expansion, navigation, progress and toast; existing 250/300/375ms roles retained |
| Semantic / Typography | TitleL=22, TitleM=16, TitleS=14 | Dialog title, app-bar title, secondary-tab label; existing body/label roles used for descriptions and captions |
| Semantic / Icon | Small=16, Compact=20, Standard=24 | Actual icon visuals only; former 18/22 icons become 20/24 |
| Semantic / Interaction | TouchTarget=48 | Existing 48dp min-height contracts plus Home delete action (34→48); no blanket enlargement of editor controls |
| Semantic / State | ContentHover=.85, ContentPressed=.70 | Direct opacity feedback on legacy page actions; distinct from .08/.12 state layers and dialog's .87/.56 content feedback |
| Component / FAB | Size=56 and rest/hover/pressed shadow metrics | Preserve all three original elevation profiles |
| Component / OptionEntry | MinHeight=56, IconContainerSize=36, TrailingInset=12 | Shared About/Options rows; actual icon size remains a separate semantic role |
| Existing components | Card/Input/Dialog shape references; Dialog title/touch; Slider touch; TabItem title role | Reuse the existing entry points rather than parallel aliases |
| Feature-owned | ToastTokens, EditorTokens, PhonemeCanvasTokens | Distinct overlay elevation, entry offset, handle-rest opacity and selection timing |

## Main layout changes

- All legacy DialogBody consumers use a padding Border. Named controls and
  their DataContexts are retained inside the wrapper; attached placement and
  visibility move to the wrapper when required.
- DialogActionRow has a styled Spacing property. It divides remaining width
  equally **after** subtracting gaps, clamps gaps under extremely narrow widths,
  and retains explicit business row grouping. DialogActionRows omits empty rows
  from spacing. The existing 16dp footer inset remains; the extra 4dp perimeter
  previously contributed by each button is intentionally removed.
- Singer detail: 6+6 column margins become one 12dp Grid gap; cards use parent
  spacing, hero icon/text use ColumnSpacing, fields use StackPanel spacing.
- About/Options: parent Border owns the trailing inset, Grid owns the text-to-
  arrow gap; icon column 52→44 plus 8dp gap preserves the title start position.
  About build-metadata rows use parent spacing instead of repeated top margins.
- Settings: category stacks own card gaps, title/description stacks keep their
  declared spacing, path rows own column gaps, navigation-label wrappers own
  trailing inset. Wrapped tags use panel item/line gaps.
- Dependency manager: the Border inside the scroll container owns page inset; search/header grids own
  column gaps, card grids own row gaps, ItemsPanels own card spacing, and tag
  WrapPanel owns horizontal/vertical gaps.
- Batch edit: parent Grid owns row/column gaps, parameter stack owns label/input
  gap; action and icon margins no longer accumulate across columns.
- Import/mapping: the Border inside ScrollViewer owns body inset, source/mapping Border owns inner
  inset. EditorMore uses parent action spacing and a tab-content padding wrapper.
- File save: footer Grid owns row/column gaps. Phonemizer item templates wrap
  text in padded containers. Color picker uses WrapPanel gaps rather than child
  margins; its 34dp swatch becomes 32dp and cells use 4dp-aligned sizes.
- Top bars own title/action spacing in their content layout rather than the
  shared title TextBlock's Margin. Home recent/delete and SingerList text-cell
  insets move into containers. Classic setup headers use padded containers.

## Intentionally retained values and margins

- Font roles 11/14/22 and 22px line heights are typography, not spacing. Some
  feature-local typography remains literal: this is not a complete type redesign.
- 130/150/167/200/250/300/375/500ms durations and the 16ms timer interval are time,
  not dp. Playback blink/viewport physics constants and toast display lifetime
  retain their existing behavioral owners; musical note duration is not UI motion.
- 1dp outlines/dividers, 2dp tab/progress indicators, 3dp progress strokes,
  half-height indicator corners (including TrackHeader's 3.5), slider joins and
  path data remain drawing geometry. Partial corners preserve the intended edge.
- FAB shadows keep blur 10/14/6, offsets 3/5/1 and opacity .24/.28/.20. Toast
  keeps 14/4/.35; the rendering hint keeps 8/2/.30. Elevation is tokenized but
  not quantized to the spacing grid.
- Retained Margin categories: section separation (category headings, danger
  section and help notes), full-bleed negative dividers, label-aligned separators,
  layered editor/tool overlays, scroll/template optical offsets and external
  item-container spacing not covered by this pass. Slider and FAB/TabItem
  TemplateBinding-based insets are deliberately left within existing templates.
- One-off on-grid padding and sizing remain local rather than generating
  catch-all aliases. Splash's 48dp hero separation and zero spacing are deliberate.
- Remaining Margin uses are inventoried individually in the source audit. This
  pass does not claim every remaining literal or legacy margin was eliminated.

## Verification

- Parse and review all 82 AXAML files; compare the multiset of bindings, resource
  references, x:Name/Name, x:Class and x:DataType against the baseline.
- Baseline→modified: nonzero Margin attributes/setters 180→86; literal spacing
  174→3; literal column/row spacing 10/2→0/0; literal corners 110→16; literal
  font sizes 219→125; literal AXAML durations 4→0. Counts include local geometry
  and intentional zero values, not just candidates for removal.
- No lost/added binding, name or resource-reference entries; no unused tokens.
- Build Windows host with telemetry disabled; source audit and git diff --check.
  Build verification is not runtime visual verification. No unit tests run.
- Device acceptance remains manual: narrow/wide layout, long localized labels,
  light/dark themes, disabled/pressed/focused actions, hidden/dynamic footer rows,
  navigation collapse, picker wrapping, import scrolling and popup resize.


## Scroll extent correction — 2026-09-10

The owner reported unreachable bottom content and approved continuing on the
uncommitted metrics changes. The previous advice to move content inset directly
to ScrollViewer.Padding is superseded: leave that property at zero and put the
padding on a Border inside the scrollable content. This preserves layout
ownership without returning to child Margin or rewriting Fluent ControlThemes.
For fixed viewport spacing, use an outer padded Border instead.

All 12 explicit nonzero ScrollViewer paddings across 10 AXAML files were moved,
including the pre-existing OptionsView padding. Bindings, named controls,
visibility, height constraints and token values are retained.

An actual Avalonia.Controls 12.1.0 layout diagnostic used ten 60dp rows, a 200dp
viewport and inset (16,20,16,24), then requested the maximum scroll offset:

| Structure | Extent | Viewport | Offset | Last row bottom | Hidden bottom | Trailing space |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Presenter.Padding | 556 | 200 | 356 | 264 | 64 | -64 |
| Content Border.Padding | 644 | 200 | 444 | 176 | 0 | 24 |

The probe invokes the real Measure/Arrange implementation without a window; it
is a layout diagnostic, not a unit-test suite or full device visual acceptance.
The exact historical first-half-year fix has not been identified; do not claim
that this change reproduces that historical fix. Device bottom-scroll checks
are still needed for any symptoms remaining after the padding correction.
