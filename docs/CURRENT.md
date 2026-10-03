# Current state

As of 2026-10-03. Application baseline inspected at `cc2ef10`; the working tree
now includes repository quality tooling and a formatting baseline.

## Delivered application

Enoch is a publication system with an ASP.NET Core API, filesystem run storage, a .NET client and CLI, and a read-only Vue browser. The production Docker build combines the API and browser into one image.

The HTTP API supports run creation and reading, plans, semantic events, evidence, artifacts, results, finish, wait, and resume. Artifact content has a download endpoint. Publisher authentication uses a bearer token; the documented deployment delegates reader authentication to its proxy.

## Verification and limits

The [repository tooling task log](TASK_LOGS.md#2026-10-03-repository-quality-tooling)
records a clean dependency restore and successful `just check` under .NET 8 and
Node.js 22: formatting, analyzers, Vue types, production builds, 13 .NET tests,
and 3 UI tests. [ADR-0004](adr/0004-repository-tooling.md) establishes mandatory
repository directories and `just` as the tooling interface. The [development
rules](exploitation/development.md) focus on immutability, clarity, readability,
and idiomatic code. The container workflow now depends on the check job;
execution on GitHub has not been observed.

The [review council task log](TASK_LOGS.md#2026-10-03-review-council) records Docker build/test results and targeted probes. The existing application suites pass, while additional probes expose material defects that they do not cover.

The live proxy estate and backup restoration were not verified during this review. The existing deployment note retains its own prior provenance.

## Unresolved review findings

The canonical list is [PROBLEMS](PROBLEMS.md), containing the unresolved review defects and separately identified feature, quality, and operations gaps. Only the orchestrator agent may clear its entries after verifying resolution and recording evidence. Detailed historical observations remain in the task log.

## Planned work

The [plans index](plans/README.md) has no accepted implementation sequence yet. Review findings remain unresolved; creating this vault does not implement their fixes.
