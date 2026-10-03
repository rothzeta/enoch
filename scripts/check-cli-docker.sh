#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"
if [[ ! -f .cache/local-docker/last-running ]]; then
  echo 'Launch a checked local container with just local-launch first.' >&2
  exit 1
fi
read -r state_relative < .cache/local-docker/last-running
state_file="$state_relative/state.env"
container="$(sed -n 's/^CONTAINER=//p' "$state_file")"
secret_file="$repo_root/$state_relative/publisher.env"
if [[ ! "$container" =~ ^enoch-local-[0-9a-f-]{36}$ ]]; then
  echo 'Recorded local container is not task-owned.' >&2
  exit 1
fi
fixture_relative=".cache/cli-check/$(cat /proc/sys/kernel/random/uuid)"
mkdir -p "$fixture_relative"
printf 'CLI authored request\n' > "$fixture_relative/request.txt"
printf 'CLI authored plan\n' > "$fixture_relative/plan.txt"
printf 'CLI authored result\n' > "$fixture_relative/result.txt"
printf 'CLI authored evidence λ\n' > "$fixture_relative/evidence.txt"
printf '\000\001\177\377' > "$fixture_relative/artifact.bin"
run_cli() {
  docker run --rm --network "container:$container" --env-file "$secret_file" \
    --env ENOCH_URL=http://127.0.0.1:8080 --env ENOCH_TIMEOUT_SECONDS=5 \
    --user "$(id -u):$(id -g)" --volume "$repo_root:/workspace:ro" --workdir /workspace \
    mcr.microsoft.com/dotnet/sdk:8.0 dotnet src/Enoch.Cli/bin/Debug/net8.0/Enoch.Cli.dll "$@"
}
run_id="$(run_cli start --title 'CLI Docker compatibility' --request "$fixture_relative/request.txt")"
printf '%s\n' "$run_id" > "$fixture_relative/run-id"
run_cli plan --run "$run_id" --file "$fixture_relative/plan.txt"
run_cli progress --run "$run_id" --event-id cli-docker-progress --message 'CLI compatibility progress'
run_cli evidence --run "$run_id" --name notes.txt --file "$fixture_relative/evidence.txt"
run_cli artifact --run "$run_id" --file "$fixture_relative/artifact.bin"
run_cli result --run "$run_id" --file "$fixture_relative/result.txt"
run_cli read --run "$run_id" > "$fixture_relative/before-finish.json"
if run_cli result --run "$run_id" --file "$fixture_relative/missing.txt" --outcome failed > "$fixture_relative/rejected-output.txt" 2> "$fixture_relative/rejected-error.txt"; then
  echo 'Unsupported result outcome unexpectedly succeeded.' >&2
  exit 1
fi
run_cli read --run "$run_id" > "$fixture_relative/after-rejection.json"
run_cli finish --run "$run_id" --outcome failed --summary 'CLI explicit finish verified'
run_cli read --run "$run_id" > "$fixture_relative/after-finish.json"
docker run --rm --network "container:$container" \
  --user "$(id -u):$(id -g)" --volume "$repo_root:/workspace:ro" --workdir /workspace \
  node:22-alpine node scripts/check-operations.mjs cli http://127.0.0.1:8080 "$fixture_relative"
printf 'CLI Docker compatibility passed for authored run %s\n' "$run_id"
