---
name: opum-ui-design
description: Make repository-specific UI implementation and review decisions in OpenUtauMobile (OPUM). Use when creating or changing pages, dialogs, popups, editor UI, layouts, interaction states, ControlThemes, component styles, or visual tokens; also when deciding whether a value or abstraction should be shared or feature-local. Not for unrelated backend work or generic design advice.
---

# OPUM UI design

## Decide before editing

1. Identify the surface: navigation page, modal dialog, transient popup/overlay, or editor interaction. Find the nearest **behavioral** match in [canonical patterns](references/canonical-patterns.md); read only that entry and its source, including the relevant code-behind/ViewModel. Similar appearance alone is insufficient.
2. Trace the existing class, Theme key, resource and style include to its owner. Before adding anything, search the relevant base theme, shared component or feature style for an existing abstraction; do not scan every layer for a local edit.
3. Choose the smallest owner: base control state/template, shared component role, feature behavior/layout, or one local value. For ambiguous ownership or new values, read [architecture](references/architecture.md). State the chosen owner and reused implementation briefly before implementing.
4. Change only that responsibility. Preserve feature-specific composition; consistency does not require identical page layouts. Existing examples are starting points, not templates to copy wholesale.
5. Review the diff using only applicable sections of [review checklist](references/review-checklist.md). Report source/build checks separately from actual visual/interaction checks; follow repository instructions for verification.

## Stable boundaries

- OPUM owns complete local ControlThemes. “Fallback” means local upstream-derived templates, not a Fluent package. Shared control states belong there, not in page-level template/presenter patches or duplicated cursor/opacity rules.
- For standard controls, component styles select roles and geometry rather than replace base states. Custom components such as DialogShell own their own templates. Features own business content and domain interactions; global theme resources must not depend on feature styles/tokens.
- Reuse a value by **meaning and change ownership**, never merely by number. A local style already centralizes its setter; a literal alone is not a reason to create a token or component.
- Modal chrome/actions belong to `DialogShell` and shared Dialog styles; viewport coercion belongs to `PopupDialogControl`. Do not reproduce either in a page.

## Read on demand

- Ownership, colors, tokens, base controls: architecture reference; follow its README links only for the affected layer.
- New surface or reuse search: select one canonical entry, not the entire catalog.
- Completion/review request: applicable checklist sections plus the changed source.

References label **contract**, **current pattern**, **example**, and **feature exception**. Recheck current source when documentation disagrees; dated migration inventories and old `.agent/context` status lists are not future design rules.
