# ADR-0004 — Repository layout and tooling

Status: accepted by user instruction, 2026-10-03.

## Decision

Every Enoch checkout must contain the repository-root directories `.agents/`,
`bin/`, and `scripts/`. Keep a tracked README in each so the directories survive
a fresh clone and their responsibilities remain explicit.

| Location   | Responsibility                                                                                           |
| ---------- | -------------------------------------------------------------------------------------------------------- |
| `.agents/` | Repository-specific agent resources and supporting assets; root `AGENTS.md` is the discovery entry point |
| `bin/`     | Small, tracked executable entry points that delegate reusable logic to `scripts/`                        |
| `scripts/` | Reusable repository automation called by executable entry points or `just` recipes                       |
| `justfile` | Discoverable, canonical interface for repository management and tooling aggregation                      |

Use `just` for installation, formatting, linting, type checking, builds, tests,
and the combined check. `just` lists the supported recipes. Add new recurring
repository operations as recipes; move substantial automation into `scripts/`.
Recipes aggregate existing tools and preserve their failures. CI invokes the same
recipes as developers. Package scripts remain the frontend tool adapters.

Root `bin/` is source-controlled. Per-project .NET `bin/` and `obj/`, Node
dependencies, bundles, coverage, and `.cache/` are generated and ignored. Ignore
rules must distinguish root commands from generated output. Agent resources
must never contain tokens, private transcripts, or machine-specific credentials.
Canonical decisions and code rules stay in the vault, linked from agent entry points.

## Quality tooling

Use the SDK's `dotnet format` for C# and Prettier for frontend code and supported
repository text formats. Use built-in .NET analyzers and explicit EditorConfig
rules for C#, ESLint with Vue and typed TypeScript rules for the browser, and
`vue-tsc` for component type checking. Formatting checks do not write files.

Pin the .NET SDK in `global.json`, analyzer level in `Directory.Build.props`, and
frontend tools in `package.json` and its lockfile. Select compatible TypeScript
and lint/type-checker versions together. C# nullable checks and strict TypeScript
remain enabled. Analyzer and ESLint warnings fail checks; exceptions must be
narrow and explain a concrete reason.

The declared runtime families are .NET 8 and Node.js 22. The tool entry point
supports local tools and explicit `ENOCH_TOOLING=docker` mode. Docker runs as the
invoking user and persists ignored dependency caches and build output.

## Rationale

One visible command interface keeps developer and CI verification aligned.
Separate directory responsibilities keep agent support, executable adapters, and
automation easy to find. Standard tools provide enforcement with a small
configuration surface. C# does not require an additional formatter here.

## Consequences

Container publication must depend on successful formatting, lint, type, test,
and build checks. Run formatting explicitly and review its diff. Keep mechanical
formatting separate from changes to application contracts or behavior.

[Development rules](../exploitation/development.md) define immutability,
readability, and idiomatic coding expectations. These are review criteria where
tooling cannot establish them; this decision requires no custom analyzer.
Executed verification belongs in [TASK_LOGS](../TASK_LOGS.md), with delivered
facts summarized in [CURRENT](../CURRENT.md).
