# Repository invariants

## Git safety
- Start each task by inspecting `git status --short`.
- Preserve all pre-existing uncommitted changes.
- If pending changes overlap files required by the task, or their ownership is
  unclear, stop and notify the user before editing.
- Unrelated pending changes do not block the task; do not modify, stash,
  discard, or clean them.
- Do not maintain any rollback files or scripts. Instead, ask the user to make a backup commit before high risk or unclear changes. Use git to manage rollbacks.
- Place plans documents in `/plans` where git can ignore them. Do not place plans in the source tree or in the `docs` directory.

## Boundaries and style
- Keep singer plugin infrastructure generic. Do not name or describe specific external plugins or add their dedicated behavior in host code, documentation, or UI. Keep README and UI text concise and actionable.
- Forbid modifications to `OpenUtau.Core/Ustx`, which could break compatibility with universal USTX files.
- Avoid unnecessary changes to upstream-derived `OpenUtau.Core`; keep application orchestration in the Mobile layer. Always ask for permission before modifying upstream-derived code.
- `OpenUtau.Core/Util/Preferences.cs#OpenUtau Mobile特定选项` can be modified. 
- Do not hand-edit upstream-copy `OpenUtau.Plugin.Builtin` for feature work. For upstream synchronization, follow `docs/UPSTREAM_SYNC.md`, including its compatibility-review requirements.
- Follow `.editorconfig`. Write new code comments in Simplified Chinese; preserve upstream comments.
- Newly added classes, public methods, hot private methods, and hot properties should use XML comments in Simplified Chinese whenever possible.
- When newly added controls involve icons, you must use existing icons in the icon library and are prohibited from drawing icons yourself in forms such as PathGeometry. If necessary, stop and confirm.
- Add comments to critical containers to explain their purpose when adding new controls in `.axaml` files.
- Always use collection expressions instead of such as `ToList()` or `ToArray()` when constructing collections.

## Verification
- Do not introduce defensive validation layers in the build graph or CI.
- Do not add non-temporary verification scripts unless explicitly requested.
  Keep temporary investigation scripts outside the source tree and do not hook
  them into normal builds or CI.
- Before `dotnet build`, set `$env:AVALONIA_TELEMETRY_OPTOUT='1';` (or the equivalent environment variable in another shell).
- Do not add new unit tests unless explicitly requested or the task requires them.
- Run existing relevant tests when available and appropriate.
- Do not introduce a new test suite solely for verification.

## Context
- Do not preload repository documentation or historical notes. Read source, docs, and repository-local skills only when relevant to the current task.
- Current source is the primary source of truth; docs are references, not startup context.
- Load repository-local skills when their scope matches the task. Historical migration notes are not active instructions.

## Code editing rules

- Prefer apply_patch for source code modifications.
- Do not use Python str.replace(), PowerShell -replace, or whole-file rewriting for ordinary C# edits.
- If apply_patch fails, report the exact error. Do not silently switch to string-based editing.
- Search with rg and read only relevant code sections.
- If an approach fails twice, reassess the root cause before retrying.
