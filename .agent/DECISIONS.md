# Decisions Log

Record meaningful technical decisions here. Use one entry per decision.

## Template

- Date:
- Decision:
- Rationale:
- Alternatives considered:
- Impacted areas:

## Entries

- Date: 2026-05-13
- Decision: Initialize agent context and workflow documents for OpenUtau Mobile.
- Rationale: Provide a consistent, searchable context for AI agents and contributors.
- Alternatives considered: None.
- Impacted areas: `.agent` documentation only.

- Date: 2026-05-13
- Decision: Restructure context files into modular `.agent/context/` subfolder.
- Rationale: Reduce context loading overhead by splitting monolithic PROJECT_CONTEXT.md into focused files. Agents can now load only what they need (e.g., PLATFORMS.ctx.md only when working on platform-specific code). Improves scalability for future additions.
- Alternatives considered: Keep everything in one file; create separate platform files inside .agent root.
- Impacted areas: `.agent/` directory structure; README, AGENT_WORKFLOW.md updated to reflect new paths.

