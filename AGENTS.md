# Repository invariants

## Git safety
- Start each task by inspecting `git status --short`.
- Preserve all pre-existing uncommitted changes.
- If pending changes overlap files required by the task, or their ownership is
  unclear, stop and notify the user before editing.
- Unrelated pending changes do not block the task; do not modify, stash,
  discard, or clean them.
- Do not maintain any rollback files or scripts. Instead, ask the user to make a backup commit before high risk or unclear changes. Use git to manage rollbacks.

## Boundaries and style
- Avoid unnecessary changes to upstream-derived `OpenUtau.Core`; keep application orchestration in the Mobile layer.
- Do not hand-edit upstream-copy `OpenUtau.Plugin.Builtin` for feature work. For upstream synchronization, follow `docs/UPSTREAM_SYNC.md`, including its compatibility-review requirements.
- Follow `.editorconfig`. Write new code comments in Simplified Chinese; preserve upstream comments.

## Verification
- Before `dotnet build`, set `$env:AVALONIA_TELEMETRY_OPTOUT='1';` (or the equivalent environment variable in another shell).
- Do not add new unit tests unless explicitly requested or the task requires them.
- Run existing relevant tests when available and appropriate.
- Do not introduce a new test suite solely for verification.

## Context
- Do not preload repository documentation or historical notes. Read source, docs, and repository-local skills only when relevant to the current task.
- Current source is the primary source of truth; docs are references, not startup context.
- Load repository-local skills when their scope matches the task. Historical migration notes are not active instructions.
