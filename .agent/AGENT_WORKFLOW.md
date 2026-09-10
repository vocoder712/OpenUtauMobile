# Agent Workflow

This workflow is for AI agents and human contributors to restore context quickly and work consistently.

## Step-by-Step Flow

1. Read `context/GENERAL.ctx.md` for project constraints and pain points.
2. If platform-specific, also read `context/PLATFORMS.ctx.md`.
3. Review `context/MODULE_INDEX.ctx.json` for module boundaries.
4. Identify the target module and confirm if `OpenUtau.Core` or `OpenUtau.Plugin.Builtin` changes are truly required.
5. Outline a plan, then implement the smallest safe change.
6. Verify with builds and source review; do not create or run unit tests under the current repository instructions.
7. Document decisions in `DECISIONS.md`.
8. Update `context/GENERAL.ctx.md` or `context/PLATFORMS.ctx.md` if new facts are learned.

## Development Rules

- Use en-US for context management.
- Use Simplified Chinese (Mainland) for code comments.
- Avoid `var`; keep strong types.
- Put braces on their own line.
- Prefer direct `DocManager` subscriptions for UI controls when it reduces coupling.
- Avoid editing `OpenUtau.Core` and `OpenUtau.Plugin.Builtin` unless necessary.
- Name behavior-significant constants and use typed enums. UI literals are allowed for one-off local layout, zero values and independent geometry; do not create tokens solely to eliminate numeric literals.
- Dynamic colors remain in the existing semantic color resources. Static UI tokens live in `Themes/OpenUtauMobile/Tokens/{Foundation,Semantic,Components}` or near their feature owner in `Controls/Tokens` / `Views/Tokens`. Follow `Themes/OpenUtauMobile/Tokens/README.md`; do not recreate a global catch-all token file.
- When running `dotnet build` in Codex sandboxes, ensure the working directory/output paths are writable (use `sudo` only if necessary) to avoid permission errors.

## Communication Expectations

- Ask for missing information before guessing.
- Prefer minimal diffs with clear reasoning.
- Call out performance impacts and memory risks.

## Information Needed from Owner

- Target platforms and minimum OS versions.
- Build/run commands and expected toolchains.
- UI/UX guidelines beyond MD3.
- Module ownership and areas to avoid.
- Testing expectations, CI, and release workflow.
