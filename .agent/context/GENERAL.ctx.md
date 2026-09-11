# OpenUtau Mobile - General Project Context

## Project Summary

OpenUtau Mobile is a cross-platform mobile singing voice synthesis editor based on OpenUtau.

## Key Constraints

- Avoid modifying `OpenUtau.Core` unless necessary.
- Do not modify `OpenUtau.Plugin.Builtin` (upstream copy).
- Front-end controls should subscribe to `DocManager` directly when possible to reduce coupling.
- Keep UI aligned with Material Design 3 (MD3) and modern, touch-first interaction. Plan for keyboard/mouse and stylus compatibility.
- Name behavior-significant constants and use typed enums. UI literals are allowed for one-off local layout, zero values and independent geometry; do not create tokens solely to eliminate numeric literals.
- Dynamic colors remain in the existing semantic color resources. Static UI tokens live in `Themes/OpenUtauMobile/Tokens/{Foundation,Semantic,Components}` or near their feature owner in `Controls/Tokens` / `Views/Tokens`. Follow `Themes/OpenUtauMobile/Tokens/README.md`; do not recreate a global catch-all token file.
- Use MVVM. UI is Avalonia 12.1 on .NET 10.
- Use ReactiveUI and the Fody helper (deprecated); plan to migrate to a source-generator alternative.
- Language rules follow .NET 10 preview.

## Performance Pain Points

- Memory usage is high: entering edit mode is ~1.2 GB or more; worst cases exceed 3 GB and may crash.
- UI framerate is low and unstable on mobile; editor (piano roll, etc.) is often < 50 FPS.
- The app is locked to 60 FPS even on 120 Hz devices.

## UI/UX Status

- Avalonia 12.1.0 scrolling pitfall: do not set nonzero `ScrollViewer.Padding`.
  Put scrollable content inside a `Border` and set that Border's `Padding` so
  insets participate in the scroll extent. Fixed viewport insets instead belong
  to a Border outside the ScrollViewer. Do not replace child Margin with
  ScrollViewer.Padding during spacing cleanup. A layout diagnostic reproduced
  unreachable bottom content with presenter padding; see UI_METRICS_REVIEW.md.

- All modal dialogs use `DialogShell`; new footers use `DialogActionRows` and
  `DialogActionRow` for explicit equal-width rows (`DialogActions` is the legacy
  single-row alias). Read `DIALOGS.ctx.md`
  before adding or changing a dialog. `Styles/Components/Dialog.axaml` is the single owner
  of dialog chrome and action design; do not add per-dialog chrome tokens or
  duplicate title/close/footer markup.

- Repeated dialog height policies use one root class: DialogHeightCompact,
  DialogHeightRegular, DialogHeightList or DialogHeightExpanded. Values and width
  preset metrics are owned by Components/DialogTokens.cs; mappings live in
  Styles/Components/Dialog.axaml. Do not repeat root MinHeight/MaxHeight literals
  for a shared use case. The token README begins with the owner map and examples.

- Singer installation UI is rough and needs improvement.
- The product is still in the basic feature stage and not feature-complete.

## Incomplete or Missing Features

- Phoneme display and editing.
- Expression parameter display and editing.
- Rendered waveform display.
- Deleting singers.
- Intent filter: opening project/audio files.
- Help/tutorial system.
- Log export.
- Performance monitor.

## Future Features

- Batch editing (see batchedit in `OpenUtau.Core`).
- Real-time microphone recording.
- Real-time playback and performance (live input).
- Remote rendering and cloud sync (concept; details TBD).
- AI-assisted features (e.g., next-edit suggestions; details TBD).

## Coding and Documentation Rules

- Context management uses en-US.
- Code comments must be Simplified Chinese (Mainland).
- XML doc `summary` should be concise; prefer filling `param` and `returns`.
- Strong typing only; do not use `var`.
- Braces must be on their own line.

## Versioning

- Version format: `2.0.x`.
- Increment the patch (`x`) for each release.
- If upstream is synced, increment the minor and reset patch to `0`.

## CI and Testing

- No CI yet.
- Plan: add CI with unit tests and GitHub Actions auto-release after basic features stabilize.

## Known Issues to Track

- TODOs are scattered across the codebase; resolve incrementally.
- Memory usage and frame rate are primary priorities.

