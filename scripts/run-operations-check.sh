#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"
task_id="$(cat /proc/sys/kernel/random/uuid)"
fixture_relative=".cache/operations/$task_id"
mkdir -p "$fixture_relative/source"
network="enoch-operations-$task_id"
volume="enoch-operations-$task_id"
api="enoch-operations-$task_id"
contender="enoch-operations-contender-$task_id"
burst="enoch-operations-burst-$task_id"
image="enoch-operations:$task_id"
export ENOCH_TOKEN="$(od -An -N32 -tx1 /dev/urandom | tr -d ' \n')"
cleanup() {
  docker rm --force --volumes "$api" "$contender" "$burst" >/dev/null 2>&1 || true
  docker network rm "$network" >/dev/null 2>&1 || true
  docker volume rm "$volume" >/dev/null 2>&1 || true
}
trap cleanup EXIT

docker build --tag "$image" .
docker network create --internal "$network" >/dev/null
docker volume create "$volume" >/dev/null
docker run --detach --name "$api" --network "$network" --network-alias enoch \
  --env ENOCH_TOKEN --mount "source=$volume,target=/data/runs" "$image" >/dev/null
run_helper() {
  docker run --rm --network "$network" --env ENOCH_TOKEN \
    --user "$(id -u):$(id -g)" --volume "$repo_root:/workspace" --workdir /workspace \
    node:22-alpine node scripts/check-operations.mjs "$1" http://enoch:8080 "$fixture_relative/fixture.json"
}
run_helper seed
docker run --rm --name "$burst" --network "$network" --env ENOCH_TOKEN \
  --user "$(id -u):$(id -g)" --volume "$repo_root:/workspace" --workdir /workspace \
  node:22-alpine node scripts/check-operations.mjs burst http://enoch:8080 "$fixture_relative/fixture.json" &
burst_pid=$!
for attempt in {1..500}; do
  if [[ -f "$fixture_relative/fixture.json.kill-ready" ]]; then break; fi
  if ! kill -0 "$burst_pid" 2>/dev/null; then
    wait "$burst_pid"
    echo 'Publication burst ended before the process-kill checkpoint.' >&2
    exit 1
  fi
  sleep 0.02
done
if [[ ! -f "$fixture_relative/fixture.json.kill-ready" || -f "$fixture_relative/fixture.json.burst-complete" ]]; then
  echo 'Publication burst did not reach a live process-kill checkpoint.' >&2
  exit 1
fi
docker kill --signal KILL "$api" >/dev/null
wait "$burst_pid"
docker start "$api" >/dev/null
run_helper recover-burst
run_helper bounded-upload
docker run --detach --name "$contender" --network "$network" --network-alias contender \
  --env ENOCH_TOKEN --mount "source=$volume,target=/data/runs" "$image" >/dev/null
docker run --rm --network "$network" --env ENOCH_TOKEN \
  --user "$(id -u):$(id -g)" --volume "$repo_root:/workspace:ro" --workdir /workspace \
  node:22-alpine node scripts/check-operations.mjs owner http://contender:8080
docker rm --force --volumes "$contender" >/dev/null
docker stop "$api" >/dev/null
docker cp "$api:/data/runs" "$repo_root/$fixture_relative/source/runs"
docker rm --volumes "$api" >/dev/null
ENOCH_TOOLING=docker just backup "$fixture_relative/source" "$fixture_relative/saved" --quiesced
ENOCH_TOOLING=docker just restore "$fixture_relative/saved" "$fixture_relative/restored"
docker run --detach --name "$api" --network "$network" --network-alias enoch \
  --env ENOCH_TOKEN --mount "type=bind,source=$repo_root/$fixture_relative/restored/runs,target=/data/runs" "$image" >/dev/null
run_helper verify
printf '%s\n' "$image" > .cache/operations/last-image
printf '%s\n' "$fixture_relative/restored" > .cache/operations/last-restored
printf 'Local operational drill passed; image %s; fixture %s\n' "$image" "$fixture_relative"
