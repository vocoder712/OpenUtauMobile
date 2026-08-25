# AGENTS.md

Read [`.agent/README.md`](.agent/README.md) for a detailed explanation of the agent-assisted development workflow.

## Git workflow requirements

- Use Git directly for diffs, restoration, and rollback. Do not create or maintain manual backup copies, rollback scripts, rollback directories, or other rollback artifacts.
- At the start of every task, run `git status --short`. If the working tree contains any pending uncommitted changes, stop before making changes and notify the user. Do not alter, stash, discard, or clean those pending changes unless the user explicitly instructs you to do so.
