# Agent-Assisted Development Workflow

## For Developers

When you develop with an agent, your job is to **set direction, give instructions, and review work**.

### Before instructing an agent:
1. Read `context/GENERAL.ctx.md` once to understand project constraints, pain points, and features.
2. If the task involves multiple platforms, skim `context/PLATFORMS.ctx.md`.
3. Check `DECISIONS.md` to see what choices have been made.
4. Review `AGENT_WORKFLOW.md` to understand how agents work and what to expect.

### When instructing an agent:
- Be specific: What needs to change? Which files? What are the acceptance criteria?
- Point to relevant context: "See the module at X. Remember constraint Y."
- State non-goals: "Don't touch OpenUtau.Core unless it's absolutely necessary."

### After the agent delivers:
- Review changes against the acceptance criteria.
- Check for constraint violations (e.g., hardcoded values, var usage, missing tests).
- Record significant decisions in `DECISIONS.md` (date, what, why, impact).

---

## For LLM Agents

Your job is to **execute tasks efficiently and safely within constraints**.

### At the start of a task (restore context):
1. Read `context/GENERAL.ctx.md` for constraints, pain points, and general project info.
2. If working on platform-specific code, also read `context/PLATFORMS.ctx.md`.
3. Review `context/MODULE_INDEX.ctx.json` to understand module boundaries.
4. Check `DECISIONS.md` to see what's already been decided.

### When executing a task:
- Follow `AGENT_WORKFLOW.md` step by step.
- Confirm that target changes don't violate constraints (e.g., no `OpenUtau.Core` edits unless required).
- Outline your plan before implementing.
- Document decisions in `DECISIONS.md` (date, decision, rationale, alternatives, impact).

---

## Reference Files

**In `.agent/` (Core Workflow):**
- **`README.md`**: This file.
- **`AGENT_WORKFLOW.md`**: Step-by-step workflow, development rules, communication expectations.
- **`DECISIONS.md`**: Decision log. Append entries when making meaningful technical choices.

**In `.agent/context/` (Agent Context — Load as Needed):**
- **`INDEX.md`**: Quick navigation guide. Start here if you're unsure which file to read.
- **`GENERAL.ctx.md`**: Core project info, constraints, pain points, UI/UX status, features, coding rules, versioning, CI setup.
- **`PLATFORMS.ctx.md`**: Platform-specific status, build/run commands, target OS versions. Read when working on platform-specific code.
- **`MODULE_INDEX.ctx.json`**: Module boundaries and ownership.
- **`TODO.ctx.md`**: Ongoing task checklist.
