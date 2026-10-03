# Task logs

Record dated work, evidence, and limitations here. Plans and standalone tasks follow [ADR-0002](adr/0002-plan-filenames.md) and [ADR-0003](adr/0003-implementation-plan-writing.md); this note records their actual execution.

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
