# OpenUtau Mobile - General Project Context

## Project Summary

OpenUtau Mobile is a cross-platform mobile singing voice synthesis editor based on OpenUtau.

## Key Constraints

- Avoid modifying `OpenUtau.Core` unless necessary.
- Do not modify `OpenUtau.Plugin.Builtin` (upstream copy).
- Front-end controls should subscribe to `DocManager` directly when possible to reduce coupling.
- Keep UI aligned with Material Design 3 (MD3) and modern, touch-first interaction. Plan for keyboard/mouse and stylus compatibility.
- Do not hardcode numeric values or enum values in new code.
- Color tokens are generated dynamically; other tokens live in `ThemeStaticTokens.cs`.
- Use MVVM. UI is Avalonia 11 with a planned upgrade to the latest version.
- Use ReactiveUI and the Fody helper (deprecated); plan to migrate to a source-generator alternative.
- Language rules follow .NET 9 preview.

## Performance Pain Points

- Memory usage is high: entering edit mode is ~1.2 GB or more; worst cases exceed 3 GB and may crash.
- UI framerate is low and unstable on mobile; editor (piano roll, etc.) is often < 50 FPS.
- The app is locked to 60 FPS even on 120 Hz devices.

## UI/UX Status

- Singer installation UI is rough and needs improvement.
- The product is still in the basic feature stage and not feature-complete.

## Incomplete or Missing Features

- Phoneme display and editing.
- Expression parameter display and editing.
- Rendered waveform display.
- Deleting singers.
- Opening external URL links.
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

