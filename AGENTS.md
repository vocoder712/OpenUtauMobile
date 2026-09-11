# Repository invariants

## Git safety
- Start each task with `git status --short`. If there are pending changes, stop and notify the user before editing unless they explicitly permit continuing.
- Do not overwrite, stash, discard, or clean uncommitted changes without explicit instruction.
- Use Git for diffs and restoration; do not create manual backups, rollback copies, directories, or scripts.

## Boundaries and style
- Avoid unnecessary changes to upstream-derived `OpenUtau.Core`; keep application orchestration in the Mobile layer.
- Do not hand-edit upstream-copy `OpenUtau.Plugin.Builtin` for feature work. For upstream synchronization, follow `docs/UPSTREAM_SYNC.md`, including its compatibility-review requirements.
- Follow `.editorconfig`. Write new code comments in Simplified Chinese; preserve upstream comments.

## Verification
- Before `dotnet build`, set `$env:AVALONIA_TELEMETRY_OPTOUT='1';` (or the equivalent environment variable in another shell).
- Do not create or run unit tests after implementing new features.

## Context
- Do not preload repository documentation or historical notes. Read source, docs, and repository-local skills only when relevant to the current task.
- Current source is the primary source of truth; docs are references, not startup context.
- Load repository-local skills when their scope matches the task. Historical migration notes are not active instructions.
