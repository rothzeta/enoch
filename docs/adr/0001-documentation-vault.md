# ADR-0001 — Documentation is an Obsidian vault

Status: accepted by user instruction, 2026-10-03.

## Decision

`docs/` is Enoch's Obsidian vault root. Its structure is:

```text
docs/
├── lexicon/
├── adr/
├── plans/
├── exploitation/
├── README.md
├── SCHEMA.md
├── CURRENT.md
├── PROBLEMS.md
└── TASK_LOGS.md
```

`README.md` is the entry point. `SCHEMA.md` defines document roles and conventions. `CURRENT.md` is the current implementation summary. `PROBLEMS.md` is the canonical unresolved issue list. `TASK_LOGS.md` retains dated observations and execution evidence.

Only the orchestrator agent may clear entries from `PROBLEMS.md`, including marking resolved, closing, removing, or emptying the list. Other agents may add findings and evidence. The orchestrator verifies resolution and records its evidence in `TASK_LOGS.md` before clearing an issue; historical evidence remains available.

The four directories have distinct responsibilities:

- `lexicon/`: canonical meanings and distinctions used in Enoch.
- `adr/`: accepted decisions and explicitly marked proposals.
- `plans/`: delivery order, bounded work, and task specifications.
- `exploitation/`: procedures for deploying, operating, protecting, and recovering the application.

Keep all Enoch documentation conventions in this vault. Use relative Markdown links between its notes so navigation works in Obsidian and in repository browsers. Repository source paths and revisions can be recorded as evidence without making another repository's documents a required part of this vault.

An ADR states an accepted obligation. A plan states intended work. A task log states what was observed or executed. Operator guidance states what can currently be exercised. Keep these authorities separate.

## Rationale

One vault gives readers and implementers a shared entry point, stable navigation, and a clear distinction between decisions, current behavior, planned work, and evidence.

## Consequences

The repository README links into the vault. Existing deployment guidance belongs in `exploitation/`, with referring links updated when moved.

Plans follow [ADR-0002](0002-plan-filenames.md) and [ADR-0003](0003-implementation-plan-writing.md). Current facts link to execution evidence in `TASK_LOGS.md`; accepted plans and passing tests do not establish unperformed implementation or operational verification.
