# Context Structure Guide

## Quick Navigation

### I need to... → Read this:

| Task                                               | File to Read                                          |
|----------------------------------------------------|-------------------------------------------------------|
| Understand project constraints and rules           | `context/GENERAL.ctx.md`                              |
| Work on Android/iOS/Windows/platform-specific code | `context/PLATFORMS.ctx.md` + `context/GENERAL.ctx.md` |
| Find module boundaries and ownership               | `context/MODULE_INDEX.ctx.json`                       |
| Check what's left to do                            | `context/TODO.ctx.md`                                 |
| Understand agent workflow                          | `AGENT_WORKFLOW.md`                                   |
| See what decisions were made                       | `DECISIONS.md`                                        |
| Get started as a developer or agent                | `README.md`                                           |

## File Organization Philosophy

**Why split?** Smaller, focused context files load faster and reduce noise for agent tasks that don't need platform-specific info.

**Example Workflows:**

### Scenario 1: Add a UI control to the main screen
- Read: `context/GENERAL.ctx.md` (constraints, MVVM rules)
- Skip: `context/PLATFORMS.ctx.md` (not needed)

### Scenario 2: Fix Android audio integration
- Read: `context/GENERAL.ctx.md` (constraints, rules)
- Read: `context/PLATFORMS.ctx.md` (Android build/run, Audio path)
- Reference: `context/MODULE_INDEX.ctx.json` (Android module structure)

### Scenario 3: Implement a new feature across all platforms
- Read: `context/GENERAL.ctx.md` (constraints, rules)
- Reference: `context/PLATFORMS.ctx.md` (verify all platforms support it)
- Reference: `context/MODULE_INDEX.ctx.json` (module structure)
- Reference: `context/TODO.ctx.md` (check if it's already listed)

## When to Append / Update Context Files

- **New facts or blockers discovered?** → Update `context/GENERAL.ctx.md` or `context/PLATFORMS.ctx.md`.
- **Significant technical choice made?** → Append to `DECISIONS.md`.
- **Feature completed or reprioritized?** → Update `context/TODO.ctx.md`.
- **New module or ownership change?** → Update `context/MODULE_INDEX.ctx.json`.

