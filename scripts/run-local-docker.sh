#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"
operation="${1:-}"
case "$operation" in
  launch)
    if [[ ! -f .cache/operations/last-image || ! -f .cache/operations/last-restored ]]; then
      echo 'Run just operations-check successfully before launching its verified image.' >&2
      exit 1
    fi
    read -r image < .cache/operations/last-image
    read -r restored < .cache/operations/last-restored
    task_id="$(cat /proc/sys/kernel/random/uuid)"
    container="enoch-local-$task_id"
    volume="$container"
    state_relative=".cache/local-docker/$task_id"
    mkdir -p "$state_relative"
    secret_file="$repo_root/$state_relative/publisher.env"
    (umask 077; printf 'ENOCH_TOKEN=%s\n' "$(od -An -N32 -tx1 /dev/urandom | tr -d ' \n')" > "$secret_file")
    docker volume create "$volume" >/dev/null
    started=false
    cleanup_failed_launch() {
      if [[ "$started" == false ]]; then
        docker rm --force --volumes "$container" >/dev/null 2>&1 || true
        docker volume rm "$volume" >/dev/null 2>&1 || true
      fi
    }
    trap cleanup_failed_launch EXIT
    docker run --rm --mount "source=$volume,target=/data/runs" \
      --volume "$repo_root:/workspace:ro" --env "RESTORED_RUNS=/workspace/$restored/runs" \
      node:22-alpine node --input-type=module -e \
      'import { cp } from "node:fs/promises"; await cp(process.env.RESTORED_RUNS, "/data/runs", {recursive:true});'
    docker run --detach --name "$container" --publish 127.0.0.1::8080 \
      --env-file "$secret_file" --mount "source=$volume,target=/data/runs" "$image" >/dev/null
    docker run --rm --network "container:$container" --env-file "$secret_file" \
      --user "$(id -u):$(id -g)" --volume "$repo_root:/workspace:ro" --workdir /workspace \
      node:22-alpine node scripts/check-operations.mjs health http://127.0.0.1:8080
    address="$(docker port "$container" 8080/tcp)"
    printf 'URL=http://%s\nCONTAINER=%s\nVOLUME=%s\nTOKEN_FILE=%s\nIMAGE=%s\n' \
      "$address" "$container" "$volume" "$state_relative/publisher.env" "$image" > "$state_relative/state.env"
    printf '%s\n' "$state_relative" > .cache/local-docker/last-running
    started=true
    printf 'URL=http://%s\nCONTAINER=%s\nTOKEN_FILE=%s\nSTOP=just local-stop %s\n' \
      "$address" "$container" "$state_relative/publisher.env" "$container"
    ;;
  stop)
    container="${2:-}"
    if [[ ! "$container" =~ ^enoch-local-[0-9a-f-]{36}$ ]]; then
      echo 'Supply the exact task-owned enoch-local container name.' >&2
      exit 2
    fi
    docker stop "$container"
    ;;
  *) echo 'Usage: run-local-docker.sh launch|stop [container]' >&2; exit 2 ;;
esac
