# Working in Enoch

Read [the development rules](docs/exploitation/development.md) and relevant
[ADRs](docs/adr/README.md) before changing source. Use `just` for repository
management and aggregated checks; run `just` to list commands.

Prefer immutable values, explicit state transitions, descriptive names, and
idiomatic C# and Vue. Keep mutation within the owner of state. Format with
`just format` and verify with `just check` before handing back source changes.

`.agents/` holds repository agent resources; `bin/` holds executable entry points;
`scripts/` holds reusable automation. Their contract is
[ADR-0004](docs/adr/0004-repository-tooling.md).

Record actual verification in `docs/TASK_LOGS.md` and update `docs/CURRENT.md` when
implementation facts change. Only the orchestrator may clear entries in
`docs/PROBLEMS.md`, after verification and recorded evidence.
