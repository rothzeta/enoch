# Problems

This is the canonical list of unresolved Enoch issues. Initial findings come from the [2026-10-03 review council](TASK_LOGS.md#2026-10-03-review-council) at application revision `cc2ef10`.

## Ownership

**Only the orchestrator agent may clear entries from this file.** Clearing includes marking resolved, closing, removing, or emptying the list. Implementers and reviewers may add findings and supporting evidence, but must leave resolution to the orchestrator.

Before clearing an entry, the orchestrator verifies its resolution and records the issue ID, implementation reference, verification results, and any remaining limitations in [TASK_LOGS](TASK_LOGS.md). Historical review evidence stays in the task log. Issue IDs remain stable and are not reused.

All entries below are open. P1 identifies urgent correctness or integrity defects; P2 identifies other functional or operational defects; P3 identifies lower-priority defects. Feature and quality gaps are listed separately from established bugs.

## Review defects

| ID        | Priority | Issue                                                                    | Source and acceptance for resolution                                                                                                                                     |
| --------- | -------- | ------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| ENOCH-001 | P1       | Accepted null request, plan, or result makes a run unreadable            | `FileSystemRunStore.cs:32,40,44,49`. Reject unsupported null payloads before persistence or consistently support them; verify read-after-write and terminal readability. |
| ENOCH-002 | P1       | Interrupted publication can duplicate event sequences or overwrite plans | `FileSystemRunStore.cs:44–46`. Establish recoverable commits and verify interrupted writes and retries across restart. The review modeled reachable disk states.         |
| ENOCH-003 | P1       | Missing finish outcome silently records success                          | `Models.cs:10`; `FileSystemRunStore.cs:50`. Require an explicit valid outcome; omission must leave the run active.                                                       |
| ENOCH-004 | P2       | Incomplete creation breaks the complete run index                        | `FileSystemRunStore.cs:40,42`. Stage initial bundles and define incomplete-bundle recovery; unrelated runs must remain listable.                                         |
| ENOCH-005 | P2       | Finish can return failure while leaving an irreversible terminal state   | `FileSystemRunStore.cs:50`. Define the commit point and checksum recovery; verify checksum write failure and retry behavior.                                             |
| ENOCH-006 | P2       | Evidence content has no read endpoint or viewer access                   | `Enoch.Api/Program.cs:34`; `Models.cs:16–17`. Readers must retrieve the published evidence body through the application.                                                 |
| ENOCH-007 | P2       | Browser hides terminal outcomes and summaries                            | `App.vue:51–52`. Display each terminal outcome and its summary accurately, including runs without a result.                                                              |
| ENOCH-008 | P2       | Browser Back/Forward retains the previous run ID                         | `App.vue:9,43`. Reconcile state with the current location and verify index/detail history transitions.                                                                   |
| ENOCH-009 | P2       | Obsolete detail responses can undo navigation                            | `App.vue:31–40`. Cancel or ignore superseded responses; a delayed request must not restore an obsolete record.                                                           |
| ENOCH-010 | P2       | Unknown-run mutations return 500                                         | `FileSystemRunStore.cs:43`. Missing runs must return the documented not-found response without mutation.                                                                 |
| ENOCH-011 | P2       | Plan event IDs have no effect on retry identity                          | `FileSystemRunStore.cs:44`. Implement the intended identity semantics or remove the unused field and document retry behavior.                                            |
| ENOCH-012 | P2       | CI publishes without application tests or a client/CLI build             | `.github/workflows/container.yml:47`; `Dockerfile:13`. Gate publication on the full solution build/tests and UI tests.                                                   |
| ENOCH-013 | P2       | CLI transport failures are unhandled                                     | `Enoch.Cli/Program.cs:43`. Report transport, timeout, and invalid-response failures with an actionable message and controlled exit code.                                 |
| ENOCH-014 | P2       | Advertised `result --outcome` has no effect                              | `EnochClient.cs:71–72`; `Enoch.Cli/Program.cs:34,60`. Implement its documented effect or remove the option.                                                              |
| ENOCH-015 | P3       | Plan ordering is wrong after sequence 9,999                              | `FileSystemRunStore.cs:27,44`. Read numeric sequence order; verify the 9,999/10,000 boundary.                                                                            |
| ENOCH-016 | P3       | Loading state never activates                                            | `App.vue:8,26,51`. Distinguish pending, empty, and failed initial loads.                                                                                                 |
| ENOCH-017 | P3       | Evidence/artifact writes leave the manifest timestamp unchanged          | `FileSystemRunStore.cs:47–48`. Record an accurate update timestamp for these publications.                                                                               |
| ENOCH-018 | P2       | Run locks are retained indefinitely, including invalid/missing IDs       | `FileSystemRunStore.cs:22–23`. Validate before allocation and bound lock retention without breaking serialization.                                                       |
| ENOCH-019 | P2       | Mutation cost grows with all historical content                          | `FileSystemRunStore.cs:35,45–46`. Avoid rehashing all blobs and rewriting all events for each small publication while preserving integrity and recovery.                 |
| ENOCH-020 | P2       | Store upload limit is checked after the complete stream is copied        | `FileSystemRunStore.cs:48`. Enforce the storage bound during copying and clean rejected uploads. HTTP limits must be consistent with the chosen contract.                |
| ENOCH-021 | P2       | Cancellation is inconsistently propagated                                | API handlers and `FileSystemRunStore.Locked`. Carry request cancellation through waits and I/O where appropriate; keep interrupted persistence recoverable.              |

Source filenames refer to their projects under repository `src/`; full provenance and exact observed results are in TASK_LOGS. Entries 018–021 are source-level resource and operational findings, not measured production incident rates.

## Feature, quality, and operations gaps

These are open improvement candidates; they do not establish accepted implementation scope.

| ID        | Gap                                          | Completion must establish                                                                                                                        |
| --------- | -------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------ |
| ENOCH-022 | Human-readable publication sections          | Readable requests, plan revisions, results, and a semantic progress timeline.                                                                    |
| ENOCH-023 | CLI/client lifecycle coverage                | A supported way to use the existing wait/resume API actions.                                                                                     |
| ENOCH-024 | Complete bundle export and verification      | An export containing evidence bodies and artifact bytes, with checksum verification.                                                             |
| ENOCH-025 | Run discovery at scale                       | Search, state/outcome filters, and pagination supported by the actual API and browser.                                                           |
| ENOCH-026 | Local UI/API development wiring              | A documented working development setup with API routing from Vite.                                                                               |
| ENOCH-027 | Protected-deployment CLI read authentication | A supported credential path under the documented human-auth read boundary. This gap is inferred from configuration; live routing was not tested. |
| ENOCH-028 | Backup and restore procedure                 | An executable procedure with observed restoration evidence.                                                                                      |
| ENOCH-029 | Persistence code reviewability               | Readable methods and clear validation, lifecycle, persistence, and recovery responsibilities; reconcile the unused application facade.           |
| ENOCH-030 | Reader accessibility                         | Accessible section selection, associated panels, and readable timestamps.                                                                        |

See [CURRENT](CURRENT.md) for implementation status and [Plans](plans/README.md) for accepted delivery scope.
