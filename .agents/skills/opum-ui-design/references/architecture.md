# UI ownership decisions

All code paths are repository-relative. `T` below means
`OpenUtauMobile/Themes/OpenUtauMobile/`.

## Choose the owner (contract)

| Requested change | First owner to inspect | Boundary |
| --- | --- | --- |
| A standard control's structure or reusable hover/focus/pressed/disabled behavior | `T/Controls/<Control>.axaml`, helper themes and `ControlThemes.axaml` | Read the whole affected template/state family, not just the matching selector. Preserve parts and behavior. |
| Shared role such as dialog action, top-bar action, FAB or option entry | `T/Styles/Components/` and its tokens | Compose the base theme using role colors/metrics; do not fork its interaction state. |
| Feature-specific visual family or gesture | `OpenUtauMobile/Controls/Styles/`, `Views/Styles/`, corresponding control/view and tokens | Feature loads its styles. Global theme must not import feature-owned resources. |
| One section's alignment, gap or drawing offset | That AXAML/layout or drawing implementation | A local value/style is valid; promotion needs a shared meaning or coordinated change contract. |

**Important path distinction:** `T/Controls/` contains base theme dictionaries;
`OpenUtauMobile/Controls/` contains application controls, popup views and domain interactions.
Putting a file in the latter does not make it a global theme component.

For loading, resource ownership or a framework upgrade, read the
[theme README](../../../../OpenUtauMobile/Themes/OpenUtauMobile/README.md)
and `OpenUtauMobile/App.axaml` / `T/OpenUtauMobileTheme.axaml`.
The local theme is complete; DialogHost and IconPacks integrations are separate.
Do not add Fluent/Semi dependencies to fix a missing style. Trace includes, keys and
resource resolution first.

## Choose a value owner

Consult the current ownership table in the
[token README](../../../../OpenUtauMobile/Themes/OpenUtauMobile/Tokens/README.md)
when adding/changing a shared value, not for every local layout edit.

| Kind | Use when | Avoid |
| --- | --- | --- |
| Foundation token | Existing app-wide spacing, shape or motion role expresses the intent | Quantizing typography, strokes, time or musical duration onto a spacing grid |
| Semantic token | Typography, actual icon size, touch target or state-opacity role | Treating icon visual size as its hit target; imposing touch size on every editor control |
| Component token | A shared component owns a coordinated dimension/behavior contract | Borrowing unrelated Card/Navigation dimensions for Dialog chrome |
| Feature-local token | Related feature dimensions change together, or AXAML and C# share a contract | Promoting domain geometry into global theme tokens |
| Local style | Several elements in one feature share role/layout setters | Adding a C# token that merely mirrors each setter |
| Local literal | Independent one-off geometry/inset/offset, zero or design-preview size | Replacing every repeated number with a semantically unrelated token |
| Dynamic semantic color | UI paint must follow the generated palette | Hardcoded theme paint or taking a one-time brush snapshot |

Search for the proposed **role and consumers**, not just its numeric value. Ask which
consumers should change together; if none, keep it local. A new cross-feature component
needs an actual shared behavior/role absent from the existing ones, not just two similar grids.

### Color decisions (contract)

Use `DynamicResource Sem.Color.*` for brushes, `Sem.Value.*` for properties requiring
typed `Color` (e.g. shadow color). Select a surface/emphasis role with its corresponding
foreground, rather than compensating with ad-hoc opacity. Feature data colors, such as
track colors or color-picker swatches, are not automatically semantic UI paint.

For palette/state-color changes only, follow `T/Accents/ControlResources.axaml`
(and `T/COLOR_ROLES.json`) to `T/Runtime/Generation/ThemeGenerator.cs`.
`T/Runtime/Resources/SemanticThemeResources.cs` provides startup/designer variants;
`ThemeResourceBridge.cs` updates live per-variant palettes. Preserve Default/Light/Dark
resolution and nested `ThemeVariantScope`; do not replace them with one global palette.

## Base controls: what to reuse

- **Contract — buttons:** `T/Controls/Button.axaml` owns a Foreground-derived state
  layer, keyboard focus ring, one disabled fade and action/disabled cursors. Its content
  drives height; shared Dialog actions add their own touch-sized minimum. Content cards
  can remain ordinary Button content. Do not reintroduce local reusable
  `ContentPresenter` background/opacity overrides.
- **Current pattern — choices:** `ToggleButton.axaml` builds on Button;
  `RadioButton.axaml` defines `OpumChoiceCard` for radio-group cards without a radio dot.
  `Controls/Styles/ExportAudioPopup.axaml` selects that theme and supplies feature geometry.
  Reuse it for the same selection behavior, not for unrelated navigation cards.
- **Contract — inputs:** inspect `TextBox.axaml`, `ComboBox.axaml` or
  `NumericUpDown.axaml` with their helper themes before changing chrome. Preserve
  validation, read-only, selection, placeholder, popup and text-cursor behavior as applicable;
  InputTokens are not permission to replace an input with a button-like template.
- **Current pattern — tabs/sliders:** ordinary TabControl/TabItem and Slider already use
  local full themes. Inspect `TabItemTokens` / `SliderTokens` and all placement/orientation
  branches before changing shared dimensions. A feature tab-content inset does not belong
  to TabItem's indicator contract. A DawKnob is a domain control, not a Slider skin.

## Layout decisions (current recommended pattern)

Sibling gap belongs to `Spacing`, `RowSpacing`, `ColumnSpacing` or WrapPanel item/line
spacing; container inset belongs to `Padding`. Reserve `Margin` chiefly for section
separation, overlays and optical offsets. This is an ownership preference, not a ban
on legitimate template margins or feature geometry.

Keep `ScrollViewer.Padding` zero in the current implementation: put a padded Border
inside for scrolling inset, outside for fixed inset. Preserve finite measurement and
check the last content at maximum offset. Reassess this implementation-specific constraint
against actual layout behavior after framework changes, rather than spreading workarounds.

## Modal decisions (contract)

For modal work, verify the contracts below against
[DialogShell](../../../../OpenUtauMobile/Controls/DialogShell.cs),
[PopupDialogControl](../../../../OpenUtauMobile/Controls/PopupDialogControl.cs) and
[Dialog styles](../../../../OpenUtauMobile/Themes/OpenUtauMobile/Styles/Components/Dialog.axaml).
Select a matching modal example from [canonical patterns](canonical-patterns.md).

- Use PopupDialogControl with root `PopupDialogRoot`, one DialogShell and business content.
  Shared styles own header, close action, body inset and footer roles; the view owns commands.
  A non-dismissible operation need not expose CloseCommand; immediate selection need not
  have a footer. Do not invent cancellation or confirmation merely to fill slots.
- Choose width preset independently of at most one semantic height profile; omit a profile
  for content-driven sizing. Read current profile values in the token README instead of
  copying another dialog's height. Root dimensions cover the whole dialog.
- Host inset and base-class height coercion preserve the viewport and restore requested
  sizes on enlargement. No per-popup viewport subtraction or resize subscription.
- Use explicit `DialogActionRow` / `DialogActionRows`; visible actions share each declared
  row equally. Business grouping stays fixed on resize while labels wrap. `DialogActions`
  is a legacy one-row alias, not an adaptive wrapping panel.
- Footer role classes are `DialogPrimary` / `DialogDestructive`; use `DialogAction` for
  generated/out-of-row actions. Footer spacing is not per-button Margin. Keep footer outside
  body scrolling and hide the footer root when absent.
- `Views/MainView.axaml` owns the host popup template: its outer Padding binding carries
  host inset. The shell's translucent background is separate from opaque interactive content.
  Do not apply glass opacity to the entire dialog or duplicate its background in each view.
