# Project Memory

## Version-control workflow

- Use Git directly for reviewing, comparing, restoring, and reverting source changes.
- Do not create separate rollback scripts, backup archives, patch bundles, or verification artifact directories unless the user explicitly requests them.
- For ordinary code changes, report the changed files and verification commands; `git diff` and repository history are the rollback mechanism.
