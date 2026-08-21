# Project Memory

## Version-control workflow

- Use Git directly for reviewing, comparing, restoring, and reverting source changes.
- Do not create separate rollback scripts, backup archives, patch bundles, or verification artifact directories unless the user explicitly requests them.
- For ordinary code changes, report the changed files and verification commands; `git diff` and repository history are the rollback mechanism.

## OPUM preferences

- OpenUtau Mobile-specific persistent options live at the end of `OpenUtau.Core/Util/Preferences.cs`, inside the `OpenUtau Mobile 特定选项` region of `SerializablePreferences`.
- New OPUM settings may be added there and persisted through `Preferences.Save()`.
