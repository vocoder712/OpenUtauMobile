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
- Do not enlarge existing controls while reorganizing tokens. Changes to hit
  targets, visual design or sizing policy require an explicit behavior decision.

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
