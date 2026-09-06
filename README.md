# Enoch

Enoch publishes durable, human-readable records of agent work. A run captures the request, plan revisions, semantic progress events, evidence, artifacts, result, and terminal outcome. It is deliberately a publication system rather than an agent runtime or transcript store.

V1 is one ASP.NET Core application serving a Vue 3 static UI and a versioned HTTP API. The canonical store is a filesystem bundle below `${ENOCH_DATA}/runs/<run-id>`; no database, broker, or cache is required.

## Build and test

Requirements: .NET 8 SDK and Node.js 22.

```sh
dotnet restore Enoch.sln
dotnet build Enoch.sln --no-restore
dotnet test Enoch.sln --no-build

cd src/Enoch.Ui
npm ci
npm test
npm run build
```

Run the API locally with a non-empty publisher token:

```sh
ENOCH_DATA="$PWD/data" ENOCH_TOKEN='development-only-token' \
  dotnet run --project src/Enoch.Api
```

The production container builds the Vue application into the API's `wwwroot`, so `/` and `/runs/<id>` serve the browser view. The API exposes `/health` for orchestration checks.

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

Publishing requires `Authorization: Bearer <ENOCH_TOKEN>`. Read endpoints are intentionally left to the deployment proxy's human-auth boundary. Finished runs reject every mutation.

## Storage

Each run is stored as a self-contained bundle containing `manifest.json`, immutable `request.json`, numbered plans, `events.jsonl`, optional `result.json`, evidence and artifact directories, and `checksums.sha256`. Active-run mutations are serialized per run and published through same-filesystem atomic renames. An index can therefore be rebuilt by enumerating run manifests.

See [deployment.md](docs/deployment.md) for the production Compose input and the intentionally blocked estate rollout.
