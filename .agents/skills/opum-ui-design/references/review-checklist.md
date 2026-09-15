# Review only the affected responsibilities

## Every UI diff

- Name the canonical source and actual changed owner. Did a local issue accidentally
  alter all Buttons, a shared component, or the palette? If adding a token/component,
  identify consumers that must change together; otherwise retain the local value/style.
- Trace new class/theme/resource usage through actual includes. No global-to-feature
  resource dependency, Fluent dependency, or page-level replacement of shared states.
- Confirm parent-owned gap/inset has not become doubled child Margin + Padding.
  Check scroll-to-bottom after inset changes, not only the initial viewport.
- Preserve commands, namescope, typed bindings and visibility when adding wrappers.
  In editor layouts also inspect coordinate transforms, hit regions and overlay input routing.

## Shared theme / interaction change

- Review affected helper themes and derived usages, not only the demonstrated Button.
  For buttons check normal, hover, pressed, keyboard focus and disabled, including
  checked choices and semantic action roles. Verify disabled fade is applied once;
  role background and content paint survive state changes.
- Check cursor on the actual content/hit target as well as the control: enabled action,
  disabled action, editable/read-only input and specialized scroll/resize interactions
  are not all Hand. Keep state ownership in the base theme.
- For input changes include error/read-only/placeholder/selection behavior; for tabs
  selected+interaction combinations and placement; for sliders orientation, thumb,
  track and disabled behavior. Preserve framework template parts and bindings.
- Verify Light/Dark and live seed changes. If touching resource plumbing, also inspect
  Default/startup/designer resolution and mixed ThemeVariantScopes. Sem.Color is a brush;
  Sem.Value is Color. A successful build does not demonstrate correct live paint.

## Modal change

- Root is PopupDialogControl + PopupDialogRoot; size/profile belongs to root, not body
  ScrollViewer or DialogShell. Width and height choices are independent. Resize down
  **and back up**, including a short viewport; no duplicate viewport arithmetic.
- Body stays finitely constrained; header/footer remain reachable outside its scrolling.
  Background translucency does not dim content; host Padding binding retains viewport inset.
- Check explicit row grouping, long wrapped labels, hidden actions, all-hidden rows,
  disabled/busy actions and absent footer inset. No responsive regrouping or action margins.
- For dynamic options verify parent commands/parameters and result/cancel paths.
  For picker modes inspect each affected mode's footer, not just the default one.

## Text / localization change

- Reuse existing resource keys before adding strings. AXAML examples use DynamicResource;
  `OpenUtauMobile/Helpers/LocalizationManager.cs` loads
  `OpenUtauMobile/Assets/Lang/Strings.*.resx`. Check the currently supported dictionaries
  when adding keys; do not rely on an unrelated locale file to fill missing entries.
- Inspect bound ViewModel text at its producer too. Verify live language change where
  supported, long titles/actions, wrapping vs truncation and narrow widths; do not shrink
  shared typography merely to fit one untranslated or unexpectedly long label.

## Evidence and stopping point

For headless probes, use [UI probes](ui-probes.md). Verify data-path isolation before
constructing ViewModels. Reopen screenshots and confirm the complete target is visible.
Report compilation, offscreen rendering, binding/persistence and actual input checks
separately; assigning a Value property is not evidence of pointer or keyboard routing.

Follow current AGENTS.md for Git/build/test rules; this checklist introduces no test suite.
Use source review and `git diff --check`; if code/theme changed, build the affected target
with repository-prescribed telemetry setting. Exercise only relevant visual/state cases in
the app when available. Report exactly which checks ran and which visual cases remain
unobserved. Do not expand a feature edit into a global literal/token cleanup.
