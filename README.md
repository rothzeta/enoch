# Enoch

[![CI](https://github.com/rothzeta/enoch/actions/workflows/container.yml/badge.svg?branch=main)](https://github.com/rothzeta/enoch/actions/workflows/container.yml)
[![Latest tag](https://img.shields.io/github/v/tag/rothzeta/enoch)](https://github.com/rothzeta/enoch/tags)
[![Renovate](https://img.shields.io/badge/dependencies-Renovate-blue)](https://github.com/rothzeta/enoch/issues?q=is%3Aissue%20is%3Aopen%20%22Dependency%20Dashboard%22)

Enoch publishes durable, human-readable records of agent work. A run captures the request, plan revisions, semantic progress events, evidence, artifacts, result, and terminal outcome. It is deliberately a publication system rather than an agent runtime or transcript store.

V1 is one ASP.NET Core application serving a Vue 3 static UI and a versioned HTTP API. The canonical store is a filesystem bundle below `${ENOCH_DATA}/runs/<run-id>`; no database, broker, or cache is required.

The documentation is an Obsidian vault rooted at [`docs/`](docs/README.md). Its [schema](docs/SCHEMA.md), [current state](docs/CURRENT.md), [problems](docs/PROBLEMS.md), and [task logs](docs/TASK_LOGS.md) distinguish decisions, planned work, implemented behavior, unresolved issues, and verification evidence. Only the orchestrator agent may clear problem entries.

## Build and test

Requirements: `just`, the .NET 8 SDK selected by `global.json`, and Node.js 22.

```sh
just install
just format
just check
```

Run `just` to list individual lint, type-check, build, and test recipes. Use
`ENOCH_TOOLING=docker just install` and `ENOCH_TOOLING=docker just check` with
Docker instead of local .NET and Node tools. See the [development rules](docs/exploitation/development.md)
and [repository tooling decision](docs/adr/0004-repository-tooling.md).

Run the API locally with a non-empty publisher token:

```sh
ENOCH_DATA="$PWD/data" ENOCH_TOKEN='development-only-token' \
  dotnet run --project src/Enoch.Api
```

The production container builds the Vue application into the API's `wwwroot`, so `/` and `/runs/<id>` serve the browser view. The API exposes `/health` for orchestration checks.

## Dependency updates and branches

Renovate creates normal update branches weekly, on Mondays between 00:00 and
06:00 UTC. Patch and minor updates merge automatically only after all checks pass
and the dependency release is at least three days old. The cooldown starts at the
upstream release date, not the PR creation date. Updates without a release
timestamp stay pending. Major upgrades and other update types remain PRs for
manual review. Security fixes bypass the weekly schedule and cooldown; patch and
minor security fixes still require passing checks, and major security upgrades
still need manual review. Existing PRs can be rebased and merged outside the weekly
creation window. Validate configuration changes with `just renovate-check`
(Docker required; the pinned validator uses Node 24 separately from the app).

GitHub's dependency graph and Dependabot alerts supply security advisories to
Renovate. Renovate owns dependency update PRs; enabling alerts does not require
enabling a second bot's automatic security PRs. GitHub Actions and container images
are pinned by digest through Renovate PRs.

`main` is the default integration branch. Feature and Renovate branches are
temporary PR branches and are deleted after merging. GitHub protection requires
an up-to-date PR with successful `check` and container `build` jobs, resolved
conversations, and no force pushes or branch deletion. These rules include
administrators. No mandatory approval count is set, so a solo maintainer and
Renovate can merge passing PRs; major upgrades remain a manual merge decision.

Badges are linked Markdown images at the top of this file. The CI badge reports
the workflow on `main`, the tag badge links to version tags, and
the Renovate badge links to the Dependency Dashboard.

## CLI-first publication

The CLI reads `ENOCH_URL`, `ENOCH_TOKEN`, and optionally `ENOCH_RUN_ID`:

```sh
export ENOCH_URL=http://localhost:5000
export ENOCH_TOKEN='development-only-token'

run_id="$(dotnet run --project src/Enoch.Cli -- start --title 'Example' --request request.md)"
export ENOCH_RUN_ID="$run_id"
dotnet run --project src/Enoch.Cli -- plan --file plan.json
dotnet run --project src/Enoch.Cli -- progress --message 'Implemented the storage layer' --event-id storage-done
dotnet run --project src/Enoch.Cli -- artifact --file output.txt
dotnet run --project src/Enoch.Cli -- result --file result.md
dotnet run --project src/Enoch.Cli -- finish --outcome success
dotnet run --project src/Enoch.Cli -- read
```

Publishing requires an Authorization: Bearer token. Read endpoints are intentionally left to the deployment proxy's human-auth boundary. Finished runs reject every mutation.

## Storage

Each run is stored as a self-contained bundle containing `manifest.json`, immutable `request.json`, numbered plans, `events.jsonl`, optional `result.json`, evidence and artifact directories, and `checksums.sha256`. Active-run mutations are serialized per run and published through same-filesystem atomic renames. An index can therefore be rebuilt by enumerating run manifests.

See [deployment.md](docs/exploitation/deployment.md) for the production Compose input and live estate deployment notes.
