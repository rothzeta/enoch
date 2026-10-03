# Problems

This is the canonical list of unresolved Enoch issues. Initial findings come from the [2026-10-03 review council](TASK_LOGS.md#2026-10-03-review-council) at application revision `cc2ef10`.

## Ownership

**Only the orchestrator agent may clear entries from this file.** Clearing includes marking resolved, closing, removing, or emptying the list. Implementers and reviewers may add findings and supporting evidence, but must leave resolution to the orchestrator.

Before clearing an entry, the orchestrator verifies its resolution and records the issue ID, implementation reference, verification results, and any remaining limitations in [TASK_LOGS](TASK_LOGS.md). Historical review evidence stays in the task log. Issue IDs remain stable and are not reused.

Entries in the review-defect and improvement-candidate tables below are open. P1 identifies urgent correctness or integrity defects; P2 identifies other functional or operational defects; P3 identifies lower-priority defects. Feature and quality gaps are listed separately from established bugs.

## Review defects

No review defects remain open. Resolution evidence and limits are recorded below.

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
| ENOCH-029 | Persistence code reviewability               | Readable methods and clear validation, lifecycle, persistence, and recovery responsibilities; reconcile the unused application facade.           |
| ENOCH-030 | Reader accessibility                         | Accessible section selection, associated panels, and readable timestamps.                                                                        |

See [CURRENT](CURRENT.md) for implementation status and [Plans](plans/README.md) for accepted delivery scope.

The [2026-10-03 quality audit](TASK_LOGS.md#2026-10-03-quality-audit) reconfirmed
existing storage and browser defects at `6db50c0` despite a successful full check.
ENOCH-031 records the observed coverage gap separately from application defects.
No existing entry was cleared during that audit.

## Resolved findings

- **ENOCH-012**: publication is gated by the full repository check.
  [Hosted verification](TASK_LOGS.md#hosted-ci-evidence-at-the-starting-revision)
  observed successful check and dependent container publication at baseline
  `6db50c098d565d6ae3fae8f2f44b492b308ecef4` in
  [Actions run 37144611803](https://github.com/rothzeta/enoch/actions/runs/37144611803).
  The [active stabilization sequence](plans/2026-10-03-954e0343-app-stabilization.md)
  preserves this gate. This closes the original CI omission; it does not claim
  hosted verification or publication of the modified stabilization candidate.

- **ENOCH-001, ENOCH-003, ENOCH-010**: new unsupported null publications,
  omitted/invalid finish outcomes and unknown-run mutations now reject without
  modifying published files. Valid publications remain readable after restart.
  [S1 evidence](TASK_LOGS.md#2026-10-03-stabilization-s1--publication-validation)
  records retained failing regressions and intentional nullable outcome semantics.
- **ENOCH-006, ENOCH-007, ENOCH-008, ENOCH-009, ENOCH-016**: evidence bodies
  are available through the actual reader download endpoint; terminal outcomes
  and summaries render; history and obsolete responses follow the current route;
  pending, empty and error states differ.
  [Reader/evidence evidence](TASK_LOGS.md#2026-10-03-stabilization-s5006--reader-state-and-evidence-access)
  records retained browser/HTTP regressions and the polling-starvation follow-up.
  The [coordinated gate](TASK_LOGS.md#coordinated-s1s2s5006-gate) passed
  `just format`/`just check`: 57 backend, 7 client/CLI and 17 browser tests.
  These early working-tree fixes did not repair historical null corruption or
  establish incremental interruption recovery, mounted Docker restoration or
  deployed proxy authentication. Later verified stabilization slices are recorded
  below and in the [active plan](plans/2026-10-03-954e0343-app-stabilization.md).

- **ENOCH-004**: creation stages a complete initial bundle inside the mounted
  run tree; incomplete legacy bundles are excluded from the index with operator
  diagnostics while direct reads remain corrupt.
  [S2 evidence](TASK_LOGS.md#2026-10-03-stabilization-s2--staged-creation-and-mounted-ownership)
  records creation-fault/restart/checksum and ownership regressions.
  The [coordinated gate](TASK_LOGS.md#coordinated-s1s2s5006-gate) passed, and
  [actual local Docker evidence](TASK_LOGS.md#actual-local-docker-and-restoration-evidence)
  proves creation and restored reads with only `/data/runs` mounted, plus rejection
  of a second API owner. The [active plan](plans/2026-10-03-954e0343-app-stabilization.md)
  keeps incremental recovery separate. This working-tree fix does not claim
  multi-writer, power-loss or deployed-estate support.

- **ENOCH-002, ENOCH-005, ENOCH-011, ENOCH-015, ENOCH-017**: replayable
  publication commits, immutable finish retries, plan/event identity, numeric
  ordering and accurate blob timestamps are verified by
  [S3](TASK_LOGS.md#2026-10-03-stabilization-s3--recoverable-plan-and-event-publication),
  [S4](TASK_LOGS.md#2026-10-03-stabilization-s4--remaining-mutations-and-terminal-recovery)
  and [actual Docker interruption/restoration](TASK_LOGS.md#complete-final-production-image-operational-rerun).
  Valid legacy layouts remain compatible; inconsistent history without an
  authentic intent fails explicitly without guessed repair.
- **ENOCH-013, ENOCH-014**: controlled transport/body-timeout/invalid-response
  failures and explicit finish replace the ignored result outcome option.
  [Retained real-process regressions](TASK_LOGS.md#2026-10-03-stabilization-clientcli--controlled-publication-failures)
  and [all-family final CLI proof](TASK_LOGS.md#preserved-local-instance-upgrade-and-actual-cli-verification)
  verify rejection before publication and result-only PUT behavior.
- **ENOCH-018, ENOCH-019, ENOCH-020, ENOCH-021**: 64 fixed lock stripes,
  bounded owned metadata caches, incremental events/checksums, streaming upload
  bounds and request/wait/precommit cancellation are verified by
  [resource regressions](TASK_LOGS.md#integrated-recoveryresource-checkpoint)
  and [real Kestrel exact/excess limits](TASK_LOGS.md#complete-final-production-image-operational-rerun).
  Warm publications avoid historical blob rehash/log rewrites; cold bootstrap,
  per-run inventory and checksum-index serialization still grow with history.
  HTTP body-limit override applies only to raw artifacts. TestServer cancellation
  proves route forwarding, not socket timing.
- **ENOCH-028**: [supported backup/restore recipes](exploitation/backup-and-restore.md)
  require a quiesced writer, fresh destination and exact hash/inventory checks.
  [Complete Docker restoration](TASK_LOGS.md#complete-final-production-image-operational-rerun)
  and [the preserved-instance upgrade](TASK_LOGS.md#preserved-local-instance-upgrade-and-actual-cli-verification)
  verify restored bundles/blobs and unchanged existing data, credentials and URL.
- **ENOCH-031**: retained read-after-write/recovery, browser outcome/history/race,
  real adapter and CLI regressions passed the
  [superseding canonical gate](TASK_LOGS.md#verified-empty-bundle-restoration-correction):
  125 backend, 21 client/CLI, 17 browser and 11 operations cases; zero
  failures/skips or build warnings/errors. Actual final-image interruption,
  restoration and consumer proof are recorded above. The
  [implemented stabilization plan](plans/2026-10-03-954e0343-app-stabilization.md)
  scopes these working-tree fixes to one active store owner and Linux Docker
  process interruption. Power-loss, simultaneous multiwriter, deployed proxy
  verification and hosted-candidate publication remain unclaimed.
