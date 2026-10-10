# Task logs

Record dated work, evidence, and limitations here. Plans and standalone tasks follow [ADR-0002](adr/0002-plan-filenames.md) and [ADR-0003](adr/0003-implementation-plan-writing.md); this note records their actual execution.

## 2026-10-10 Renovate and GitHub controls

The Renovate installation opened onboarding PR
[#1](https://github.com/rothzeta/enoch/pull/1) on `renovate/configure`; it failed
only because the generated `renovate.json` did not satisfy Prettier. Inspected
GitHub settings confirmed `main` was the default branch, neither branch was
protected, and auto-merge, merged-branch deletion, and vulnerability alerts were
disabled. There was no `master` branch.

Configured normal updates for Monday 00:00–06:00 UTC, a three-day upstream release
cooldown with timestamps required, and squash automerge for passing patch/minor
PRs. Major/other updates remain manual PRs. Security fixes use the lowest patched
version and bypass schedule/cooldown while retaining the update-type merge
policy. Renovate manages merges itself so GitHub's native auto-merge cannot skip
its internal cooldown. Enabled digest pinning, a dependency dashboard through
`config:recommended`, and bounded normal PR concurrency. Added README badges and
`just renovate-check` using official validator `44.149.2` in separate Node 24
Docker tooling.

Applied and read back GitHub branch protection: up-to-date PRs; mandatory `check`
and `build` checks bound to GitHub Actions app `15368`; resolved conversations;
administrator enforcement; force pushes and deletion disabled. Required review
count is zero for the solo maintainer/bot. Enabled repository auto-merge, deletion
of merged branches, and vulnerability alerts. Dependabot automatic security PRs
remain disabled to avoid duplicate updates.

Executed `ENOCH_TOOLING=docker just install`, `ENOCH_TOOLING=docker just format`,
and `ENOCH_TOOLING=docker just check`: passed formatting, analyzers, Vue types,
production builds, 125 backend tests, 21 client/CLI tests, 17 UI tests and 11
operations tests. `just renovate-check` passed strict official configuration
validation. GitHub CLI works with network access outside the restricted sandbox.
An npm audit found transitive `source-map-js` 1.2.1 affected by
[GHSA-68fv-2mgg-jv7q](https://github.com/advisories/GHSA-68fv-2mgg-jv7q), fixed in
1.2.2; a separate security patch follows this onboarding change. Hosted checks of
the updated onboarding commit and its merge are recorded after completion below.

## 2026-10-03 Review council

Scope: complete repository evaluation of correctness, code quality, application behavior, features, tests, and deployment configuration at `cc2ef10`.

Three independent review agents covered backend correctness/storage, application/UI/features, and quality/client/CLI/operations. The integration review consolidated their findings. Application source was not edited.

### Executed verification

The final verification used Docker with the declared runtime families:

| Check                         | Command or setup                                                                                                                                                | Result                                                                                                                                         |
| ----------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------- |
| Full .NET solution            | `mcr.microsoft.com/dotnet/sdk:8.0`; isolated source copy; `dotnet restore Enoch.sln`, `dotnet build Enoch.sln --no-restore`, `dotnet test Enoch.sln --no-build` | Build: 0 warnings, 0 errors. Tests: 13 passed, 0 failed, 0 skipped; backend 6, client 7. Runtime 8.0.31                                        |
| UI suite and production build | `node:22-alpine`; isolated source copy; `npm ci`, `npm test`, `npm run build`                                                                                   | 3 tests passed; TypeScript/Vite production build passed                                                                                        |
| Production image              | `docker build -t enoch-review:cc2ef10 .`                                                                                                                        | Passed                                                                                                                                         |
| Production smoke              | Disposable built-image container with a temporary publisher token and temporary data; HTTP GET of `/health`, `/`, `/runs/review-smoke`, and `/api/v1/runs`      | Each returned HTTP 200                                                                                                                         |
| Storage probes                | Original protocol/application/storage source compiled in an isolated .NET 8 Docker harness                                                                      | Reproduced null-payload corruption, missing outcome, missing-run error, ignored plan IDs, failed finish, interrupted states, and plan ordering |
| Browser probes                | Three additional mounted-component tests in temporary scratch files, executed under Node 24                                                                     | Reproduced hidden failed outcomes, stale history navigation, and delayed detail restoring an obsolete record                                   |

An initial .NET 10 host run encountered a .NET 8 TestHost/System.Text.Json compatibility error. The native .NET 8 Docker run passed the affected test; that environment failure was not counted as an application defect.

### Findings

| Priority | Finding                                                                                        | Source at the inspected revision                                                                                                        | Evidence                                                                                                                                                                       |
| -------- | ---------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| P1       | Null request, plan, and result payloads are accepted but later rejected as corrupt stored JSON | `src/Enoch.Storage.FileSystem/FileSystemRunStore.cs`, lines 32, 40, 44, 49; `tests/Enoch.Backend.Tests/PublicationApiTests.cs`, line 30 | All three payload variants made subsequent reads fail with `storage_corrupt`                                                                                                   |
| P1       | Event and manifest commits can disagree after interruption                                     | `FileSystemRunStore.cs`, lines 45–46                                                                                                    | Modeling the state between renames, then retrying/publishing through the original store, produced event sequences `[1,1]`; plan writes have a corresponding overwrite interval |
| P1       | Missing finish outcome records success                                                         | `src/Enoch.Protocol/Models.cs`, line 10; `FileSystemRunStore.cs`, line 50                                                               | Deserializing `{}` and finishing a run persisted `Success`                                                                                                                     |
| P2       | Incomplete creation breaks the entire run index                                                | `FileSystemRunStore.cs`, lines 40, 42                                                                                                   | Modeling an exposed run directory without its manifest made listing fail                                                                                                       |
| P2       | A failed finish can commit the irreversible finished state                                     | `FileSystemRunStore.cs`, line 50                                                                                                        | Replacing the checksum target with a directory caused an I/O failure; the run remained finished and retry returned 409                                                         |
| P2       | Evidence content has no read route                                                             | `src/Enoch.Api/Program.cs`, line 34; `src/Enoch.Protocol/Models.cs`, lines 16–17                                                        | Traced all read endpoints: they return evidence metadata; content remains on disk                                                                                              |
| P2       | Terminal outcome and summary are hidden in the browser                                         | `src/Enoch.Ui/src/App.vue`, lines 51–52                                                                                                 | Mounted failed run displayed the finished state without failure or summary                                                                                                     |
| P2       | History navigation retains the old route ID                                                    | `App.vue`, lines 9, 43                                                                                                                  | Changing the location to `/` and dispatching popstate still fetched/rendered the old run                                                                                       |
| P2       | An obsolete request can restore stale detail after navigation                                  | `App.vue`, lines 31–40                                                                                                                  | Deferred detail response restored the old record after returning to the index                                                                                                  |
| P2       | Mutating an unknown run returns a server error                                                 | `FileSystemRunStore.cs`, line 43                                                                                                        | Missing-run publication threw `DirectoryNotFoundException`                                                                                                                     |
| P2       | Plan event IDs do not affect retry identity                                                    | `FileSystemRunStore.cs`, line 44                                                                                                        | Two requests with the same plan event ID produced two revisions                                                                                                                |
| P2       | CI publishes without application tests or a client/CLI build                                   | `.github/workflows/container.yml`, line 47; `Dockerfile`, line 13                                                                       | Workflow builds the container; Docker publishes the API and builds the UI                                                                                                      |
| P2       | CLI transport failures are unhandled                                                           | `src/Enoch.Cli/Program.cs`, line 43                                                                                                     | A transport failure exited with an unhandled stack trace                                                                                                                       |
| P2       | `result --outcome` has no effect                                                               | `src/Enoch.Client/EnochClient.cs`, lines 71–72; CLI line 34                                                                             | Original client invocation with failed outcome sent only `{"result":"done"}`                                                                                                   |

Interruption probes constructed precise reachable disk states; they were not process-kill experiments. The checksum failure used actual filesystem fault injection. The probes establish recovery defects, not a measured frequency of production failure.

Additional findings: numeric plan filenames sort incorrectly after sequence 9,999; UI loading state never activates; evidence/artifact publication leaves the manifest update timestamp unchanged; run locks are retained for all requested IDs; each mutation rehashes historical content and event publication rewrites the full log; the store upload limit is checked after copying and cancellation propagation is incomplete.

### Assessment and deferred work

The application is a useful publication MVP with a compact deployment, readable storage layout, path validation, fixed-time publisher token comparison, event retry handling, sequence conflicts, and terminal-state guards. Material durability and reader-workflow defects remain.

Storage code combines validation, lifecycle policy, persistence, and recovery in heavily compressed methods. The application service is an unused forwarding facade. Formatting and clearer responsibility boundaries would improve auditability.

Feature gaps include readable publication sections, CLI/client wait and resume, complete bundle export and checksum verification, search/filter/pagination, local UI/API development wiring, and a supported CLI read-authentication path for the documented protected deployment. That authentication limitation follows from documented routing; no live proxy test was performed.

Recommended planning order: payload validation and recoverable commits; evidence and browser correctness; CI gates and CLI coverage. No remediation plan was accepted and no source fix was implemented in this task.

## 2026-10-03 Documentation vault creation

Authority: the user's instruction to create `docs/{lexicon,adr,plans,exploitation}` with `README.md`, `SCHEMA.md`, `CURRENT.md`, and `TASK_LOGS.md`, and establish documentation, naming, and format decisions as ADRs 1, 2, and 3.

Created the vault entry points and three local ADRs. Adapted the plan naming and writing rules to Enoch's terminology, evidence destinations, and Docker verification workflow. Moved deployment guidance to `exploitation/deployment.md` and updated its referring link in the repository README.

Recorded the completed review as historical evidence and summarized unresolved implementation findings in CURRENT. The plans index remains explicit that no implementation sequence has been accepted.

Verification passed: all 12 required notes exist; ADR headings are numbered 0001, 0002, and 0003; all 47 local Markdown links and fragment targets resolve; no source-project name or retired domain references remain; deployment content is preserved apart from its new navigation links; all Markdown files pass whitespace checks; `git diff --check` is clean. Application tests were not rerun for this documentation-only change; earlier application results are recorded above.

## 2026-10-03 Problems register

Added [PROBLEMS](PROBLEMS.md) by user instruction as the canonical issue list. Populated stable issue IDs from the review, separating defects from feature, quality, and operations gaps. All entries remain open.

Updated ADR-0001, SCHEMA, vault navigation, repository navigation, and the plans index. CURRENT now links to the issue register instead of duplicating its list. Only the orchestrator agent may clear entries, after verification and a resolution record in this task log.

Verification passed: 30 unique open issue IDs, 56 local links and fragment targets, the orchestrator-only clearing rule, and Markdown whitespace checks. `git diff --check` is clean. This change affects documentation only.

## 2026-10-03 Repository quality tooling

Authority: user approval of linting, formatting, and code rules, with the SDK's
formatter for C#, immutability and readability as review priorities, mandatory
`.agents/`, `bin/`, and `scripts/`, and `just` for repository management and tooling
aggregation. Recorded in [ADR-0004](adr/0004-repository-tooling.md).

Inspected baseline: `cc2ef10`, with the user's documentation vault edits already
present. Verification ran before committing. Added EditorConfig, central .NET analyzer
settings, `global.json`, Prettier, Vue/TypeScript ESLint, `vue-tsc`, a root
`justfile`, executable adapters, and canonical [development
rules](exploitation/development.md). Added a CI check job and made container
publication depend on it. Retained the earlier documentation work and applied
the formatter baseline to supported repository files.

The UI now pins TypeScript 6.0.3 because `npm view typescript-eslint
peerDependencies --json` reports support for TypeScript `>=4.8.4 <6.1.0`, while
the prior project used 7.0.2. Frontend dependencies are pinned and locked. The SDK
is 8.0.425 with patch roll-forward; the analyzer level is 8.0-recommended.

Fixed findings exposed by the new checks: interface implementation parameter
names, reused JSON serializer options, explicit ordinal checksum-path comparison,
static helpers, ES2021 libraries for existing `replaceAll` usage, type-only
imports, and void callbacks around the browser's error-handled refresh. API
snapshot types now use readonly properties and collections. Expanded compressed
control flow. No publication/storage lifecycle defect is claimed resolved.

Two scoped test-only rule exceptions preserve readable underscore-separated test
names and inline constant fixture arrays. Production diagnostics stay enforced.
Initial stricter checks failed on these findings; the final run below passed.

| Verification                | Command or setup                                                                                                                  | Result                                                                                                                                                                                                                                  |
| --------------------------- | --------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Complete reproducible check | `ENOCH_TOOLING=docker just install format check`                                                                                  | Passed; restored all .NET projects and ran locked `npm ci`, both formatter checks, all solution builds/analyzers, Vue script/template types, Vite production build, ESLint with zero warnings, both test suites, and `git diff --check` |
| .NET build                  | Included in `just check`, .NET SDK 8.0.425                                                                                        | 0 warnings, 0 errors                                                                                                                                                                                                                    |
| .NET suites                 | `dotnet test Enoch.sln --no-build --no-restore` through the recipe                                                                | 13 passed: backend 6, client/CLI 7; 0 failed, 0 skipped                                                                                                                                                                                 |
| Browser suite               | `npm --prefix src/Enoch.Ui test` through the recipe                                                                               | 3 passed in 2 files                                                                                                                                                                                                                     |
| Workflow validation         | `docker run --rm --volume /opt/dev/enoch:/workspace --workdir /workspace rhysd/actionlint:latest .github/workflows/container.yml` | Passed                                                                                                                                                                                                                                  |
| Recipe and shell validation | `just --list`, `just --dry-run check`, invocation from `src/Enoch.Ui`, `bash -n bin/enoch-tool scripts/run-tool.sh`               | Passed                                                                                                                                                                                                                                  |
| Wrapper failures            | Invoke with no arguments, unsupported tool, and unsupported `ENOCH_TOOLING`                                                       | All 3 returned the expected exit code 2                                                                                                                                                                                                 |
| Documentation links         | Checked repository/vault Markdown file targets                                                                                    | Passed, including final evidence updates                                                                                                                                                                                                |
| Generated output            | `git check-ignore` for project `bin/` and `.cache/tooling/nuget`; root command status                                             | Generated paths ignored; root `bin/` and `scripts/` visible for tracking                                                                                                                                                                |

Installed `.agents/README.md` with filesystem escalation using
`install -D -m 644 /tmp/enoch-agents-README.md /opt/dev/enoch/.agents/README.md`.
The sandbox exposes `.agents/` as a read-only projection; the actual host
directory had to be created. Docker verification reads the completed host checkout.
GitHub-hosted execution and live deployment were not performed. Existing problem
entries remain open, including ENOCH-012 pending observed CI execution and the
broader ENOCH-029 persistence reviewability work.

## 2026-10-03 Quality audit

Scope: source, tests, repository tooling, and deployment configuration at
`6db50c0`. The working tree was initially clean. Read the development rules and
ADR-0004, reviewed the API, filesystem store, protocol/application contracts,
client, CLI, Vue reader, existing tests, and CI/container configuration. No
application source or existing test was changed. Added ENOCH-031 as a quality gap;
all earlier problem entries remain open.

### Executed verification

| Check                 | Command or setup                                                                                                                        | Result                                                                                                                                                                                                        |
| --------------------- | --------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Dependency restore    | `ENOCH_TOOLING=docker just install`                                                                                                     | Passed; .NET projects restored and locked npm installation completed. npm reported 209 audited packages and zero vulnerabilities. This is not a comprehensive dependency or security assessment.              |
| Aggregated checks     | `ENOCH_TOOLING=docker just check`                                                                                                       | Passed: C# and Prettier formatting, whole-solution build/analyzers with zero warnings/errors, Vue script/template types, Vite production build, ESLint, 13 .NET tests, 3 UI tests, and whitespace validation. |
| Storage audit harness | .NET 8 console project under ignored `.cache/quality-audit/Audit.csproj`, referencing the actual filesystem store and protocol projects | Eleven probes reproduced the defects listed below; harness exited 0 because each expected defect was observed.                                                                                                |
| Browser audit harness | Node 22, Vitest, Vue plugin, and jsdom; `.cache/quality-audit/Browser.test.ts` mounted the actual `App.vue` with a mocked API           | Four probes confirmed the defects below. These assertions describe defective current behavior; their passing result does not establish correct application behavior.                                          |

The initial sandboxed Docker restore failed because the Docker socket was
inaccessible. Restore, checks, and probes then ran with filesystem escalation.
The first scratch C# harness build failed repository style diagnostics; SDK
formatting corrected the harness and the subsequent run succeeded. The scratch
Vite config emitted an ESM/config-loader compatibility warning; all four probes
completed. These were audit setup issues, not application findings.

### Reconfirmed defects

| Issue     | Observation at audited revision                                                                                                                                               | Current source                                                  |
| --------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------- |
| ENOCH-001 | Null request, plan, and result were each accepted and then caused `storage_corrupt` on read. A null-plan run could finish successfully and remain unreadable.                 | `FileSystemRunStore.cs:79,154,176,310`                          |
| ENOCH-002 | Restoring the pre-event manifest after event persistence modeled an interrupted multi-file commit. A fresh store then assigned the next event the same sequence as the first. | `FileSystemRunStore.cs:218–226`                                 |
| ENOCH-003 | Deserializing `{}` as `FinishRequest` and finishing the run recorded `Success`.                                                                                               | `Models.cs:7–16`; `FileSystemRunStore.cs:319–335`               |
| ENOCH-004 | A run directory with no manifest made the entire list fail with `storage_corrupt`, including unrelated complete runs.                                                         | `FileSystemRunStore.cs:148–155,164`                             |
| ENOCH-005 | Replacing the checksum destination with a directory caused finish to throw after persisting a terminal manifest. Reading the run still returned `Finished`.                   | `FileSystemRunStore.cs:334–335`                                 |
| ENOCH-007 | A finished/failed run with a summary displayed neither its outcome nor summary in any of the six reader sections.                                                             | `App.vue:15–23,116–167`                                         |
| ENOCH-008 | Changing location to `/` and dispatching `popstate` still fetched and displayed the previous run.                                                                             | `App.vue:9,29–35,69`                                            |
| ENOCH-009 | A deferred detail response restored the old detail view after the user returned to the index; location remained `/`.                                                          | `App.vue:31–35,47–52`                                           |
| ENOCH-010 | Publishing a plan to a nonexistent run threw `DirectoryNotFoundException`, outside the API's domain-error translation.                                                        | `FileSystemRunStore.cs:165–168`; `Enoch.Api/Program.cs:25–26`   |
| ENOCH-011 | Publishing two plans with the same event ID created two revisions.                                                                                                            | `FileSystemRunStore.cs:172–184`                                 |
| ENOCH-016 | An unresolved initial list request immediately displayed “No runs have been published yet.”                                                                                   | `App.vue:8,26–29,94–95`                                         |
| ENOCH-017 | Evidence publication left `Manifest.UpdatedAt` equal to its creation value.                                                                                                   | `FileSystemRunStore.cs:229–249`                                 |
| ENOCH-021 | A read with a pre-cancelled token completed normally. API handlers also omit request cancellation, and the shared lock wait receives no cancellation token.                   | `FileSystemRunStore.cs:38–44,163`; `Enoch.Api/Program.cs:41–70` |

The eleven storage probes include three separate ENOCH-001 cases. Interruption
was modeled through a reachable disk state, rather than killing a process. Finish
failure used an actual filesystem conflict. Paths above refer to their projects
under `src/`; the audit did not measure production incident rates or throughput.

### Quality and coverage assessment

ENOCH-031: `tests/Enoch.Backend.Tests/PublicationApiTests.cs:32–55` requires success
for a null plan and then finish, but never reads the bundle. It therefore passes
while exercising the unreadable-run defect. `src/Enoch.Ui/src/api.test.ts:3–4`
tests JavaScript's `encodeURIComponent` directly without calling the API adapter;
removing production URL encoding would not fail that test. Existing suites lack
regressions for the reproduced interruption, terminal-display, and history/race
failures. Tests should assert complete observable contracts and relevant failure
states before being used as evidence of durability or reader correctness.

Source review also reconfirmed existing quality/resource concerns: blocking
`GetAwaiter().GetResult()` in store reads/listing (`FileSystemRunStore.cs:67,164`),
per-ID locks retained without eviction or prior validation (`37–44`), full
historical hashing on publications (`104–113`), upload limits checked after copying
(`278–281`), and silent cleanup catches (`260–262,300–302`). These fall under
existing ENOCH-018–021 and ENOCH-029; no new performance measurements were made.
The unused application facade remains a forwarding layer. The CLI's exception
filter still excludes transport, timeout, and JSON parse errors
(`Enoch.Cli/Program.cs:62`), and result outcome remains unused
(`EnochClient.cs:80–81`), as already recorded in ENOCH-013 and ENOCH-014. These
client/CLI observations were source-reviewed, not newly executed as probes.

Repository tooling is discoverable and enforces formatting, analyzers, types,
builds, and existing tests. CI now has a `check` job and publication depends on
it (`.github/workflows/container.yml:16–47`). Hosted execution remains unobserved,
so ENOCH-012 was not cleared. Reader authentication remains intentionally delegated
to the deployment proxy; no live authentication or deployment test was performed.

Recommended order remains payload/outcome validation and recoverable storage
commits first, then browser lifecycle/navigation fixes and behavioral regressions,
then resource/cancellation and CLI gaps. No remediation scope was accepted.

Audit harnesses remain in ignored `.cache/quality-audit/` for local inspection and
are not part of the regression suite. The storage harness ran in the SDK image
with repository and tooling-cache mounts, `dotnet run --project
.cache/quality-audit/Audit.csproj`. The browser harness ran in `node:22-alpine` with
`node src/Enoch.Ui/node_modules/vitest/vitest.mjs run --config
.cache/quality-audit/vite.config.ts`.

Documentation verification: `ENOCH_TOOLING=docker just format` passed and left
application source unchanged. All 79 local Markdown links resolved; the problem
register contained 31 unique IDs. `git diff --check` passed. The final tracked
diff contains only CURRENT, PROBLEMS, and this task log.

## 2026-10-03 Stabilization S1 — publication validation

Authority: user authorization to implement the accepted
[stabilization sequence](plans/2026-10-03-954e0343-app-stabilization.md).
Starting application revision: `6db50c0`; preserved the existing quality-audit
documentation changes. This slice addresses ENOCH-001, ENOCH-003 and ENOCH-010;
issue closure remains the orchestrator's responsibility.

Required request, plan and result payloads now reject CLR null, JSON null and
undefined before persistence with `invalid_request`, `invalid_plan` and
`invalid_result`. `FinishRequest.Outcome` is nullable so omission/null cannot
become success; all five supported explicit outcomes remain valid. Missing-run
mutations return `not_found`/404, while an existing incomplete directory retains
`storage_corrupt`. HTTP body binding failures retain their HTTP rejection status
and a generic public problem message. Valid scalar JSON values remain supported.
The former successful-null-plan API test now publishes a valid plan and reads the
finished bundle back.

Retained regressions cover unchanged persisted files/checksums and readable
surviving runs, six omitted/null HTTP payloads, nine CLR/JSON/undefined payloads,
all eight missing-run mutation families, invalid finish outcomes, all five valid
outcomes, and existing incomplete-bundle corruption. Independent reviewer
inspected the source/tests and approved the validation contracts; final integrated
verification remains pending while parallel reader regressions are authored.

| Verification                   | Actual command/result                                                                                                                                                                                                                                                                                                                                       |
| ------------------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Dependency restore             | `ENOCH_TOOLING=docker just install` passed; pinned .NET restore and locked npm installation completed. The sandbox Docker socket was inaccessible; reviewed escalation succeeded.                                                                                                                                                                           |
| Regression before fix          | `ENOCH_TOOLING=docker just test` failed as intended: backend 25 failed, 13 passed. Failures reproduced accepted null payloads, implicit/null finish outcomes and missing-run 500 responses. Output: `/tmp/enoch-validation-before.log`.                                                                                                                     |
| First complete gate            | `ENOCH_TOOLING=docker just format` and `ENOCH_TOOLING=docker just check` passed after the initial fix: 38 backend, 7 client/CLI and 3 UI tests, formatting, analyzers, types, production builds, lint and whitespace. Output: `/tmp/enoch-validation-check.log`.                                                                                            |
| Additional boundary regression | `ENOCH_TOOLING=docker ./bin/enoch-tool dotnet test tests/Enoch.Backend.Tests/Enoch.Backend.Tests.csproj --no-restore --filter 'FullyQualifiedName~Missing_or_invalid'` failed as intended: 1 failed, 5 passed; invalid outcome string returned 500. Output: `/tmp/enoch-validation-binding-before.log`.                                                     |
| Final focused .NET gate        | `ENOCH_TOOLING=docker ./bin/enoch-tool dotnet format Enoch.sln --no-restore --severity warn` and `ENOCH_TOOLING=docker ./bin/enoch-tool dotnet test Enoch.sln --no-restore` passed after preserving binding error statuses and adding direct invalid-outcome coverage: 41 backend and 7 client/CLI tests. Output: `/tmp/enoch-validation-final-dotnet.log`. |

Limitations: these changes prevent new invalid publications; they do not repair
historically corrupted null bundles or establish interruption recovery. The final
coordinated `just format`/`just check` remains required after parallel work reaches
a stable checkpoint. No stabilization candidate was committed, hosted or deployed
by this slice.

### Hosted CI evidence at the starting revision

The independent reviewer performed read-only hosted verification using
`gh run list --repo rothzeta/enoch --workflow container.yml --limit 5 --json databaseId,headSha,status,conclusion,url,createdAt`
and
`gh run view 37144611803 --repo rothzeta/enoch --json headSha,conclusion,url,jobs --jq '{headSha,conclusion,url,jobs: [.jobs[] | {name,conclusion,steps: [.steps[] | {name,conclusion}]}]}'`.
The first sandbox network attempt failed; reviewed network escalation succeeded.
[Actions run 37144611803](https://github.com/rothzeta/enoch/actions/runs/37144611803)
matches exact starting HEAD `6db50c098d565d6ae3fae8f2f44b492b308ecef4`: the
check-job restore/full-check steps and dependent container build/publication
steps all succeeded. This supplies ENOCH-012's previously missing observed-hosted
evidence. It does not verify or publish the modified stabilization candidate.

The orchestrator subsequently cleared ENOCH-012 using that exact-revision hosted
evidence. The stabilization candidate remains unhosted and unpublished.

## 2026-10-03 Stabilization S2 — staged creation and mounted ownership

Creation now writes the manifest, request and checksums in a private recognized
creation stage, then publishes the completed directory by rename. An exclusive
disposable store lease serializes ownership of the actual shared run directory.
Discovery locks each run, excludes missing initial manifest/request files with
operator diagnostics, and preserves explicit corruption for direct reads and
malformed complete manifests. Startup cleans recognized abandoned creation stages
while preserving unknown staging content. Cleanup releases the run semaphore even
if a diagnostic callback throws. Deployment documentation describes quiescence,
preservation and repair/quarantine without inventing lost publication content.

The initial architecture placed the stage and lease beside `runs/`. Reading the
documented deployment revealed that `/data/runs` is a separate mounted filesystem:
that placement could neither guarantee atomic rename nor coordinate owners sharing
the mount. Corrected both to reserved `runs/.staging/` and
`runs/.enoch-store.lock`, preserving the deployed run-volume layout. Discovery
explicitly excludes the reserved staging directory. A retained regression uses two
distinct data roots aliasing the same physical run directory to verify ownership
rejection and readable reopening after lease release. An actual separate-mount
Docker drill remains an integration acceptance requirement.

Verification evidence so far:

- Before the production change, the focused backend command with filter
  `FullyQualifiedName~Creation_failure|FullyQualifiedName~Incomplete_initial|FullyQualifiedName~Abandoned_creation|FullyQualifiedName~Concurrent_store|FullyQualifiedName~Corrupt_complete`
  failed as intended: 5 failed, 1 passed. Output: `/tmp/enoch-creation-before.log`.
- Added empty, manifest-only, request-written and checksum-written abandoned-stage
  restart fixtures, a real cyclic-request serialization fault after initial
  manifest preparation, fresh-owner bundle readability and independently computed
  SHA256 checksums. These strengthen creation acceptance beyond a pre-stage fault.
- The first integrated check stopped on CA1848 for the new warning callback. A
  generated `LoggerMessage` method fixed that diagnostic. A subsequent focused
  build exposed CA1861; moving required initial filenames to a static readonly
  array fixed it.
- A coordinated check later stopped at Prettier on an operations script still
  being authored. This was a concurrent tooling checkpoint, not a demonstrated
  application failure; full integration was deferred until all owners were stable.
- The first combined backend run with evidence tests had 57 cases: 55 passed,
  2 failed. Both were fixture expectations: `DirectoryNotFoundException` derives
  from `IOException` but the assertion required an exact type; invalid evidence-ID
  syntax correctly returned 400 while its test expected 404. Corrected assertions
  retain rejection, unchanged run data, and valid-but-missing ID 404 requirements.
- Independent reviewer approved the strengthened S2 source/tests, conditional on
  the final canonical gate and actual mounted Docker verification.

No application candidate has been hosted or deployed by this checkpoint. Pending
acceptance is recorded explicitly rather than treating original passing suites as
proof of recoverable publication.

## 2026-10-03 Stabilization S5/006 — reader state and evidence access

The parallel reader implementer retained an initial failing browser checkpoint:
`ENOCH_TOOLING=docker ./bin/enoch-tool npm --prefix src/Enoch.Ui test` produced
11 failed, 4 passed in 15 tests before source fixes. The first green checkpoint
had 16 passing tests. Independent review then identified background-poll starvation
for requests slower than the interval. A retained focused regression using
`-- --testNamePattern='slow initial'` failed once, with 16 skipped. The fix skips
background refresh while a request is pending. A fixture microtask-flush correction
was needed afterward; the final focused browser suite passed 17 tests.

The browser now displays terminal outcomes/summaries, follows Back/Forward
location state, ignores obsolete responses, and distinguishes loading, empty and
failed initial loads. API adapter tests exercise actual encoded fetch URLs and
problem handling. Published evidence exposes a real body download route backed
by `IRunStore.ReadEvidenceAsync`; returned metadata preserves MIME type/name and
the HTTP response owns stream disposal. The browser consumes that route.

Before adding the body endpoint, the focused `EvidenceApiTests` backend suite
failed all 4 cases. The implementation opens validated metadata-backed evidence
under the run lock, rejects invalid IDs, reports missing IDs, and translates a
missing published body to explicit storage corruption. The successful body test
reads the actual bytes and response metadata through HTTP rather than only testing
a generated link. Final integrated verification remains the shared gate.

### Coordinated S1/S2/S5/006 gate

`ENOCH_TOOLING=docker just format` and `ENOCH_TOOLING=docker just check` passed
after all owners reached a stable checkpoint. The complete solution compiled with
zero warnings/errors; formatting, Vue types, production bundles, ESLint and
whitespace passed. Backend: 57 passed; client/CLI: 7 passed; browser: 17 passed.
Outputs: `/tmp/enoch-integration-s1-s2-s5-format.log` and
`/tmp/enoch-integration-s1-s2-s5-check.log`. This includes the final S1 binding
status change, S2 mounted-layout correction and real evidence-body HTTP tests.
Independent review approved S1, S2, S5 and ENOCH-006 contracts. Actual mounted
Docker smoke, backup restoration and deployed authorization remain separate
operational gates; this successful check does not establish their results.

The orchestrator then cleared ENOCH-001, ENOCH-003, ENOCH-006, ENOCH-007,
ENOCH-008, ENOCH-009, ENOCH-010 and ENOCH-016 after independent review and that
57/7/17 gate. Implementation reference: the current working-tree source/tests
listed in the active stabilization plan, based on `6db50c0`; no implementation
commit or hosted candidate is claimed. Historical null-bundle damage and broader
publication recovery remain separate limitations.

### Actual local Docker and restoration evidence

The operations implementer ran `ENOCH_TOOLING=docker just operations-test`:
10 passed, 0 failed. The architect had independently run the earlier 9-case
checkpoint successfully. `ENOCH_TOOLING=docker just operations-check` then
passed with exit 0; output: `/tmp/enoch-operations-first.log`. Built image:
`enoch-operations:d1517e4e-b3e3-4fbc-8dc8-cfd71dc227bb`, image ID
`sha256:089a7616a27475ddba33d2ebb4741b4928884afc1703540ea196e76fefcea2f3`.

The real production image used a named volume mounted only at `/data/runs`,
matching the documented layout. It created and fully published a run, rejected
a second API process sharing that volume with `store_in_use`/409, then stopped
the sole writer and executed `just backup`/`just restore` for 14 canonical files
with checksum validation. The restarted API returned exact bundle data,
evidence and binary artifact bytes, terminal metadata and browser entry content.
This is actual mounted creation and local backup restoration evidence, rather
than a simulated directory fixture or an inference from passing unit tests.

`just local-launch` left that verified image running at
`http://127.0.0.1:32768`, container
`enoch-local-4ea57140-1054-4787-847c-7762c9bf0c00`. Inspection confirmed the API
port is bound only to loopback. The generated publisher environment file is
ignored and has mode 600; its value was neither printed nor added to Git. Stop
using `just local-stop enoch-local-4ea57140-1054-4787-847c-7762c9bf0c00`.
Throwaway drill containers were removed. These observations apply to the passed
S1/S2/S5/006 image; subsequent recovery changes require another local image/drill.
No deployed proxy or hosted stabilization-candidate execution is claimed.

## 2026-10-03 Stabilization S3 — recoverable plan and event publication

Retained regressions against actual producer writes failed 4/4 before the journal
implementation (`/tmp/enoch-publication-before.log`): restart sequence, plan retry
identity, and numeric revision ordering. The journal stages replacement bytes,
checksums and an integrity-protected descriptor before an atomic pending-directory
rename decides the commit. Readers and mutations recover under the run lock;
manifest publication is last. Damaged payloads, descriptors or destinations retain
explicit corruption errors and preserve the decision for operator repair.

The core focused gate passed 4/4 (`/tmp/enoch-publication-core-green.log`). Real
write-boundary interruption, concurrent sequence and damaged-journal coverage then
passed 15/15 (`/tmp/enoch-publication-boundaries.log`). Independent review found
that a process stopped during a temporary copy could leave a canonical GUID
`.tmp` file. A retained regression combines an actual producer-created pending
intent with that reachable partial-copy state; it failed 1/1 with cleanup withheld
(`/tmp/enoch-publication-temporary-before.log`). Constrained cleanup preserves
unknown operator files and removes only recognized canonical destinations with
GUID temporary suffixes. Final focused coverage passed 16/16
(`/tmp/enoch-publication-final.log`); independent source review approved it,
conditional on the final combined gate. Callback-thrown interruptions execute
`finally` and are not evidence of an actual process kill. The final Docker drill
will separately exercise SIGKILL/restart/retry.

### Local verification and orchestrator closure follow-up

The orchestrator independently inspected the actual mounted Docker restoration
log and read `http://127.0.0.1:32768/api/v1/runs` with
`curl --fail --silent --show-error`. The sandbox network attempt could not connect;
reviewed network escalation returned HTTP 200 with the active fixture and restored
finished partial run at sequence 4. No credentials were printed. After recorded
mounted-volume evidence and independent review, the orchestrator cleared
ENOCH-004. Current source remains the working tree based on `6db50c0`; this
closure establishes single-owner local creation, not power-loss durability or
shared multiwriter operation.

## 2026-10-03 Stabilization client/CLI — controlled publication failures

The client implementer retained 10 failing `CliFailureTests` using
`ENOCH_TOOLING=docker ./bin/enoch-tool dotnet test tests/Enoch.Client.Tests/Enoch.Client.Tests.csproj --no-restore --filter FullyQualifiedName~CliFailureTests`.
Five reproduced historical defects: transport failure, three invalid
JSON/envelope cases, and a silently ignored result outcome. Five established new
contract tests: four invalid timeout values and a configured fractional timeout;
the latter reached the fixture's 5-second budget before the new deadline support,
which is not proof of a previously broken timeout contract.

The first broader focused gate passed 20 cases. Independent review and an actual
Docker consumer exposed a progress-envelope compatibility regression: the event
had committed, but the CLI returned exit 1/502. A retained focused event-response
regression failed before the parser correction. The final client-project gate
passed 21 tests. The reviewer then ran `just cli-docker-check` successfully for
start, plan, progress, evidence, artifact, result, read and explicit finish;
rejected result outcomes left storage unchanged and downloaded bytes matched.
Output: `/tmp/enoch-cli-docker-after.log`, exit 0. Final canonical integration is
still pending.

`PublishResultAsync` intentionally removes the ignored outcome source parameter;
result CLI `--outcome` now rejects before file/network access and help omits it.
Finish remains explicit. Request timeout defaults to 100 seconds with a validated
positive finite fractional `ENOCH_TIMEOUT_SECONDS` override. The linked deadline
covers response headers and body; caller cancellation remains cancellation.
Transport, timeout and invalid response failures produce controlled errors and
CLI exit 1 without a stack trace; HTTP responses are disposed.

### Resource regression checkpoint

Direct-store regressions genuinely failed 5/5 before core integration
(`/tmp/enoch-s6-resource-red.log`): upload consumption exceeded limit+1, invalid
IDs retained more gates, a pre-cancelled read succeeded, a cancelled gate waiter
did not stop, and warm mutations repeatedly hashed an unchanged artifact.
The corrected actual-route cancellation fixture failed 17/18 cases (the evidence
route already passed), output `/tmp/enoch-s6-api-red.log`. Cleanup-retry and
pre/post commit-decision cancellation cases additionally specify the new journal
contract; they are not counted as historical defect reproductions.

Operations scripts are now stable; the final independent
`ENOCH_TOOLING=docker just operations-test` passed 11/11 with no skips
(`/tmp/enoch-operations-unit-final.log`). The architect independently passed the
same 11-case checkpoint (`/tmp/enoch-operations-independent-final.log`). Actual
SIGKILL/restart and Kestrel chunked 64 MiB boundary checks await the final image.

## 2026-10-03 Stabilization S4 — remaining mutations and terminal recovery

Retained focused regressions failed 9/10 before the remaining mutation refactor
(`/tmp/enoch-terminal-before.log`). They reproduced partial publication after
checksum failure, missing retained decisions for evidence/result/wait, and unsafe
terminal retry behavior; the old artifact precommit cleanup case already passed.
Evidence, artifact, result, wait/resume and finish now share the typed journal
pipeline. Artifact bodies stage through a bounded stream and replay validation
hashes the retained stream without a whole-body allocation. Evidence/artifact
metadata creation time matches the same committed manifest update.

`ENOCH_TOOLING=docker ./bin/enoch-tool dotnet format --no-restore` completed;
focused `dotnet test tests/Enoch.Backend.Tests/Enoch.Backend.Tests.csproj --no-restore --filter FullyQualifiedName~TerminalPublicationTests`
passed 10/10 (`/tmp/enoch-terminal-green.log`). This includes same-outcome but
changed-summary rejection with unchanged manifest/checksum. Exact finish retries
preserve FinishedAt/UpdatedAt. Independent source review approved S4 conditional
on the final canonical gate. Additional null/empty summary distinction and staged
creation cancellation contract cases were added afterward and await that gate.

The reviewer tested the suspected MIME-download failure against a real temporary
Docker API: malformed and empty evidence MIME both published and returned exact
body bytes with HTTP 200. The probe disproved the hypothesis; no MIME rejection
was added. The task-owned probe container/volume were cleaned and the existing
local instance remained running.

### Integrated recovery/resource checkpoint

The integrated backend compiled cleanly and ran 123 cases: 122 passed, 1 failed
(`/tmp/enoch-integrated-backend-test.log`). All storage recovery, terminal,
legacy-compatibility, upload-boundary and fixed-gate tests passed. The outstanding
failure was the GET run-index cancellation fixture's expectation of a client
exception after request cancellation; diagnosis and final canonical verification
remain pending. This result is not recorded as a clean gate.

Normal mutations now append numeric event segments without rewriting legacy
`events.jsonl`. The first mutation bootstraps the canonical checksum inventory
from actual file hashes; warm commits update hashes for changed files only.
The cache retains metadata/identity locators for at most one run per fixed stripe
(64 slots), with no event payloads or reader arrays retained. Per-run inventory
and checksum serialization still grow with history. All postdecision failures
invalidate retained metadata, including failures after journal retirement.
Recognized temporary cleanup runs once per owned stripe/run and again after
external recovery or failed preparation/commit; successful warm commits preserve
the clean marker.

Legacy bundles remain readable when their shared plan/event sequences agree with
the manifest. Behind, ahead, duplicate or finished mismatches without an authentic
retained decision return `storage_corrupt` and preserve files for operator repair.
The new compatibility cases specify this conservative policy; the attempted
preintegration probe stopped on a fixture analyzer before test execution, so no
historical failing count is claimed for that new policy. Numeric ordering coverage
now uses a coherent 9,998-event legacy history plus revisions 9,999 and 10,000
with matching manifest/checksums.

Artifact streaming consumes at most the established 64 MiB limit plus one byte
before rejecting excess input, preserves prior publication on rejection, and
hashes with bounded buffers both while staging and while validating replay.
Creation and request operations pass cancellation into lock waits, reads and
precommit writes/hash I/O. Three actual creation checkpoint tests verify no
published bundle after pre-cancel, manifest preparation or completed preparation
cancellation. Cancellation after a journal commit decision does not abandon the
retained decision; recovery/retry converges to the committed publication.

Independent final review found that the event response cloned caller data through
a second serialization. A deterministic changing-getter regression failed 1/1:
committed data contained `value:1`, while the returned response contained
`value:2` (`/tmp/enoch-snapshot-before.log`). The fix serializes once, stages those
bytes and deserializes the response from the exact same bytes. The integrated API
cancellation failure was a TestServer cancellation/error-response race: the
fixture now requires actual captured storage cancellation and accepts either a
client cancellation or a non-success response, never a successful response. These
changes await the final canonical gate below.

### Final coordinated source gate

`ENOCH_TOOLING=docker just format` and `ENOCH_TOOLING=docker just check` both
passed, exit 0. Outputs: `/tmp/enoch-stabilization-final-format.log` and
`/tmp/enoch-stabilization-final-check.log`. The solution built with zero warnings
and errors; formatting, Vue types, production builds, ESLint, whitespace and all
aggregated suites passed: **124 backend, 21 client/CLI, 17 browser and 11 operations
cases**, no failures or skips. This gate includes the corrected API cancellation
fixture, single-serialization snapshot regression, coherent numeric ordering,
legacy preservation, resource bounds and pre/post decision cancellation contracts.

Independent final source review approved the integrated journal, streamed
validation, cache invalidation, warm cleanup behavior and creation cancellation
without remaining source blockers. Implementation reference is the current
working tree based on `6db50c0`, including `FileSystemRunStore.cs` and the
Publication/Resources/History partials plus their backend tests, application/API
stream contracts, client/CLI changes, browser changes and operations automation.
No new implementation commit, hosted-candidate run or publication is claimed.
Final real Docker SIGKILL/restart/retry, Kestrel chunked exact-limit/excess, restored
large-body read-back and refreshed local launch remain the operational gate; the
currently running earlier image does not establish those new source results.

### Actual final-image restore regression

The first final-image operational drill passed the real SIGKILL publication burst,
chunked exact 64 MiB/excess upload boundary, competing-owner rejection and
checksum-validated backup/restore of 121 canonical files. It then failed on the
restored empty active run: backup correctly omits empty optional directories, but
new history validation unconditionally enumerated `plans/`. Output:
`/tmp/enoch-operations-final.log`. This is a failed overall operational gate; the
existing local user instance was not upgraded or altered.

A retained direct-store regression creates an empty valid bundle, releases its
owner, removes its empty plans/evidence/artifact directories as restoration does,
then reopens the store and reads/publishes. It failed 1/1 with `storage_corrupt`
(`/tmp/enoch-empty-restore-before.log`). The bounded compatibility fix treats
missing optional plan directories as empty while preserving strict manifest/shared
sequence validation. Updated source verification and a complete new-image
operational rerun remain required.

### Verified empty-bundle restoration correction

The architect added existence guards around plan and receipt enumeration;
independent review approved preservation of valid absent optional directories and
continued rejection of missing claimed history. The full required
`ENOCH_TOOLING=docker just format` and `ENOCH_TOOLING=docker just check` then
passed, exit 0: **125 backend, 21 client/CLI, 17 browser, 11 operations tests**,
no failures/skips and zero build warnings/errors. Outputs:
`/tmp/enoch-stabilization-restore-format.log` and
`/tmp/enoch-stabilization-restore-check.log`. The new empty-directory regression
reads the restored bundle with a fresh owner and successfully publishes plan
sequence 1 and event sequence 2. This supersedes the 124-case source checkpoint;
all source owners froze the verified candidate for a complete operational rerun.

### Complete final production-image operational rerun

The reviewer reran `ENOCH_TOOLING=docker just operations-check` against the
corrected verified source. It passed, exit 0; output:
`/tmp/enoch-operations-final-retry.log`. Image:
`enoch-operations:120e4ebf-83d5-49fe-b2f9-23859a4c7454`. The earlier failed
operational log remains retained.

A real SIGKILL interrupted the bounded publication burst after two acknowledgments.
After restart, all 64 plan/event identities retried to original acknowledged
responses and consecutive sequences. Real Kestrel accepted a chunked exact
64 MiB artifact with matching hash and streamed download, rejected one extra byte
with HTTP 413 and an unchanged bundle, and rejected a competing owner with
`store_in_use`/409. With the sole writer stopped, backup/restore validated 121
canonical files. The restarted API returned every restored bundle including the
empty active run, exact large artifact bytes, static browser entry and expected
authorization responses. This proves actual local process interruption and
quiescent restoration; no power-loss or simultaneous multiwriter guarantee is
claimed. Existing-instance upgrade and final CLI verification are recorded
separately once completed.

### Preserved local-instance upgrade and actual CLI verification

`bash /tmp/enoch-local-upgrade.sh` passed, exit 0; output:
`/tmp/enoch-local-upgrade-final.log`. The script invoked the real
`just cli-docker-check` against the verified final image. Before replacement,
the sole writer stopped and a quiesced backup captured 25 canonical files in
`.cache/local-upgrade/be2135e5-f0dd-4697-99a9-ea39d893db46/saved`.
All three preexisting bundles and four streamed blob bodies matched exactly after
upgrade. Fresh CLI start/plan/progress/evidence/artifact/result/read/explicit
finish all passed, and rejected result outcome flags left publication unchanged.
The authored CLI fixture run was
`20261003212500-3dc6ec0d950540d795a963a38da25863`.

The final image is now running at the unchanged `http://127.0.0.1:32768`,
container `enoch-local-be2135e5-f0dd-4697-99a9-ea39d893db46`, image ID
`sha256:1c543f64cac5a4769aa5f59470aeaeda132dfae933a0dcc0186e7e3748f8bd30`.
Inspection confirmed only loopback port publication and the original
`enoch-local-4ea57140-1054-4787-847c-7762c9bf0c00` volume at `/data/runs`.
The original ignored publisher environment file remains unchanged with mode 600;
its value was never printed. The old container is stopped, exactly one task app
is running, and throwaway drill containers were cleaned. Actual HTTP reads returned
200 for browser HTML and the run-index API. Stop with
`just local-stop enoch-local-be2135e5-f0dd-4697-99a9-ea39d893db46`.
The final local runtime, restored data and real CLI consumer gates are complete;
no hosted-candidate publication or live proxy deployment is claimed.

The orchestrator independently inspected the upgrade log's exact comparison of
three existing bundles/four blob bodies and successful CLI publication families.
Approved read-only localhost verification then returned HTTP 200 for both `/`
and `/api/v1/runs` on the upgraded instance. No credentials were printed. Final
issue-register closure follows this recorded verification and independent review.

### Verified orchestrator issue closure

After independently inspecting the superseding 125/21/17/11 source gate,
complete final Docker run, preserved-upgrade/CLI logs and actual reader/API HTTP
200 responses, the orchestrator applied the independently reviewed final
PROBLEMS-only patch. It cleared ENOCH-002, ENOCH-005, ENOCH-011, ENOCH-013,
ENOCH-014, ENOCH-015, ENOCH-017, ENOCH-018, ENOCH-019, ENOCH-020, ENOCH-021,
ENOCH-028 and ENOCH-031. All 21 application review defects ENOCH-001 through
ENOCH-021 are now resolved, together with the accepted operations/regression gaps
028/031. Other feature/deployment gaps remain in the canonical register. Current
working-tree source and the verified running image remain unchanged; no commit,
hosted-candidate execution or external deployment is claimed.

### Final documentation and unchanged-source verification

After the orchestrator closure and completed plan update,
`ENOCH_TOOLING=docker just format` and `ENOCH_TOOLING=docker just format-check`
passed. Outputs: `/tmp/enoch-final-docs-format.log` and
`/tmp/enoch-final-docs-format-check.log`. `git diff --check` passed, and a local
link/heading review verified 74 relative Markdown links across 10 changed
Markdown documents. Application suites were not repeated after documentation-only
changes; the superseding source gate remains 125/21/17/11.

The sorted non-Markdown tracked/untracked repository files (71 source, test,
configuration and tooling files) had the same combined SHA256 before and after
this final gate:
`6140ea9787346f3978cff110f3a5fc928049bc9331733ed98cbe4e91634f2542`.
Snapshots: `/tmp/enoch-final-source-before.sha256` and
`/tmp/enoch-final-source-after.sha256`. The running verified application source
remained unchanged throughout final documentation handling.

### Requested local stop and release preparation

The user requested stopping the local app, committing the verified changes,
tagging and pushing them. `just local-stop enoch-local-be2135e5-f0dd-4697-99a9-ea39d893db46`
succeeded after reviewed Docker socket access; the initial sandbox attempt was
denied. Independent `docker inspect --format '{{.State.Status}}' enoch-local-be2135e5-f0dd-4697-99a9-ea39d893db46`
returned `exited`. Stopping preserved the container, original data volume and
publisher credentials.

Read-only remote inspection confirmed `origin/main` still points to
`6db50c098d565d6ae3fae8f2f44b492b308ecef4`, with no existing remote tags.
The sandbox SSH configuration rejected the initial attempt; reviewed network
access succeeded. Annotated tag `v0.1.0` was selected to match the repository's
existing version. Application source remains the previously verified candidate;
only operational documentation changed during this release preparation.

Release preparation rechecked all 71 non-Markdown source/configuration/tooling
files against the verified stabilization snapshot; the combined SHA256 remained
`6140ea9787346f3978cff110f3a5fc928049bc9331733ed98cbe4e91634f2542`.
Only documentation changed after the successful 125/21/17/11 application gate;
application suites were not repeated for these operational documentation edits.

`ENOCH_TOOLING=docker just format` passed for the stop/release documentation
update; output: `/tmp/enoch-release-docs-format.log`. The previously verified
application source remained frozen during release preparation.
