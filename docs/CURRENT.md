# Current state

As of 2026-10-03. The latest quality audit inspected `6db50c0`, including
repository quality tooling and a formatting baseline.

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
hosted execution of the exact baseline `6db50c098d565d6ae3fae8f2f44b492b308ecef4`
was subsequently observed successful. Before release publication, stabilization
was verified locally; hosted execution of that candidate had not been observed.

The [review council task log](TASK_LOGS.md#2026-10-03-review-council) records Docker build/test results and targeted probes. The existing application suites pass, while additional probes expose material defects that they do not cover.

The [quality audit](TASK_LOGS.md#2026-10-03-quality-audit) repeated the full Docker
restore and check successfully: 13 .NET tests and 3 UI tests passed. Eleven
isolated storage probes and four browser probes reconfirmed existing defects,
including unreadable null publications, duplicate sequences after an interrupted
commit, implicit successful finish, and stale browser navigation. The issue
register now also records inadequate behavioral regression coverage as ENOCH-031.
Application source was not changed during that audit.

The first stabilization slice now rejects null or omitted request, plan, and
result payloads before persistence; finish requires an explicit supported
outcome. Missing-run mutations return `not_found`/404. Invalid HTTP body binding
retains its HTTP rejection status instead of returning 500. Valid JSON scalar
payloads and all five terminal outcomes remain supported. Previously corrupted
bundles are not repaired by these validation changes.

Creation now prepares a complete bundle in reserved `runs/.staging/` before an
atomic directory rename publishes it. Ownership is enforced by an exclusive
lease in the mounted `runs/` directory. Startup removes recognized abandoned
creation stages; discovery excludes incomplete initial bundles and reports their
run IDs/missing files through host logging. Direct reads retain corruption errors.
Evidence bodies have a metadata-backed download endpoint and browser link.

The coordinated stabilization gate passed 57 backend, 7 client/CLI and 17 browser
tests. A real local production-image drill verified the `/data/runs` mounted
layout, competing-owner rejection, exact publication read-back and quiesced
backup/restore of 14 canonical files with checksums. This first S1/S2/S5/006 image was subsequently upgraded to the final verified
candidate at the same local address, as recorded below.

The live proxy estate remains unverified. Local quiescent backup restoration
was actually verified, including the corrected final recovery/resource image
described below. The deployment note retains its separate provenance.

Plan/event publication now stages a replayable integrity-protected decision before
canonical writes. Recovery precedes reads/mutations under the shared run lock;
plan retry identities retain their original sequence and response. The focused
recovery suite passed 16 cases including damaged intents, real write boundaries,
numeric order and constrained temporary cleanup. Remaining mutations now use the
same journal; terminal outcome/summary retries preserve timestamps and reject
conflicts. Their focused gate passed 10 cases. The final coordinated source gate passed
125 backend, 21 client/CLI, 17 browser and 11 operations tests with zero warnings
or errors. Resource/history/cancellation and exact event-snapshot regressions
are included; independent final source review approved the integrated changes.

Client/CLI focused verification passed 21 tests and the actual Docker consumer
smoke passed all publication families. Result outcome flags/parameters are
intentionally rejected/removed because result publication does not finish a run.
A validated configurable deadline covers headers and body; invalid responses,
transport failures and timeout errors are controlled. These client changes are included in the successful coordinated source gate.

Publication locks use 64 fixed stripes. Metadata/identity cache retention is
limited to one run per stripe; payloads and returned reader lists are not cached.
New events use additive numeric segments while legacy logs remain readable.
Cold mutation bootstrap verifies canonical hashes; warm publications update only
changed hashes, with checksum inventory serialization still growing with history.
Inconsistent legacy sequence histories without an authentic journal return
explicit corruption and preserve files for operator repair. Raw artifacts stream
with a 64 MiB bound and at most one excess byte consumed before rejection.
Cancellation reaches lock waits, read/preparation I/O and creation commit checks;
committed decisions retain recoverability after cancellation.

The complete corrected production-image Docker drill passed actual SIGKILL and
64-identity restart/retry, Kestrel chunked exact 64 MiB acceptance and one-byte
excess rejection, competing-owner rejection, and quiescent 121-file restoration.
Restored empty runs, terminal data, exact large-body bytes, static browser content
and expected authorization responses passed. The retained empty-directory defect
and correction remain recorded. The final image was verified at `http://127.0.0.1:32768`, container
`enoch-local-be2135e5-f0dd-4697-99a9-ea39d893db46`. The original data volume,
publisher credentials and address were preserved. A quiesced 25-file backup and
exact comparison of all three existing bundles/four blob bodies protected the
upgrade. Actual fresh CLI publication families and explicit finish passed;
rejected result outcome flags left data unchanged. Browser HTML and API reads
returned 200. The user subsequently requested that the local app be stopped;
Docker inspection now reports the final container as `exited`. Its data volume,
credentials and the previously stopped container remain preserved.
See the task log for image identity, detailed evidence and stop command.

## Unresolved review findings

All 21 application review defects ENOCH-001 through ENOCH-021 are resolved,
together with the accepted backup/restore and regression gaps ENOCH-028/031.
The canonical [PROBLEMS](PROBLEMS.md) register preserves the remaining feature
and deployment gaps. Only the orchestrator clears entries after recorded evidence
and independent review; detailed historical observations remain in the task log.

## Planned work

The [plans index](plans/README.md) records the accepted stabilization plan as
implemented. Remaining feature/deployment gaps require separate accepted work.
