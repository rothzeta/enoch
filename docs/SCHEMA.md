# Documentation schema

Authority: [ADR-0001](adr/0001-documentation-vault.md), [ADR-0002](adr/0002-plan-filenames.md), and [ADR-0003](adr/0003-implementation-plan-writing.md).
Repository layout and tooling follow [ADR-0004](adr/0004-repository-tooling.md).

## Vault layout

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

Each directory has a `README.md` entry point so it is navigable and tracked even before more notes are added.

| Location        | Responsibility                                                                     |
| --------------- | ---------------------------------------------------------------------------------- |
| `README.md`     | Vault entry point and navigation                                                   |
| `SCHEMA.md`     | Document roles, formats, and links                                                 |
| `CURRENT.md`    | Current implementation facts and links to issues and evidence                      |
| `PROBLEMS.md`   | Canonical unresolved issue list; only the orchestrator agent may clear entries     |
| `TASK_LOGS.md`  | Dated observations, task execution, exact verification results, and limitations    |
| `lexicon/`      | Canonical terms and distinctions                                                   |
| `adr/`          | Accepted decisions; proposals must be explicitly marked proposed                   |
| `plans/`        | Active delivery sequence, bounded plans, and standalone task specifications        |
| `exploitation/` | Procedures for deploying, operating, protecting, and recovering delivered behavior |

## Naming and status

ADRs use `NNNN-descriptive-name.md` and retain their number when amended. New decisions use the next unused number. Record status, date, decision, rationale, and consequences.

Plans and standalone task specifications use `yyyy-mm-dd-[rand:8]-{name}.md`, as defined by ADR-0002. A task within a plan uses a stable task identifier inside that document. Its execution belongs in `TASK_LOGS.md`; do not create a second execution log inside the plan.

Plans identify whether they are proposed, accepted, implemented, or superseded. The plans index identifies the active delivery sequence. A plan's acceptance does not establish implementation.

Task log entries have a dated heading and record scope, inspected revision, changed files or affected behavior, commands and results, findings, limitations, and links to any governing plan. New entries retain the history of earlier observations.

Problems have stable IDs and remain open until the orchestrator agent verifies resolution. Other agents may add findings or evidence. Only the orchestrator may mark resolved, close, remove, or clear entries, after recording resolution evidence in `TASK_LOGS.md`.

## Links and evidence

Use relative Markdown links between vault notes. Keep repository source paths and revisions explicit when recording evidence. Update links in the same change as a rename or move.

`CURRENT.md` summarizes current facts and links to the relevant task log entry. Keep exact commands and historical test results in `TASK_LOGS.md` rather than duplicating them in every index.

Keep accepted decisions, intended work, implemented behavior, executed verification, and live operational evidence distinct. Only claim a review, test, or deployment check when it occurred. An application test pass does not establish interruption recovery or successful backup restoration.
