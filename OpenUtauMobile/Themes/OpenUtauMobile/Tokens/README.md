# Static UI token ownership

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
- `StateOpacityTokens.Hover/Focus/Pressed` describe state layers; Dialog action
  content opacity is a separate component contract with its existing values.
- Settings and ThemeColorPicker independently own their specialized styling.
  Do not introduce a shared AccentBtn/Placeholder token contract between them.
- Global tokens and theme includes must not depend on feature tokens/styles.
  Static definitions use `x:Static`; dynamic color keys and resource lookup are
  unchanged. FluentTheme and ControlTheme rewrites are not part of this work.
- Token-only moves preserve effective values. The subsequent metrics cleanup
  explicitly allows grid alignment: use 8dp baseline spacing and 4dp details,
  without rounding typography, motion, strokes or drawing geometry to that grid.
  The Home delete action now has a 48dp target (formerly 34dp); other compact
  editor targets retain their separate sizing contracts.

## Layout ownership and metrics follow-up (2026-09-10)

- Sibling gaps belong to `Spacing`, `ColumnSpacing`, `RowSpacing`, or wrapping
  panel `ItemSpacing`/`LineSpacing`. Container insets belong to `Padding`.
  Keep Margin for isolated section separation, overlays and optical offsets.
- Exception: keep `ScrollViewer.Padding` zero on Avalonia 12.1.0. Use a padded
  content Border inside it so inset contributes to the scroll extent, or an
  outer Border for fixed viewport inset. Presenter padding can clip the last
  content even at maximum scroll offset; a successful build does not catch this.
- Dialog body insets are `Border.DialogBodyBorder`; DialogActionRow and
  DialogActionRows own their gaps. Never restore per-action margins.
- LayoutTokens adds the consumed 12dp intermediate step and 24dp large step,
  with matching uniform insets. ShapeTokens includes a size-independent full
  corner for circular/pill visuals. Composite one-off padding remains local.
- IconTokens describes 16/20/24dp visuals; InteractionTokens independently
  describes a 48dp minimum touch target. Typography adds Title L/M/S roles;
  Dialog and TabItem retain their component entry points referencing those roles.
- MotionTokens owns 150/167/200/250/300/375/500ms durations. Toast transition
  delays reference the same tokens as their transitions. Phoneme selection's
  130ms interpolation and 16ms frame interval stay feature-owned.
- ContentHover/ContentPressed are shared direct-content feedback, not state
  layers. Dialog action content opacity remains its independent component
  contract. Settings and ThemeColorPicker still own their specialized styles;
  only generic foundation/semantic defaults are shared.
- FabTokens owns size and state-specific elevation. OptionEntryTokens owns
  shared navigation-row metrics. Toast and editor overlay elevation remain
  beside their owners rather than being equated with FAB shadows.
- See `.agent/context/UI_METRICS_REVIEW.md` for scope, retained exceptions,
  verification and the device acceptance checklist.

## Review map for the 2026-09-10 migration

1. Remove the 46 unconsumed old definitions, including their dead alias chains.
2. Move feature dimensions beside their consumers. Put single-use layout values
   back into the owning AXAML/style; OptionConfirm needs no token class.
3. Replace the catch-all file with Foundation, Semantic and Components files.
   Card, Input, Slider, TabItem and Dialog have separate existing contracts.
4. Decouple Home, Settings, DependencyManager, BatchEdit and editor-local values.
   Preserve valid one-off literals in Settings and other page bodies.
5. Compare every changed AXAML attribute after resolving both generations of
   tokens. Keep selectors, template structure, color keys and style includes.
6. Merge import width behavior with the generic Wide preset. All popup widths
   now recalculate through the base class on host resize; import retains only
   its independent height cap. The old import width minimum of zero is replaced
   by Wide's existing 320 minimum. This is the explicitly approved behavior change.

## Verification

Use source/value comparison and builds, not unit tests. Set
`$env:AVALONIA_TELEMETRY_OPTOUT='1'` before `dotnet build`.
Device visual acceptance remains separate: review light/dark themes, interaction
states, localized labels, dialog footers and popup host resize/re-attachment.
