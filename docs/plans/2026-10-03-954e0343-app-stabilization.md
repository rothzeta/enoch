# App stabilization delivery sequence

## Status and authority

Implemented and verified locally on 2026-10-03. The user authorized implementation
with “go”; all accepted S1–S7 slices in this delivery sequence are complete for
the local Docker target. The implementation choices below are settled and their
verification is linked here. Deferred feature and protected-estate work remains
outside this completed scope.

Authority: [ADR-0001](../adr/0001-documentation-vault.md),
[ADR-0002](../adr/0002-plan-filenames.md),
[ADR-0003](../adr/0003-implementation-plan-writing.md),
[ADR-0004](../adr/0004-repository-tooling.md), and the
[development rules](../exploitation/development.md). Starting evidence is
[CURRENT](../CURRENT.md), [PROBLEMS](../PROBLEMS.md), and the
[quality audit](../TASK_LOGS.md#2026-10-03-quality-audit).
Execution evidence belongs in [TASK_LOGS](../TASK_LOGS.md); the orchestrator links
the corresponding entries here after verification.

Integrated checkpoints: S1 validation, S2 staged creation and S5 reader/evidence
are implemented and passed the coordinated repository gate. Evidence:
[S1](../TASK_LOGS.md#2026-10-03-stabilization-s1--publication-validation),
[S2](../TASK_LOGS.md#2026-10-03-stabilization-s2--staged-creation-and-mounted-ownership),
and [S5/006](../TASK_LOGS.md#2026-10-03-stabilization-s5006--reader-state-and-evidence-access).
S3/S4/S6 source and retained regressions are also implemented and independently
reviewed. Evidence: [S3](../TASK_LOGS.md#2026-10-03-stabilization-s3--recoverable-plan-and-event-publication),
[S4/resources](../TASK_LOGS.md#2026-10-03-stabilization-s4--remaining-mutations-and-terminal-recovery),
and [client/CLI](../TASK_LOGS.md#2026-10-03-stabilization-clientcli--controlled-publication-failures).
The final coordinated `just format` and `just check` passed with
125 backend, 21 client/CLI, 17 browser and 11 operational tests, and zero build
warnings/errors. The final Docker process-kill/retry, chunked upload,
restore/auth and preserved-volume upgrade also passed. Evidence:
[complete final operational rerun](../TASK_LOGS.md#complete-final-production-image-operational-rerun)
and [preserved local-instance upgrade/CLI](../TASK_LOGS.md#preserved-local-instance-upgrade-and-actual-cli-verification).
The checked candidate runs at `http://127.0.0.1:32768`, preserving the original
named volume and publisher token. Source changes remain uncommitted; local
verification does not claim hosted publication of this candidate.
Issue closure remains an orchestrator action, separate from checkpoint status.

The first final Docker restoration exposed an optional-directory compatibility
defect. Its [verified correction](../TASK_LOGS.md#verified-empty-bundle-restoration-correction)
preserves file-based restoration without requiring empty plan directories.
The complete final runtime rerun verified that corrected restoration behavior.

## Smallest useful outcomes and starting ownership

Each slice below protects one observable invariant and has its own hand-back.
The combined outcome is readable, recoverable publications, truthful reader
navigation, bounded rejected work, predictable clients, and evidenced release
and restore procedures. Passing the original suites alone is insufficient: the
audit reproduced material defects despite 13 .NET and 3 UI tests passing.

Architect inspected source at `6db50c0`, including the existing uncommitted audit
documentation. Preserve those changes. Primary source homes are:

- `src/Enoch.Storage.FileSystem/FileSystemRunStore.cs`: publication, reads,
  locks, checksum generation and transitions.
- `src/Enoch.Protocol/Models.cs`, `src/Enoch.Application/IRunStore.cs`, and
  `src/Enoch.Api/Program.cs`: protocol, storage operations and HTTP translation.
- `src/Enoch.Ui/src/App.vue`, `api.ts`, `App.test.ts`, and `api.test.ts`:
  browser state, real transport and behavioral regressions.
- `src/Enoch.Client/EnochClient.cs`, `src/Enoch.Cli/Program.cs`,
  `tests/Enoch.Backend.Tests/`, and `tests/Enoch.Client.Tests/`:
  actual consumers and retained regressions.
- `.github/workflows/container.yml`, `justfile`, `scripts/`, and
  `docs/exploitation/`: verification and operational evidence.

The orchestrator owns integration, evidence acceptance and issue closure, and
does not inspect application code. At most three subagents run concurrently.
Architect and scout rotate into implementer roles after contracts are settled;
reviewer independently inspects changes and evidence. One backend implementer
owns shared protocol/API/storage/backend tests at a time. The reader implementer
owns only UI files/tests in parallel. Backend implementer owns CURRENT/TASK_LOGS
until a specific handoff. Architect owns this plan and its index. Only the
orchestrator may clear PROBLEMS after verified, recorded evidence.

## Fixtures, compatibility and settled contracts

Use temporary isolated data roots and the actual store/API, not production data.
Minimum fixtures: one unaffected run, one run under test, two publications and a
restarted store. Add explicit invalid inputs, seeded reachable interruption
states, controlled filesystem faults, deferred browser responses, and streamed
uploads as each slice needs. Numeric ordering uses the 9,999/10,000 boundary;
these fixture values impose no production limit. Retain each established bug's
deterministic failing regression before its production fix.

Keep existing valid HTTP envelopes, string enum values, routes, plan payload
files, event identity semantics and published data readable. Required arbitrary
JSON values remain arbitrary supported non-null JSON values. Reject CLR null
and JSON null/undefined for request, plan and result; events may still have null
data. Making `FinishRequest.Outcome` nullable intentionally adjusts the source
DTO contract so omission/null differs from explicit success; explicit valid
constructors and wire values continue to work. Existing unreadable legacy data
is reported explicitly; do not invent lost payloads.

Keep filesystem storage and the current protocol/application/infrastructure
boundaries. API owns status/envelope translation, client owns transport and Vue
owns reactive state. Do not add a database, duplicate storage path or forwarding
facade. Finished publications remain immutable.

### Recoverable publication strategy

The selected strategy is staged creation plus a replayable per-run publication
journal. The target is process interruption and filesystem write faults on the
same filesystem, with one active FileSystemRunStore owner per data root. Its
locks coordinate all readers and writers; restart fixtures quiesce the previous
owner before opening a new one. Multiple processes or simultaneous independent
store owners
sharing a root and power-loss durability are not established by the existing
architecture or ordinary atomic renames; report such requirements before
claiming support.

1. Reserve `runs/.staging/` for creation staging and `runs/.enoch-store.lock` for
   the exclusive store-owner lease. The documented Compose deployment mounts
   `/data/runs`, so placing either directly under `/data` would cross the mount
   for directory rename and fail to coordinate owners of persisted data. Exclude
   the exact staging directory from run discovery; the lease is metadata, not a
   run or checksum member. Atomic same-filesystem directory moves and the .NET
   exclusive file-share lease support the local Docker target; retain Windows
   compatibility in paths/APIs without claiming unexecuted Windows verification.
   Validate all inputs first; write the complete manifest, request and
   checksums before renaming the completed directory into `runs/{id}`. Pending
   creation directories never appear in the index. Classify legacy directories
   missing required initial files as incomplete, excluding them from discovery
   while preserving explicit corruption on direct read and reporting them for
   operator repair. Emit an API-host operator warning naming excluded run IDs
   and missing initial files, through a storage diagnostic callback consumed by
   the host logger; preserve the existing array list envelope. Direct reads keep
   corruption diagnostics. Remove only recognized abandoned creation staging
   directories before serving requests, when no other owner can still use them.
   Do not hide arbitrary corruption of otherwise complete runs.
2. Under the owning run lock, recover any pending intent before reads or further
   mutations. Index discovery also acquires each run's lock and recovers it before
   reading its manifest; it cannot expose partial publication through listing.
   Prepare immutable replacement payloads, a descriptor with assigned
   sequence/identity and intended manifest, and intended checksums in a private
   preparation directory. Preparation changes no canonical bundle files.
   Validate destinations and compute checksum content before the commit decision.
3. Atomically rename the complete preparation directory to the pending journal.
   This is the commit decision. Recovery rolls forward; it never reallocates
   sequence or silently rolls back a published intent. Apply each replacement
   by copying its retained journal payload to a temporary file and renaming it;
   publish the manifest last. Remove the pending journal only after the whole
   intended bundle, including checksums, is present. Repeating recovery must be
   safe after interruption during any replacement or cleanup. Constrain every
   descriptor path to allowed canonical run-relative destinations and verify
   staged payload hashes before replay. Malformed descriptors or missing/corrupt
   payloads yield explicit corruption/pending failure; never discard that journal
   or allocate a replacement sequence automatically.
4. Exclude preparation directories, journals and temporary files from checksums
   and public readers. Include committed canonical identity metadata. On legacy
   bundles, bootstrap checksum state from actual canonical files. The resource
   slice updates only changed checksum entries rather than repeatedly hashing
   unchanged blobs; stage this optimization after correctness is demonstrated.
5. An unrecovered committed intent blocks that run's reads/new publications with
   a clear retryable `publication_pending` failure; it never exposes a partial
   record or permits a new sequence. Pre-decision failure leaves the old readable
   bundle unchanged. After the decision, cancellation does not undo the commit:
   complete recovery or preserve the intent and report retryable uncertainty.

Plans with an EventId use additive per-sequence identity sidecars whose extension
does not match plan payload `*.json` discovery. A repeated plan identity returns
its original publication response and does not advance sequence, matching the
existing event behavior even if the retry supplies changed data. Identity is
scoped to the publication family. Without EventId, a new plan request publishes
a new revision; do not claim automatic retry deduplication. Sort plan payloads
by numeric sequence. Legacy plans without identity remain readable.

Finish with the same valid outcome and summary is retry-idempotent and returns
the original terminal publication response after recovery. A different finish
request or any other mutation of a finished run remains a conflict. This is an
intentional retry contract improvement, not permission to rewrite terminal data.
Pre-decision checksum faults leave the run active. A fault after the decision may
leave a pending terminal intent; repairing the fault and retrying converges to
the original committed finish, rather than an irreversible failure/409 trap.

## Implementation checkpoints and acceptance

Every checkpoint uses numbered criteria below, independently reviewed retained
regressions, `just format` with diff review, and `just check` before integration.
The hand-back reports changed files, commands/results, acceptance mapping,
intentional contract changes, findings and limitations. ENOCH-031 is exercised
throughout rather than addressed by assertions that mirror implementation.

### S1 — Validate publication boundaries

Owner: backend implementer; prerequisites: none. Files: protocol/store/API and
backend tests. Issues: ENOCH-001/003/010. Observable result: invalid publications
cannot corrupt a run or implicitly mark success.

1. Missing/null request, plan and result payloads are rejected before persistence
   through both direct store calls and the real HTTP API; valid alternatives
   read back, including after restart and finish.
2. Omitted/null/invalid finish outcome is rejected and leaves the run active;
   each explicit supported outcome still succeeds.
3. Unknown-run mutations return the documented 404 without creating files.
   Existing incomplete bundles retain explicit corruption diagnostics.

Replace the current null-plan success expectation with the supported-payload
contract and read-after-write assertions. Verify all affected mutation families,
not only plan publication.

### S2 — Stage complete creation

Owner: backend implementer; prerequisite: S1. Files: store/backend tests.
Issue: ENOCH-004. Observable result: incomplete creation cannot poison discovery.

4. Interruption at each initial persistence boundary and restart never exposes a
   new partial run; unrelated complete runs remain listable.
5. A committed initial bundle contains readable request/manifest and valid
   checksum content; duplicate creation remains a conflict. Leftover staging
   has a documented safe cleanup/recovery policy.
6. Legacy incomplete directories do not break the index, remain explicitly
   corrupt on direct read, and have an operator-visible repair path.

### S3 — Recover incremental publication

Owner: backend implementer; prerequisites: S2 and reviewed journal contract.
Files: store/backend tests and API contract only where necessary. Issues:
ENOCH-002/011/015. Observable result: plan/event publication survives retries and
process interruption without duplicate sequence or overwritten revision.

7. Retained interruption regressions at preparation, commit decision, each
   replacement and cleanup recover through a fresh store, with one coherent
   manifest/event/plan/checksum state and unique increasing sequences.
8. Repeated plan/event identity returns the original response without additional
   publication; stale expected sequence remains a conflict for a new event.
   Concurrent same-process writers cannot assign the same sequence.
9. Legacy bundles still read; plan order at 9,999/10,000 is numeric. New identity
   metadata is excluded from plan values while covered by canonical checksums.

### S4 — Recover terminal and remaining mutations

Owner: backend implementer; prerequisite: S3 acceptance. Files: store/backend
tests and API error translation. Issues: ENOCH-005, remaining ENOCH-002, ENOCH-017.
Observable result: all publication paths obey the same commit/failure contract.

10. Result, evidence, artifact, wait/resume and finish recover at all relevant
    write boundaries, including content/metadata disagreement; failed recovery
    never exposes partial records. Checksum failure is exercised as a real
    filesystem fault, with successful retry after repair.
11. Identical finish retry converges to the original outcome, summary and
    timestamps; conflicting retry and all other terminal mutations leave the
    finished bundle unchanged. Pre-decision failure leaves it active.
12. Evidence/artifact publication records an accurate UpdatedAt and preserves
    body bytes, lengths, MIME types and hashes after restart.

### S5 — Truthful and navigable reader

Owner: reader implementer for UI; backend implementer for evidence body contract.
UI prerequisites: S1 and settled existing API envelope; can run alongside S2–S4
with disjoint files. Evidence integration prerequisite: backend evidence read
operation and documented endpoint. Issues: ENOCH-006/007/008/009/016, related
ENOCH-030. Observable result: the current route displays the actual published run.

13. Every terminal outcome and summary renders accurately, including no-result
    runs. Browser state follows decoded location on Back/Forward and direct
    entry; navigating to the index clears obsolete detail immediately.
14. Superseded or unmounted requests cannot restore stale records/errors/loading
    state; tests deliberately resolve requests out of order. Pending, empty and
    failed initial loads are distinguishable.
15. Real transport tests verify production URL encoding and RunDocument envelope
    adaptation. Published evidence content is retrieved by run/evidence ID and
    accessible in the browser; unknown IDs return 404. Preserve proxy reader
    authentication and serve untrusted bytes without executable HTML injection.

Backend exposes evidence through a storage read contract rather than adding
another direct filesystem path in the UI. Settle endpoint/response shape with
the reader before implementation; artifact download compatibility is retained.
The settled additive contract is `IRunStore.ReadEvidenceAsync` returning
`EvidenceContent` with immutable metadata and a caller-owned stream. The reader
endpoint is `/api/v1/runs/{id}/evidence/{evidenceId}`; the HTTP file result owns
stream disposal and returns an attachment with the published MIME/name and
`X-Content-Type-Options: nosniff`. The browser offers a metadata-backed Download
link; it does not execute published HTML. Lookup is constrained to validated
evidence IDs and occurs after storage recovery under the run lock.
Well-formed unknown IDs return 404; malformed evidence IDs return the existing
400 `invalid_id` validation envelope.

### S6 — Bound work and make clients predictable

Owner: backend/client implementer; prerequisite: S4, with reader changes merged
before shared API contract edits. Files: API/store/client/CLI and corresponding
tests; recurring verification automation belongs in scripts/just. Issues:
ENOCH-013/014/018–021; related ENOCH-029. Observable result: rejected/cancelled
work stays bounded and callers receive actionable failures.

16. Validate IDs before allocating locks; lock retention is bounded without
    splitting serialization for concurrent requests to the same run. A cancelled
    lock waiter does not mutate state or leak ownership.
17. Enforce the established 64 MiB store upload limit while copying, with
    a matching raw HTTP artifact limit and cleanup. Count consumed bytes in a
    controlled stream to prove rejection happens before consuming arbitrary
    excess; restarted reads show no rejected content.
18. Propagate cancellation through API, lock waits and pre-decision I/O. Preserve
    recovery after commit. Verify already-cancelled calls and mid-operation
    cancellation, not just token parameters in signatures.
19. A small publication does not hash unchanged historical blob content or
    rewrite all historical events. Establish observable I/O baselines and retain
    equivalent identity, recovery and integrity behavior; no throughput claim
    without measurements.
20. CLI transport, timeout and invalid-response failures have useful messages
    and controlled exits. Correct the ineffective result outcome option by
    rejecting unsupported use with guidance to explicit finish, updating help,
    and removing the ignored client parameter with a documented source contract
    change. Do not silently turn result publication into finish.

The selected timeout contract retains the existing 100-second default, adds
`EnochClientOptions.RequestTimeout`, and allows a positive finite (fractional)
`ENOCH_TIMEOUT_SECONDS` CLI override. One deadline covers headers and body
consumption, and client cancellation remains cancellation. The client source
migration is `PublishResultAsync(runId, content, ct)` followed by explicit
`FinishRunAsync` when desired; the ignored outcome parameter is removed.

The selected lock implementation uses 64 fixed per-owner semaphore stripes,
validating IDs before selecting a gate and using cancellable waits. This is a
bounded implementation choice, not a limit on run count. Equal run IDs always
serialize; unrelated IDs may share a stripe. Streamed uploads count and hash
bytes during bounded copying, allowing only one extra byte to determine that the
established 64 MiB storage limit was exceeded, including chunked requests without
Content-Length.
The raw artifact endpoint overrides Kestrel's smaller default before reading;
the store remains the authority for its 64 MiB streamed limit. JSON publications
retain Kestrel's request-body cap, so the store's evidence bound does not promise
that an equally large JSON request is accepted over HTTP. Verify exact-limit and
limit-plus-one chunked artifacts against the real local Kestrel image.
Artifact reads follow the same storage-owned stream contract as evidence reads:
`ReadArtifactAsync` returns `ArtifactContent`, with ID lookup and pending recovery
under the owning stripe, and the HTTP attachment result disposes the stream.

The selected incremental event representation adds numeric
`events/{sequence}.json` files while retaining reads of legacy `events.jsonl`.
The HTTP RunDocument/event envelopes stay unchanged; operational checksum and
backup discovery must include the new canonical directory. The event identity
index owns identity/locator metadata rather than caller-owned Data references;
returned publication values are deserialized persisted snapshots. Checksum
inventory bootstraps actual canonical files before trusting
owned committed state, then hashes only changed payloads. Recovery must
invalidate or reconcile cached identities and checksums before later mutations.
The settled cache retention policy keeps at most one current-run publication
metadata cache per stripe, selected and evicted while its stripe is locked.
Event identity entries retain sequence/file locators, not RunEvent/Data or
serialized payloads; retries read the original stored payload. Index listing
does not populate publication-history caches. Retention is bounded to 64 cached
runs; each selected run's index/inventory still grows with its committed history,
and serializing the canonical checksum inventory remains proportional to file
count. Recovery of an externally pending intent invalidates the matching cache
before replay, including failed replay. Normal same-owner replay retains its
prior cache while the stripe is held, so no other operation can observe it,
then updates the cache after successful publication;
every commit-decided failure invalidates it, including failure after journal
retirement when no pending intent remains. Retain a warm-cache retry regression
at that cleanup boundary. The independently reviewed legacy policy rejects every manifest/history
sequence mismatch without an authentic retained publication intent with explicit
`storage_corrupt`, preserving all files for operator repair. Unique orphan
payloads do not prove the old commit decision; ignored historical plan EventIds
cannot be reconstructed. Do not automatically repair even an active manifest
behind unique observed history, renumber events, adopt orphan publications or
rewrite a finished record. Valid legacy bundles remain compatible; authentic
pending intents use the selected replay protocol. Retained fixtures distinguish
valid legacy bundles from behind/ahead, duplicate-sequence and finished mismatch
states, and prove rejected legacy mutations preserve all files.

### S7 — Evidence release and operational recovery

Owner: orchestrator coordinates operational implementer and reviewer.
Prerequisites: S1–S6 accepted for application readiness; operational scripts may
be prepared earlier. Files: workflow, scripts/just, exploitation documentation
and evidence. Issues: ENOCH-012/028; investigate ENOCH-027. Observable result:
delivery/restore claims have real evidence and explicit remaining limitations.

21. Preserve current `check` job running `just install`/`just check` and container
    build depending on it. The observed baseline hosted execution establishes
    the original ENOCH-012 gate fix; preserve that evidence and verify the final
    local candidate with the same recipes. Do not claim hosted execution of the
    modified uncommitted candidate or implement a duplicate gate for a stale issue.
22. An executable backup/restore procedure restores isolated storage containing
    request, revisions, events, evidence bodies, artifact bytes, result and
    terminal metadata, and verifies checksums/readability. Define writer quiescence
    and pending-journal treatment before copying; never claim a live backup is
    consistent merely because directory copying succeeds.
23. The locally built Docker candidate rejects missing/wrong publisher bearer
    credentials before persistence and accepts valid credentials; anonymous
    local reads retain the application contract. Separately retain the live
    proxy reader-authentication/bypass limitations and ENOCH-027 as deferred
    estate work; local anonymous reader success does not close that gap.

User steering settles the operational delivery target as local Docker. Require
the final locally built candidate to pass the actual isolated restore/auth drill
and launch healthy on a loopback-only port with persistent named run storage.
Give the user its URL and exact stop recipe; retain its publisher token only in
an ignored mode-0600 file. External publication, deployment, PR creation and
hosted execution of the modified uncommitted candidate are not required for this
local target. Observed baseline hosted-CI evidence establishes the existing gate;
keep that distinct from local candidate verification. Live proxy/estate checks
and ENOCH-027 remain outside the local completion claim.

## Verification and hand-back

Supported aggregated commands were confirmed with `just`. Restore pinned tools
with `just install`; use declared .NET 8 and Node.js 22, or reproducible Docker
mode: `ENOCH_TOOLING=docker just install`, `ENOCH_TOOLING=docker just format`,
and `ENOCH_TOOLING=docker just check`. Backend and browser regressions join the
existing suites exercised by `just test`/`just check`; focused commands may use
the repository tool adapter and actual test names once introduced. Never present
a proposed fixture/test command as already run.

Reviewer reports criteria covered, remaining concerns and exact evidence.
Orchestrator verifies that report and records issue IDs, changed implementation
reference, commands/results and limitations in TASK_LOGS before clearing issues.
Update CURRENT only with delivered facts. Mark checkpoints complete here only
after integration; link execution evidence rather than duplicating logs. Hosted
CI, restored backups and live routing need distinct observed evidence.

## Non-goals and stop conditions

Defer run search/pagination, full bundle export, semantic rendering redesign,
new lifecycle CLI features and unrelated product expansion (ENOCH-022–026).
Reviewability and accessibility improvements stay within changed responsibilities;
do not clear ENOCH-029/030 on cosmetic work alone.

Report before proceeding if source shows saved data or demonstrated consumers
contradict these contracts, a journal cannot replay idempotently, another process
shares the writable root, required filesystem operations lack atomic semantics,
or a recovery fault could expose partial state. Record any replacement strategy
and compatibility decision in this plan before dependent work. Missing live
estate access does not block reversible local application fixes; it does prevent
unsupported operational-readiness claims. Preserve user changes and never clear
issues merely because formatting or the original tests pass.
