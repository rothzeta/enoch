# ADR-0002 — Plan filenames

Status: accepted by user instruction, 2026-10-03.

## Decision

Every plan document filename must use `yyyy-mm-dd-[rand:8]-{name}.md`:

- `yyyy-mm-dd`: creation date.
- `[rand:8]`: eight randomly generated characters.
- `{name}`: descriptive plan name.

Generate the random ID once and retain the filename on subsequent edits. For example, `2026-10-03-a4b2db08-publication-recovery.md`.

Store plans in `docs/plans/`. A standalone task specification is a bounded plan and uses the same filename rule. Tasks contained within a plan use stable identifiers inside that plan; their executed work is recorded in [TASK_LOGS](../TASK_LOGS.md).

Use `git mv` for tracked plan renames and update references in the same change. Existing plans migrated to this rule use the rule-adoption date. Plan status does not exempt a plan from the naming rule.

## Rationale

The date makes the plan's origin visible; the random ID distinguishes plans with similar names. The descriptive suffix identifies the work. The rule is linked from [SCHEMA](../SCHEMA.md) and the [plans index](../plans/README.md).

## Consequences

Filenames remain stable as implementation proceeds. Status, task completion, and evidence links change inside the document and index rather than through repeated filename changes.
